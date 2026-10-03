using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Common;
using Helios.Contracts.Platform;
using Helios.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.Platform;

/// <summary>
/// The one place platform permissions are decided. The endpoint policy has already required a
/// step-up (MFA) platform session and the session validator has re-checked the staff record; this
/// reads it again and checks the named permission, persisting every denial to the audit trail.
/// </summary>
public sealed class PlatformAccess(IHeliosDbContext db, IWorkspaceContext context, IAuditWriter audit)
{
    public Guid RequireUser() => context.UserId ?? throw new UnauthenticatedException();

    public async Task<PlatformStaffMember> RequireAsync(PlatformPermission permission, CancellationToken ct)
    {
        var userId = RequireUser();

        var staff = await db.PlatformStaff.AsNoTracking()
            .SingleOrDefaultAsync(s => s.UserId == userId && s.IsActive, ct);

        if (staff is null || !staff.MfaConfirmed)
        {
            await DenyAsync(permission, "Not active platform staff with confirmed MFA.", ct);
            throw new ForbiddenException("Platform access requires an active staff role.", "platform_access_denied");
        }

        if (!PlatformPermissions.Grants(staff.Role, permission))
        {
            await DenyAsync(permission, $"Requires {permission}; platform role {staff.Role} does not grant it.", ct);
            throw new ForbiddenException($"Your platform role ({staff.Role}) does not allow {permission}.", "permission_denied");
        }

        return staff;
    }

    private async Task DenyAsync(PlatformPermission permission, string reason, CancellationToken ct)
    {
        audit.Record("platform.access", "Platform", permission.ToString(), allowed: false, denyReason: reason);

        // Persist the denial: the exception that follows would otherwise discard the staged row.
        await db.SaveChangesAsync(ct);
    }
}
