using System.Security.Claims;
using Helios.Api.Middleware;
using Helios.Api.Security;
using Helios.Application.Abstractions.Persistence;
using Helios.Application.Common;
using Helios.Contracts.Identity;
using Helios.Infrastructure.Persistence.MySql.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Helios.Api.Endpoints;

/// <summary>
/// Local sign-in (WP0.4). Register and login are anonymous; everything else needs a valid
/// token. A user signs in first, then selects a workspace, which re-issues a token scoped
/// to that workspace — the REST endpoints read the <c>workspace_id</c> claim, so a token
/// without one can create workspaces but cannot act inside one.
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

                var auth = issuer.Issue(user.Id, user.Email!, user.DisplayName, workspaceId: null, role: null);

                return Results.Created($"/api/v1/auth/me", auth);
            })
            .WithName("Register")
            .WithValidation<RegisterRequest>()
            .Produces<AuthResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapPost("/login", async (
                LoginRequest request,
                UserManager<HeliosUser> users,
                IHeliosDbContext db,
                JwtTokenIssuer issuer,
                TimeProvider clock,
                CancellationToken ct) =>
            {
                var user = await users.FindByEmailAsync(request.Email);

                // One message for "no such user" and "wrong password" alike, so the endpoint
                // does not become an account-enumeration oracle.
                if (user is null || !user.IsActive ||
                    !await users.CheckPasswordAsync(user, request.Password))
                {
                    if (user is not null)
                    {
                        await users.AccessFailedAsync(user);
                    }

                    return Results.Problem(
                        title: "Sign-in failed",
                        detail: "Invalid email or password.",
                        statusCode: StatusCodes.Status401Unauthorized);
                }

                await users.ResetAccessFailedCountAsync(user);

                // Seed the workspace claim from the last selection, but only if that
                // membership still exists — access can be revoked between sessions.
                Guid? workspaceId = null;
                WorkspaceRole? role = null;

                if (user.DefaultWorkspaceId is { } previous)
                {
                    var membership = await db.WorkspaceMembers
                        .IgnoreQueryFilters()
                        .SingleOrDefaultAsync(m => m.WorkspaceId == previous && m.UserId == user.Id, ct);

                    if (membership is not null)
                    {
                        workspaceId = membership.WorkspaceId;
                        role = membership.Role;
                    }
                }

                user.LastSignInAt = clock.GetUtcNow();
                await users.UpdateAsync(user);

                var auth = issuer.Issue(user.Id, user.Email!, user.DisplayName, workspaceId, role);

                return Results.Ok(auth);
            })
            .WithName("Login")
            .WithValidation<LoginRequest>()
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/select-workspace", async (
                SelectWorkspaceRequest request,
                ClaimsPrincipal principal,
                UserManager<HeliosUser> users,
                IHeliosDbContext db,
                JwtTokenIssuer issuer,
                CancellationToken ct) =>
            {
                var userId = principal.UserId() ?? throw new UnauthenticatedException();

                var membership = await db.WorkspaceMembers
                    .IgnoreQueryFilters()
                    .SingleOrDefaultAsync(m => m.WorkspaceId == request.WorkspaceId && m.UserId == userId, ct);

                // 404, not 403: telling a stranger the workspace is real leaks other tenants,
                // exactly as the workspace GET does.
                if (membership is null)
                {
                    throw new NotFoundException("Workspace", request.WorkspaceId);
                }

                var user = await users.FindByIdAsync(userId.ToString())
                    ?? throw new UnauthenticatedException();

                user.DefaultWorkspaceId = request.WorkspaceId;
                await users.UpdateAsync(user);

                var auth = issuer.Issue(
                    user.Id, user.Email!, user.DisplayName, membership.WorkspaceId, membership.Role);

                return Results.Ok(auth);
            })
            .WithName("SelectWorkspace")
            .WithValidation<SelectWorkspaceRequest>()
            .RequireAuthorization()
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/me", (ClaimsPrincipal principal) =>
            {
                if (principal.UserId() is not { } userId)
                {
                    throw new UnauthenticatedException();
                }

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
}
