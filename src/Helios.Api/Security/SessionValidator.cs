using System.Security.Claims;
using Helios.Infrastructure.Persistence.MySql;
using Microsoft.EntityFrameworkCore;

namespace Helios.Api.Security;

/// <summary>
/// Re-checks a validated token against current database state on every request. A signature and
/// an expiry prove only that the token was issued; they say nothing about whether the account was
/// disabled, the password changed, sessions were revoked, or the workspace grant removed since.
/// Token claims are treated as a claim to check, never as the authority.
/// </summary>
/// <remarks>
/// Fails closed: a token without a security stamp (issued before this check existed, or forged
/// with a leaked key but no database access) is rejected.
/// </remarks>
public sealed class SessionValidator(HeliosDbContext db)
{
    public async Task<string?> FindProblemAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        if (principal.UserId() is not { } userId)
        {
            return "Token has no subject.";
        }

        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.IsActive, u.SecurityStamp })
            .SingleOrDefaultAsync(ct);

        if (user is null || !user.IsActive)
        {
            return "Account is not active.";
        }

        var stamp = principal.FindFirstValue(HeliosClaims.SecurityStamp);
        if (string.IsNullOrEmpty(stamp) || !string.Equals(stamp, user.SecurityStamp, StringComparison.Ordinal))
        {
            return "Session has been revoked.";
        }

        if (principal.FindFirstValue(HeliosClaims.PlatformRole) is { } platformRole)
        {
            var staff = await db.PlatformStaff
                .AsNoTracking()
                .Where(s => s.UserId == userId)
                .Select(s => new { s.IsActive, s.Role, s.TotpConfirmedAt })
                .SingleOrDefaultAsync(ct);

            // Deactivation, a role change or an authenticator reset ends platform sessions at once.
            if (staff is null || !staff.IsActive || staff.TotpConfirmedAt is null ||
                !string.Equals(staff.Role.ToString(), platformRole, StringComparison.Ordinal) ||
                principal.FindFirstValue(HeliosClaims.AuthenticationMethod) != "mfa")
            {
                return "Platform access has been revoked.";
            }
        }

        if (principal.WorkspaceId() is not { } workspaceId)
        {
            return null;
        }

        var grant = await (
            from member in db.WorkspaceMembers.IgnoreQueryFilters()
            where member.WorkspaceId == workspaceId && member.UserId == userId
            join workspace in db.Workspaces.IgnoreQueryFilters() on member.WorkspaceId equals workspace.Id
            where workspace.IsActive
            join organization in db.Organizations on workspace.OrganizationId equals organization.Id
            where organization.IsActive
            where db.OrganizationMembers.Any(o =>
                o.OrganizationId == organization.Id && o.UserId == userId && o.IsActive)
            select new { member.Role })
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);

        if (grant is null)
        {
            return "Workspace access has been revoked.";
        }

        // A changed role must not keep acting under the old one until the token expires.
        if (principal.Role() is { } claimedRole && claimedRole != grant.Role)
        {
            return "Workspace role has changed; select the workspace again.";
        }

        return null;
    }
}
