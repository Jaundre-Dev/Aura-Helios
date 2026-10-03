using System.Security.Claims;
using Helios.Api.Middleware;
using Helios.Api.Security;
using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Common;
using Helios.Contracts.Identity;
using Helios.Infrastructure.Persistence.MySql.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Helios.Api.Endpoints;

/// <summary>
/// Local sign-in. Register and login are anonymous and rate limited; everything else needs a
/// valid token whose account, session and workspace grant are re-checked on every request
/// (<see cref="SessionValidator"/>). A user signs in first, then selects a workspace, which
/// re-issues a token scoped to it.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Authentication");

        group.MapPost("/register", async (
                RegisterRequest request,
                UserManager<HeliosUser> users,
                JwtTokenIssuer issuer,
                CancellationToken ct) =>
            {
                var user = new HeliosUser
                {
                    UserName = request.Email,
                    Email = request.Email,
                    DisplayName = request.DisplayName?.Trim()
                };

                var result = await users.CreateAsync(user, request.Password);

                if (!result.Succeeded)
                {
                    // Identity owns the password and uniqueness rules; surface its messages
                    // rather than re-implementing them, grouped like every other 400.
                    var errors = result.Errors
                        .GroupBy(e => e.Code)
                        .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

                    return Results.ValidationProblem(errors, title: "Registration failed.");
                }

                var auth = issuer.Issue(user.Id, user.Email!, user.DisplayName, user.SecurityStamp!, workspaceId: null, role: null);

                return Results.Created($"/api/v1/auth/me", auth);
            })
            .WithName("Register")
            .RequireRateLimiting(AuthRateLimiting.PolicyName)
            .WithValidation<RegisterRequest>()
            .Produces<AuthResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/login", async (
                LoginRequest request,
                UserManager<HeliosUser> users,
                IHeliosDbContext db,
                IAuditWriter audit,
                JwtTokenIssuer issuer,
                TimeProvider clock,
                CancellationToken ct) =>
            {
                var user = await users.FindByEmailAsync(request.Email);

                // One response for "no such user", "wrong password", "locked out" and "disabled",
                // so the endpoint is not an account-enumeration or lockout-probing oracle.
                var failed = Results.Problem(
                    title: "Sign-in failed",
                    detail: "Invalid email or password.",
                    statusCode: StatusCodes.Status401Unauthorized);

                if (user is null)
                {
                    return failed;
                }

                // Lockout is checked before the password. Otherwise a locked account with the
                // right password would still sign in, and lockout would only slow guessing.
                if (await users.IsLockedOutAsync(user))
                {
                    RecordSignIn(audit, user.Id, allowed: false, "locked_out");
                    await db.SaveChangesAsync(ct);
                    return failed;
                }

                if (!user.IsActive)
                {
                    RecordSignIn(audit, user.Id, allowed: false, "inactive");
                    await db.SaveChangesAsync(ct);
                    return failed;
                }

                if (!await users.CheckPasswordAsync(user, request.Password))
                {
                    // Persists the failure count and, at the threshold, the lockout end.
                    await users.AccessFailedAsync(user);

                    var nowLocked = await users.IsLockedOutAsync(user);
                    RecordSignIn(audit, user.Id, allowed: false, nowLocked ? "bad_password_locked" : "bad_password");
                    await db.SaveChangesAsync(ct);
                    return failed;
                }

                await users.ResetAccessFailedCountAsync(user);

                // Seed the workspace claim from the last selection, but only if that grant is
                // still live — access can be revoked between sessions.
                Guid? workspaceId = null;
                WorkspaceRole? role = null;

                if (user.DefaultWorkspaceId is { } previous &&
                    await FindLiveGrantAsync(db, previous, user.Id, ct) is { } grant)
                {
                    workspaceId = previous;
                    role = grant;
                }

                user.LastSignInAt = clock.GetUtcNow();
                await users.UpdateAsync(user);

                RecordSignIn(audit, user.Id, allowed: true, null);
                await db.SaveChangesAsync(ct);

                var auth = issuer.Issue(user.Id, user.Email!, user.DisplayName, user.SecurityStamp!, workspaceId, role);

                return Results.Ok(auth);
            })
            .WithName("Login")
            .RequireRateLimiting(AuthRateLimiting.PolicyName)
            .WithValidation<LoginRequest>()
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/select-workspace", async (
                SelectWorkspaceRequest request,
                ClaimsPrincipal principal,
                UserManager<HeliosUser> users,
                IHeliosDbContext db,
                JwtTokenIssuer issuer,
                CancellationToken ct) =>
            {
                var userId = principal.UserId() ?? throw new UnauthenticatedException();

                // 404, not 403: telling a stranger the workspace is real leaks other tenants,
                // exactly as the workspace GET does. Inactive workspaces and companies, and
                // grants whose company membership was removed, are treated the same way.
                var role = await FindLiveGrantAsync(db, request.WorkspaceId, userId, ct)
                    ?? throw new NotFoundException("Workspace", request.WorkspaceId);

                var user = await users.FindByIdAsync(userId.ToString())
                    ?? throw new UnauthenticatedException();

                user.DefaultWorkspaceId = request.WorkspaceId;
                await users.UpdateAsync(user);

                var auth = issuer.Issue(
                    user.Id, user.Email!, user.DisplayName, user.SecurityStamp!, request.WorkspaceId, role);

                return Results.Ok(auth);
            })
            .WithName("SelectWorkspace")
            .WithValidation<SelectWorkspaceRequest>()
            .RequireAuthorization()
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/revoke-sessions", async (
                ClaimsPrincipal principal,
                UserManager<HeliosUser> users,
                IHeliosDbContext db,
                IAuditWriter audit,
                CancellationToken ct) =>
            {
                var userId = principal.UserId() ?? throw new UnauthenticatedException();
                var user = await users.FindByIdAsync(userId.ToString())
                    ?? throw new UnauthenticatedException();

                // Every token carries the stamp it was issued under; rotating it invalidates
                // all of them, including the one making this request.
                await users.UpdateSecurityStampAsync(user);

                audit.Record("auth.sessions.revoke", "User", user.Id.ToString());
                await db.SaveChangesAsync(ct);

                return Results.NoContent();
            })
            .WithName("RevokeSessions")
            .WithSummary("Signs the caller out everywhere by invalidating every token issued so far.")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/me", (ClaimsPrincipal principal) =>
            {
                if (principal.UserId() is not { } userId)
                {
                    throw new UnauthenticatedException();
                }

                // Claims were re-validated against the database by SessionValidator.
                return Results.Ok(new AuthenticatedUser(
                    userId,
                    principal.Email() ?? string.Empty,
                    principal.DisplayName(),
                    principal.WorkspaceId(),
                    principal.Role()));
            })
            .WithName("Me")
            .RequireAuthorization()
            .Produces<AuthenticatedUser>();

        return app;
    }

    /// <summary>
    /// The caller's role in a workspace, only while the workspace and its company are active and
    /// the caller is still an active member of that company; null otherwise.
    /// </summary>
    private static async Task<WorkspaceRole?> FindLiveGrantAsync(
        IHeliosDbContext db, Guid workspaceId, Guid userId, CancellationToken ct)
    {
        var grants = await (
            from member in db.WorkspaceMembers.IgnoreQueryFilters()
            where member.WorkspaceId == workspaceId && member.UserId == userId
            join workspace in db.Workspaces.IgnoreQueryFilters() on member.WorkspaceId equals workspace.Id
            where workspace.IsActive
            join organization in db.Organizations on workspace.OrganizationId equals organization.Id
            where organization.IsActive
            where db.OrganizationMembers.Any(o =>
                o.OrganizationId == organization.Id && o.UserId == userId && o.IsActive)
            select member.Role)
            .ToListAsync(ct);

        return grants.Count == 1 ? grants[0] : null;
    }

    private static void RecordSignIn(IAuditWriter audit, Guid userId, bool allowed, string? reason) =>
        audit.Record(
            "auth.login",
            "User",
            userId.ToString(),
            allowed: allowed,
            denyReason: reason);
}
