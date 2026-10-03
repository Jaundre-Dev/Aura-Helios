using System.Text.Json;
using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Common;
using Helios.Application.Features.Accounts;
using Helios.Contracts.Platform;
using Helios.Domain.Identity;
using Helios.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.Platform;

/// <summary>
/// Platform staff and their second factor. Staff are granted by an Administrator (or, for the very
/// first one, by an operator with host access through the <c>platform-staff grant</c> command);
/// nobody can change their own record. A staff member enrols an authenticator once, and every
/// platform session after that starts with a fresh code: replays of an accepted code, and guesses
/// beyond <see cref="PlatformStaffMember.MaxFailedCodes"/>, are refused.
/// </summary>
public sealed class PlatformStaffService(
    IHeliosDbContext db,
    PlatformAccess access,
    IUserDirectory users,
    IPayloadProtector protector,
    AccountSecurityService recoveryCodes,
    IAuditWriter audit,
    TimeProvider clock)
{
    public const string TotpIssuer = "AURA HELIOS";

    public async Task<IReadOnlyList<PlatformStaffResponse>> ListAsync(CancellationToken ct)
    {
        await access.RequireAsync(PlatformPermission.ManageStaff, ct);

        var staff = await db.PlatformStaff.AsNoTracking().OrderBy(s => s.CreatedAt).ToListAsync(ct);
        var people = await users.GetAsync(staff.Select(s => s.UserId).ToList(), ct);

        return staff.Select(s => ToResponse(s, people.GetValueOrDefault(s.UserId))).ToList();
    }

    public async Task<PlatformStaffResponse> GrantAsync(GrantPlatformRoleRequest request, CancellationToken ct)
    {
        var actor = await access.RequireAsync(PlatformPermission.ManageStaff, ct);

        var person = await users.FindActiveByEmailAsync(request.Email, ct)
            ?? throw new NotFoundException("Account", request.Email);

        RefuseSelf(actor.UserId, person.Id);

        var staff = await UpsertAsync(person.Id, request.Role, actor.UserId, "api", ct);
        return ToResponse(staff, person);
    }

    /// <summary>
    /// Host-console bootstrap of a staff role, for the first Administrator. Not reachable over
    /// HTTP: whoever can run commands on the API host already controls its secrets and database.
    /// </summary>
    public async Task<PlatformStaffResponse> GrantFromConsoleAsync(string email, PlatformRole role, CancellationToken ct)
    {
        var person = await users.FindActiveByEmailAsync(email, ct)
            ?? throw new NotFoundException("Account", email);

        var staff = await UpsertAsync(person.Id, role, grantedBy: null, "console", ct);
        return ToResponse(staff, person);
    }

    public async Task DeactivateAsync(Guid userId, CancellationToken ct)
    {
        var actor = await access.RequireAsync(PlatformPermission.ManageStaff, ct);
        RefuseSelf(actor.UserId, userId);

        var staff = await db.PlatformStaff.SingleOrDefaultAsync(s => s.UserId == userId, ct)
            ?? throw new NotFoundException("Platform staff", userId);

        // The session validator re-reads this row on every request, so the deactivated person's
        // platform session stops working on their next call.
        staff.IsActive = false;
        audit.Record("platform.staff.deactivate", nameof(PlatformStaffMember), userId.ToString());
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Clears a lost authenticator so the person can enrol again. Never for oneself.</summary>
    public async Task ResetMfaAsync(Guid userId, CancellationToken ct)
    {
        var actor = await access.RequireAsync(PlatformPermission.ManageStaff, ct);
        RefuseSelf(actor.UserId, userId);

        var staff = await db.PlatformStaff.SingleOrDefaultAsync(s => s.UserId == userId, ct)
            ?? throw new NotFoundException("Platform staff", userId);

        staff.TotpSecretEnvelope = null;
        staff.TotpKeyId = null;
        staff.TotpConfirmedAt = null;
        staff.LastTotpStep = null;
        staff.FailedCodeCount = 0;
        staff.CodeLockedUntil = null;

        // Recovery codes belong to the old authenticator; they stop working with it.
        await db.RecoveryCodes
            .Where(c => c.UserId == userId && c.Scope == RecoveryCodeScope.Platform && c.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedAt, clock.GetUtcNow()), ct);

        audit.Record("platform.staff.mfa_reset", nameof(PlatformStaffMember), userId.ToString());
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Starts authenticator enrolment for the signed-in staff member. Refused once an authenticator
    /// is confirmed: replacing it needs another Administrator's reset, so a stolen password alone
    /// cannot enrol a new device.
    /// </summary>
    public async Task<MfaEnrolmentResponse> EnrolAsync(CancellationToken ct)
    {
        var userId = access.RequireUser();
        var staff = await db.PlatformStaff.SingleOrDefaultAsync(s => s.UserId == userId && s.IsActive, ct)
            ?? throw new ForbiddenException("Only platform staff can enrol for platform access.", "platform_access_denied");

        if (staff.MfaConfirmed)
        {
            throw new ConflictException("An authenticator is already confirmed. Ask another Administrator to reset it.", "mfa_already_enrolled");
        }

        var secret = Totp.NewSecret();
        var sealedSecret = protector.Protect(Convert.ToBase64String(secret));

        staff.TotpSecretEnvelope = sealedSecret.Envelope;
        staff.TotpKeyId = sealedSecret.KeyId;
        staff.LastTotpStep = null;
        staff.FailedCodeCount = 0;
        staff.CodeLockedUntil = null;

        audit.Record("platform.mfa.enrol", nameof(PlatformStaffMember), userId.ToString());
        await db.SaveChangesAsync(ct);

        var account = (await users.GetAsync([userId], ct)).GetValueOrDefault(userId)?.Email ?? userId.ToString();
        return new MfaEnrolmentResponse(Totp.Base32(secret), Totp.OtpAuthUri(TotpIssuer, account, secret));
    }

    /// <summary>
    /// Checks a code for the signed-in staff member and returns their role for the step-up session.
    /// The first valid code after enrolment confirms the authenticator.
    /// </summary>
    public async Task<PlatformRole> VerifyCodeAsync(string code, bool confirming, CancellationToken ct)
    {
        var userId = access.RequireUser();
        var now = clock.GetUtcNow();

        var staff = await db.PlatformStaff.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == userId && s.IsActive, ct);

        if (staff?.TotpSecretEnvelope is null || staff.TotpKeyId is null)
        {
            await RecordAsync(userId, allowed: false, "not_enrolled", ct);
            throw new ForbiddenException("No authenticator is enrolled for platform access.", "mfa_not_enrolled");
        }

        if (confirming != !staff.MfaConfirmed)
        {
            throw new ConflictException(
                confirming ? "The authenticator is already confirmed." : "Confirm the authenticator first.",
                confirming ? "mfa_already_enrolled" : "mfa_not_confirmed");
        }

        if (staff.CodeLockedUntil > now)
        {
            await RecordAsync(userId, allowed: false, "locked", ct);
            throw new ForbiddenException("Too many invalid codes. Try again later.", "mfa_locked");
        }

        // A one-time recovery code stands in for a lost authenticator, but never confirms one.
        var recovery = !confirming && AccountSecurityService.LooksLikeRecoveryCode(code);

        var secret = Convert.FromBase64String(protector.Unprotect(staff.TotpKeyId, staff.TotpSecretEnvelope));
        var step = recovery ? null : Totp.Verify(secret, code, now);

        // Accepting is one conditional update: only a step later than any accepted before. Two
        // concurrent uses of one code, or a replay of an observed one, cannot both succeed.
        var accepted = recovery
            ? await recoveryCodes.RedeemRecoveryCodeAsync(userId, RecoveryCodeScope.Platform, code, ct)
            : step is { } s && await db.PlatformStaff
            .Where(x => x.Id == staff.Id && (x.LastTotpStep == null || x.LastTotpStep < s))
            .ExecuteUpdateAsync(set => set
                .SetProperty(x => x.LastTotpStep, s)
                .SetProperty(x => x.FailedCodeCount, 0)
                .SetProperty(x => x.CodeLockedUntil, (DateTimeOffset?)null)
                .SetProperty(x => x.TotpConfirmedAt, x => x.TotpConfirmedAt ?? now), ct) == 1;

        if (!accepted)
        {
            await CountFailureAsync(staff.Id, now + PlatformStaffMember.CodeLockout, ct);

            await RecordAsync(userId, allowed: false, recovery ? "invalid_recovery_code" : step is null ? "invalid_code" : "replayed_code", ct);
            throw new ForbiddenException("The code is not valid.", "mfa_invalid");
        }

        await RecordAsync(userId, allowed: true, confirming ? "confirmed" : recovery ? "recovery_code" : null, ct);
        return staff.Role;
    }

    /// <summary>New platform recovery codes for the signed-in staff member (shown once).</summary>
    public Task<IReadOnlyList<string>> NewRecoveryCodesAsync(CancellationToken ct) =>
        recoveryCodes.NewRecoveryCodesAsync(access.RequireUser(), RecoveryCodeScope.Platform, ct);

    private async Task<PlatformStaffMember> UpsertAsync(Guid userId, PlatformRole role, Guid? grantedBy, string source, CancellationToken ct)
    {
        var staff = await db.PlatformStaff.SingleOrDefaultAsync(s => s.UserId == userId, ct);
        var previous = staff is { IsActive: true } ? staff.Role.ToString() : null;

        if (staff is null)
        {
            staff = new PlatformStaffMember { UserId = userId, CreatedBy = grantedBy, CreatedAt = clock.GetUtcNow() };
            db.PlatformStaff.Add(staff);
        }

        // A role change invalidates sessions issued under the old role: the session validator
        // compares the token's role with this row.
        staff.Role = role;
        staff.IsActive = true;

        audit.Record("platform.staff.grant", nameof(PlatformStaffMember), userId.ToString(),
            metadataJson: JsonSerializer.Serialize(new { role = role.ToString(), previous, source }));

        await db.SaveChangesAsync(ct);
        return staff;
    }

    /// <summary>
    /// Counts a failed code; the fifth locks. Compare-and-set on the observed count: MySQL evaluates
    /// SET clauses left to right, so one UPDATE whose assignments refer to each other locks early.
    /// </summary>
    private async Task CountFailureAsync(Guid staffId, DateTimeOffset lockUntil, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var current = await db.PlatformStaff.AsNoTracking()
                .Where(s => s.Id == staffId).Select(s => s.FailedCodeCount).SingleAsync(ct);
            var locks = current + 1 >= PlatformStaffMember.MaxFailedCodes;
            DateTimeOffset? newLock = locks ? lockUntil : null;

            var updated = await db.PlatformStaff
                .Where(s => s.Id == staffId && s.FailedCodeCount == current)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.FailedCodeCount, locks ? 0 : current + 1)
                    .SetProperty(s => s.CodeLockedUntil, s => newLock ?? s.CodeLockedUntil), ct);

            if (updated == 1)
            {
                return;
            }
        }
    }

    private async Task RecordAsync(Guid userId, bool allowed, string? reason, CancellationToken ct)
    {
        audit.Record("platform.mfa.verify", nameof(PlatformStaffMember), userId.ToString(), allowed: allowed, denyReason: allowed ? null : reason,
            metadataJson: allowed && reason is not null ? JsonSerializer.Serialize(new { reason }) : null);
        await db.SaveChangesAsync(ct);
    }

    private static void RefuseSelf(Guid actor, Guid target)
    {
        if (actor == target)
        {
            throw new ForbiddenException("Platform staff cannot change their own staff record.", "self_change_refused");
        }
    }

    private static PlatformStaffResponse ToResponse(PlatformStaffMember s, UserSummary? person) =>
        new(s.UserId, person?.Email, person?.DisplayName, s.Role, s.IsActive, s.MfaConfirmed, s.CreatedAt, s.CreatedBy);
}
