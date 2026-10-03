using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Common;
using Helios.Application.Features.Platform;
using Helios.Contracts.Identity;
using Helios.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.Accounts;

/// <summary>
/// Single-use email tokens, customer authenticators and recovery codes. Secrets are stored only as
/// hashes (tokens, recovery codes) or sealed under the keyring (TOTP secrets). Every acceptance is a
/// conditional update, so a token, a code or a recovery code works at most once even when two
/// requests race.
/// </summary>
public sealed partial class AccountSecurityService(
    IHeliosDbContext db,
    IPayloadProtector protector,
    IAuditWriter audit,
    TimeProvider clock)
{
    public const int RecoveryCodeCount = 10;
    public static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(48);
    public static readonly TimeSpan ResetLifetime = TimeSpan.FromMinutes(30);

    // Email tokens

    /// <summary>A fresh token for the purpose; any earlier unused token for it stops working.</summary>
    public async Task<string> IssueTokenAsync(Guid userId, AccountTokenPurpose purpose, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        await db.AccountTokens
            .Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), ct);

        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        db.AccountTokens.Add(new AccountToken
        {
            UserId = userId,
            Purpose = purpose,
            TokenHash = Hash(token),
            CreatedAt = now,
            ExpiresAt = now + (purpose == AccountTokenPurpose.PasswordReset ? ResetLifetime : VerificationLifetime)
        });

        await db.SaveChangesAsync(ct);
        return token;
    }

    /// <summary>Consumes a token. False when it is unknown, used, expired or for another account.</summary>
    public async Task<bool> RedeemTokenAsync(Guid userId, AccountTokenPurpose purpose, string token, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var hash = Hash(token);

        return await db.AccountTokens
            .Where(t => t.UserId == userId && t.Purpose == purpose && t.TokenHash == hash && t.UsedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), ct) == 1;
    }

    // Recovery codes

    /// <summary>Replaces the account's recovery codes for the scope and returns the new ones, once.</summary>
    public async Task<IReadOnlyList<string>> NewRecoveryCodesAsync(Guid userId, RecoveryCodeScope scope, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        await db.RecoveryCodes
            .Where(c => c.UserId == userId && c.Scope == scope && c.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedAt, now), ct);

        var codes = Enumerable.Range(0, RecoveryCodeCount).Select(_ => NewRecoveryCode()).ToList();
        foreach (var code in codes)
        {
            db.RecoveryCodes.Add(new RecoveryCode { UserId = userId, Scope = scope, CodeHash = Hash(Normalise(code)), CreatedAt = now });
        }

        audit.Record("account.recovery_codes.issue", "User", userId.ToString(),
            metadataJson: $$"""{"scope":"{{scope}}"}""");
        await db.SaveChangesAsync(ct);
        return codes;
    }

    public static bool LooksLikeRecoveryCode(string code) => RecoveryCodePattern().IsMatch(code.Trim());

    public async Task<bool> RedeemRecoveryCodeAsync(Guid userId, RecoveryCodeScope scope, string code, CancellationToken ct)
    {
        var hash = Hash(Normalise(code));
        var now = clock.GetUtcNow();

        return await db.RecoveryCodes
            .Where(c => c.UserId == userId && c.Scope == scope && c.CodeHash == hash && c.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedAt, now), ct) == 1;
    }

    public Task<int> RecoveryCodesRemainingAsync(Guid userId, RecoveryCodeScope scope, CancellationToken ct) =>
        db.RecoveryCodes.CountAsync(c => c.UserId == userId && c.Scope == scope && c.UsedAt == null, ct);

    // Customer authenticator

    public Task<bool> MfaEnabledAsync(Guid userId, CancellationToken ct) =>
        db.UserAuthenticators.AnyAsync(a => a.UserId == userId && a.ConfirmedAt != null, ct);

    /// <summary>Starts (or restarts, while unconfirmed) authenticator enrolment.</summary>
    public async Task<MfaSetupResponse> EnrolAsync(Guid userId, string accountLabel, CancellationToken ct)
    {
        var existing = await db.UserAuthenticators.SingleOrDefaultAsync(a => a.UserId == userId, ct);
        if (existing?.ConfirmedAt is not null)
        {
            throw new ConflictException("Two-step sign-in is already on. Turn it off first to replace the authenticator.", "mfa_already_enrolled");
        }

        if (existing is not null)
        {
            db.UserAuthenticators.Remove(existing);
        }

        var secret = Totp.NewSecret();
        var sealedSecret = protector.Protect(Convert.ToBase64String(secret));
        db.UserAuthenticators.Add(new UserAuthenticator
        {
            UserId = userId,
            SecretEnvelope = sealedSecret.Envelope,
            KeyId = sealedSecret.KeyId,
            CreatedAt = clock.GetUtcNow()
        });

        audit.Record("account.mfa.enrol", "User", userId.ToString());
        await db.SaveChangesAsync(ct);

        return new MfaSetupResponse(Totp.Base32(secret), Totp.OtpAuthUri(PlatformStaffService.TotpIssuer, accountLabel, secret));
    }

    /// <summary>Confirms enrolment with a first code and returns recovery codes.</summary>
    public async Task<IReadOnlyList<string>> ConfirmAsync(Guid userId, string code, CancellationToken ct)
    {
        var authenticator = await db.UserAuthenticators.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == userId, ct)
            ?? throw new ConflictException("Start enrolment first.", "mfa_not_enrolled");

        if (authenticator.ConfirmedAt is not null)
        {
            throw new ConflictException("Two-step sign-in is already on.", "mfa_already_enrolled");
        }

        if (authenticator.CodeLockedUntil > clock.GetUtcNow())
        {
            throw new ForbiddenException("Too many invalid codes. Try again later.", "mfa_locked");
        }

        if (!await AcceptTotpAsync(authenticator, code, ct))
        {
            await CountFailureAsync(authenticator.Id, ct);
            throw new ForbiddenException("The code is not valid.", "mfa_invalid");
        }

        audit.Record("account.mfa.confirm", "User", userId.ToString());
        await db.SaveChangesAsync(ct);
        return await NewRecoveryCodesAsync(userId, RecoveryCodeScope.Account, ct);
    }

    /// <summary>
    /// Checks a second factor for a confirmed authenticator: a fresh TOTP code, or an unused recovery
    /// code when <paramref name="allowRecovery"/> is set. Failures count towards a temporary lockout.
    /// </summary>
    public async Task<bool> VerifyAsync(Guid userId, string code, bool allowRecovery, CancellationToken ct)
    {
        var authenticator = await db.UserAuthenticators.AsNoTracking()
            .SingleOrDefaultAsync(a => a.UserId == userId && a.ConfirmedAt != null, ct);

        if (authenticator is null)
        {
            return false;
        }

        if (authenticator.CodeLockedUntil > clock.GetUtcNow())
        {
            Record(userId, false, "locked");
            await db.SaveChangesAsync(ct);
            throw new ForbiddenException("Too many invalid codes. Try again later.", "mfa_locked");
        }

        var accepted = allowRecovery && LooksLikeRecoveryCode(code)
            ? await RedeemRecoveryCodeAsync(userId, RecoveryCodeScope.Account, code, ct)
            : await AcceptTotpAsync(authenticator, code, ct);

        if (accepted && LooksLikeRecoveryCode(code))
        {
            await db.UserAuthenticators.Where(a => a.Id == authenticator.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.FailedCodeCount, 0), ct);
        }

        if (!accepted)
        {
            await CountFailureAsync(authenticator.Id, ct);
        }

        Record(userId, accepted, accepted ? null : "invalid_code");
        await db.SaveChangesAsync(ct);
        return accepted;
    }

    /// <summary>Turns two-step sign-in off, which needs a valid code (or recovery code).</summary>
    public async Task DisableAsync(Guid userId, string code, CancellationToken ct)
    {
        if (!await VerifyAsync(userId, code, allowRecovery: true, ct))
        {
            throw new ForbiddenException("The code is not valid.", "mfa_invalid");
        }

        var now = clock.GetUtcNow();
        await db.UserAuthenticators.Where(a => a.UserId == userId).ExecuteDeleteAsync(ct);
        await db.RecoveryCodes.Where(c => c.UserId == userId && c.Scope == RecoveryCodeScope.Account && c.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedAt, now), ct);

        audit.Record("account.mfa.disable", "User", userId.ToString());
        await db.SaveChangesAsync(ct);
    }

    private async Task<bool> AcceptTotpAsync(UserAuthenticator authenticator, string code, CancellationToken ct)
    {
        var secret = Convert.FromBase64String(protector.Unprotect(authenticator.KeyId, authenticator.SecretEnvelope));
        if (Totp.Verify(secret, code, clock.GetUtcNow()) is not { } step)
        {
            return false;
        }

        var now = clock.GetUtcNow();

        // Only a step later than any accepted before: an observed code cannot be replayed.
        return await db.UserAuthenticators
            .Where(a => a.Id == authenticator.Id && (a.LastStep == null || a.LastStep < step))
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.LastStep, step)
                .SetProperty(a => a.FailedCodeCount, 0)
                .SetProperty(a => a.CodeLockedUntil, (DateTimeOffset?)null)
                .SetProperty(a => a.ConfirmedAt, a => a.ConfirmedAt ?? now), ct) == 1;
    }

    /// <summary>
    /// Counts a failed code; the <see cref="UserAuthenticator.MaxFailedCodes"/>th locks the factor.
    /// A compare-and-set on the observed count rather than one UPDATE whose assignments refer to
    /// each other: MySQL evaluates SET clauses left to right, so a self-referencing pair would lock
    /// one attempt early (or late) depending on clause order.
    /// </summary>
    private async Task CountFailureAsync(Guid authenticatorId, CancellationToken ct)
    {
        var lockUntil = clock.GetUtcNow() + UserAuthenticator.CodeLockout;

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var current = await db.UserAuthenticators.AsNoTracking()
                .Where(a => a.Id == authenticatorId).Select(a => a.FailedCodeCount).SingleAsync(ct);
            var locks = current + 1 >= UserAuthenticator.MaxFailedCodes;
            DateTimeOffset? newLock = locks ? lockUntil : null;

            var updated = await db.UserAuthenticators
                .Where(a => a.Id == authenticatorId && a.FailedCodeCount == current)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.FailedCodeCount, locks ? 0 : current + 1)
                    .SetProperty(a => a.CodeLockedUntil, a => newLock ?? a.CodeLockedUntil), ct);

            if (updated == 1)
            {
                return;
            }
        }
    }

    private void Record(Guid userId, bool allowed, string? reason) =>
        audit.Record("account.mfa.verify", "User", userId.ToString(), allowed: allowed, denyReason: reason);

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Ten base32 characters in two groups, e.g. <c>K7Q2M-4TZPA</c>: about 50 bits, single use.</summary>
    private static string NewRecoveryCode()
    {
        var text = Totp.Base32(RandomNumberGenerator.GetBytes(7))[..10];
        return $"{text[..5]}-{text[5..]}";
    }

    private static string Normalise(string code) => code.Trim().Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    [GeneratedRegex("^[A-Za-z2-7]{5}-?[A-Za-z2-7]{5}$")]
    private static partial Regex RecoveryCodePattern();
}
