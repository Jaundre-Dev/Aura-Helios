using System.Security.Claims;
using Helios.Api.Middleware;
using Helios.Api.Security;
using Helios.Application.Abstractions.Messaging;
using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Common;
using Helios.Application.Features.Accounts;
using Helios.Contracts.Identity;
using Helios.Domain.Identity;
using Helios.Infrastructure.Persistence.MySql.Identity;
using Microsoft.AspNetCore.Identity;

namespace Helios.Api.Endpoints;

/// <summary>
/// Account security around sign-in: email verification, password reset and the customer second
/// factor (authenticator app plus one-time recovery codes). Anonymous routes are rate limited and
/// answer identically whether or not an account exists, so they cannot be used to find accounts.
/// </summary>
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Authentication");

        group.MapPost("/verify-email", async (
                VerifyEmailRequest request,
                UserManager<HeliosUser> users,
                AccountSecurityService security,
                IHeliosDbContext db,
                IAuditWriter audit,
                CancellationToken ct) =>
            {
                if (!await security.RedeemTokenAsync(request.UserId, AccountTokenPurpose.EmailVerification, request.Token, ct) ||
                    await users.FindByIdAsync(request.UserId.ToString()) is not { IsActive: true } user)
                {
                    throw new BadRequestException("This verification link is invalid or has expired.", "invalid_token");
                }

                user.EmailConfirmed = true;
                await users.UpdateAsync(user);

                audit.Record("auth.email.verify", "User", user.Id.ToString());
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            })
            .WithName("VerifyEmail")
            .AllowAnonymous()
            .RequireRateLimiting(AuthRateLimiting.PolicyName)
            .WithValidation<VerifyEmailRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/resend-verification", async (
                ClaimsPrincipal principal,
                UserManager<HeliosUser> users,
                AccountSecurityService security,
                IEnumerable<IEmailSender> senders,
                IConfiguration configuration,
                CancellationToken ct) =>
            {
                var user = await users.FindByIdAsync((principal.UserId() ?? throw new UnauthenticatedException()).ToString())
                    ?? throw new UnauthenticatedException();

                if (user.EmailConfirmed)
                {
                    throw new ConflictException("This email address is already verified.", "already_verified");
                }

                var sender = senders.LastOrDefault() ?? throw EmailUnavailable();
                await SendVerificationAsync(user, security, sender, configuration, ct);
                return Results.Accepted();
            })
            .WithName("ResendVerification")
            .RequireAuthorization()
            .RequireRateLimiting(AuthRateLimiting.PolicyName)
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/forgot-password", async (
                ForgotPasswordRequest request,
                UserManager<HeliosUser> users,
                AccountSecurityService security,
                IEnumerable<IEmailSender> senders,
                IConfiguration configuration,
                IHeliosDbContext db,
                IAuditWriter audit,
                CancellationToken ct) =>
            {
                var sender = senders.LastOrDefault() ?? throw EmailUnavailable();

                // The same 202 whether or not the account exists.
                if (await users.FindByEmailAsync(request.Email) is { IsActive: true } user)
                {
                    var token = await security.IssueTokenAsync(user.Id, AccountTokenPurpose.PasswordReset, ct);
                    var link = $"{WebUrl(configuration)}/reset-password?user={user.Id}&token={token}";

                    await sender.SendAsync(new EmailMessage(user.Email!, "Reset your AURA HELIOS password",
                        $"Someone asked to reset the password for this account. If it was you, open this link within " +
                        $"{(int)AccountSecurityService.ResetLifetime.TotalMinutes} minutes:\n\n{link}\n\n" +
                        "If it was not you, ignore this email; your password has not changed."), ct);

                    audit.Record("auth.password.reset_requested", "User", user.Id.ToString());
                    await db.SaveChangesAsync(ct);
                }

                return Results.Accepted();
            })
            .WithName("ForgotPassword")
            .AllowAnonymous()
            .RequireRateLimiting(AuthRateLimiting.PolicyName)
            .WithValidation<ForgotPasswordRequest>()
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/reset-password", async (
                ResetPasswordRequest request,
                UserManager<HeliosUser> users,
                AccountSecurityService security,
                IHeliosDbContext db,
                IAuditWriter audit,
                CancellationToken ct) =>
            {
                var invalid = new BadRequestException("This reset link is invalid or has expired.", "invalid_token");
                var user = await users.FindByIdAsync(request.UserId.ToString());
                if (user is not { IsActive: true })
                {
                    throw invalid;
                }

                // Check the new password before spending the token, so a weak choice can be retried.
                var problems = new List<IdentityError>();
                foreach (var validator in users.PasswordValidators)
                {
                    var check = await validator.ValidateAsync(users, user, request.NewPassword);
                    problems.AddRange(check.Errors);
                }

                if (problems.Count > 0)
                {
                    return Results.ValidationProblem(
                        problems.GroupBy(e => e.Code).ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()),
                        title: "Password reset failed.");
                }

                if (!await security.RedeemTokenAsync(user.Id, AccountTokenPurpose.PasswordReset, request.Token, ct))
                {
                    throw invalid;
                }

                // A new password signs the account out everywhere (new security stamp), lifts a
                // lockout, and proves control of the address.
                user.PasswordHash = users.PasswordHasher.HashPassword(user, request.NewPassword);
                user.EmailConfirmed = true;
                user.AccessFailedCount = 0;
                user.LockoutEnd = null;
                await users.UpdateAsync(user);
                await users.UpdateSecurityStampAsync(user);

                audit.Record("auth.password.reset", "User", user.Id.ToString());
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            })
            .WithName("ResetPassword")
            .AllowAnonymous()
            .RequireRateLimiting(AuthRateLimiting.PolicyName)
            .WithValidation<ResetPasswordRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/mfa/login", async (
                MfaLoginRequest request,
                UserManager<HeliosUser> users,
                AccountSecurityService security,
                IHeliosDbContext db,
                IAuditWriter audit,
                JwtTokenIssuer issuer,
                TimeProvider clock,
                CancellationToken ct) =>
            {
                var failed = Results.Problem(title: "Sign-in failed", detail: "The code is not valid or the sign-in has expired.",
                    statusCode: StatusCodes.Status401Unauthorized);

                if (issuer.ReadMfaChallenge(request.MfaToken) is not { } challenge ||
                    await users.FindByIdAsync(challenge.UserId.ToString()) is not { IsActive: true } user ||
                    !string.Equals(user.SecurityStamp, challenge.SecurityStamp, StringComparison.Ordinal))
                {
                    return failed;
                }

                if (!await security.VerifyAsync(user.Id, request.Code, allowRecovery: true, ct))
                {
                    AuthEndpoints.RecordSignIn(audit, user.Id, allowed: false, "bad_second_factor");
                    await db.SaveChangesAsync(ct);
                    return failed;
                }

                return Results.Ok(await AuthEndpoints.SignInAsync(user, users, db, audit, issuer, clock, ct));
            })
            .WithName("MfaLogin")
            .WithSummary("Completes a sign-in that returned mfaRequired, with an authenticator or recovery code.")
            .AllowAnonymous()
            .RequireRateLimiting(AuthRateLimiting.PolicyName)
            .WithValidation<MfaLoginRequest>()
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        var mfa = group.MapGroup("/mfa").RequireAuthorization().RequireRateLimiting(AuthRateLimiting.PolicyName);

        group.MapGet("/security", async (ClaimsPrincipal principal, UserManager<HeliosUser> users, AccountSecurityService security, CancellationToken ct) =>
            {
                var user = await users.FindByIdAsync((principal.UserId() ?? throw new UnauthenticatedException()).ToString())
                    ?? throw new UnauthenticatedException();

                return Results.Ok(new AccountSecurityResponse(
                    user.EmailConfirmed,
                    await security.MfaEnabledAsync(user.Id, ct),
                    await security.RecoveryCodesRemainingAsync(user.Id, RecoveryCodeScope.Account, ct)));
            })
            .WithName("GetAccountSecurity")
            .RequireAuthorization()
            .Produces<AccountSecurityResponse>();

        mfa.MapPost("/enrol", async (ClaimsPrincipal principal, AccountSecurityService security, CancellationToken ct) =>
                Results.Ok(await security.EnrolAsync(Caller(principal), principal.Email() ?? "account", ct)))
            .WithName("EnrolMfa")
            .WithSummary("Starts authenticator set-up. The secret is shown once; confirm with a code.")
            .Produces<MfaSetupResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        mfa.MapPost("/confirm", async (AccountCodeRequest request, ClaimsPrincipal principal, AccountSecurityService security, CancellationToken ct) =>
                Results.Ok(new RecoveryCodesResponse(await security.ConfirmAsync(Caller(principal), request.Code, ct))))
            .WithName("ConfirmMfa")
            .WithSummary("Turns on two-step sign-in and returns one-time recovery codes (shown once).")
            .WithValidation<AccountCodeRequest>()
            .Produces<RecoveryCodesResponse>();

        mfa.MapPost("/disable", async (AccountCodeRequest request, ClaimsPrincipal principal, AccountSecurityService security, CancellationToken ct) =>
            {
                await security.DisableAsync(Caller(principal), request.Code, ct);
                return Results.NoContent();
            })
            .WithName("DisableMfa")
            .WithValidation<AccountCodeRequest>()
            .Produces(StatusCodes.Status204NoContent);

        mfa.MapPost("/recovery-codes", async (AccountCodeRequest request, ClaimsPrincipal principal, AccountSecurityService security, CancellationToken ct) =>
            {
                var userId = Caller(principal);
                if (AccountSecurityService.LooksLikeRecoveryCode(request.Code) ||
                    !await security.VerifyAsync(userId, request.Code, allowRecovery: false, ct))
                {
                    throw new ForbiddenException("Enter a current authenticator code.", "mfa_invalid");
                }

                return Results.Ok(new RecoveryCodesResponse(await security.NewRecoveryCodesAsync(userId, RecoveryCodeScope.Account, ct)));
            })
            .WithName("RegenerateRecoveryCodes")
            .WithSummary("Replaces the recovery codes. Needs a current authenticator code.")
            .WithValidation<AccountCodeRequest>()
            .Produces<RecoveryCodesResponse>();

        return app;
    }

    /// <summary>Sends a fresh verification link; earlier links stop working.</summary>
    internal static async Task SendVerificationAsync(
        HeliosUser user, AccountSecurityService security, IEmailSender sender, IConfiguration configuration, CancellationToken ct)
    {
        var token = await security.IssueTokenAsync(user.Id, AccountTokenPurpose.EmailVerification, ct);
        var link = $"{WebUrl(configuration)}/verify-email?user={user.Id}&token={token}";

        await sender.SendAsync(new EmailMessage(user.Email!, "Verify your email for AURA HELIOS",
            $"Confirm this address to finish setting up your account:\n\n{link}\n\n" +
            $"The link works once and expires in {(int)AccountSecurityService.VerificationLifetime.TotalHours} hours."), ct);
    }

    private static string WebUrl(IConfiguration configuration) =>
        (configuration["Helios:WebUrl"] ?? "http://localhost:5173").TrimEnd('/');

    private static Guid Caller(ClaimsPrincipal principal) => principal.UserId() ?? throw new UnauthenticatedException();

    private static ServiceUnavailableException EmailUnavailable() =>
        new("Email is not configured on this server, so links cannot be sent.", "email_unavailable");
}
