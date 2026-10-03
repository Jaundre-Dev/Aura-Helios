using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Common;
using Helios.Contracts.Organizations;
using Helios.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.Identity;

/// <summary>
/// The one place organisation permissions are decided. A caller with no active membership of an
/// active organisation gets 404 — indistinguishable from a company that does not exist — and a
/// member whose role lacks the permission gets 403 with a persisted audit denial.
/// </summary>
public sealed class OrganizationAccess(
    IHeliosDbContext db,
    IRowLocks locks,
    IWorkspaceContext context,
    IAuditWriter audit)
{
    public Guid RequireUser() => context.UserId ?? throw new UnauthenticatedException();

    /// <summary>Plain membership check for reads and for writes that need no lock.</summary>
    public async Task<OrganizationMember> RequireAsync(
        Guid organizationId,
        OrganizationPermission permission,
        CancellationToken ct)
    {
        var userId = RequireUser();

        var membership = await db.OrganizationMembers
            .Where(m => m.OrganizationId == organizationId && m.UserId == userId && m.IsActive)
            .Where(m => db.Organizations.Any(o => o.Id == organizationId && o.IsActive))
            .SingleOrDefaultAsync(ct);

        return await CheckAsync(organizationId, permission, membership, ct);
    }

    /// <summary>
    /// Membership check that share-locks the row until the surrounding transaction commits, so a
    /// concurrent removal cannot slip between this decision and the write it authorises.
    /// </summary>
    public async Task<OrganizationMember> RequireLockedAsync(
        Guid organizationId,
        OrganizationPermission permission,
        CancellationToken ct)
    {
        var userId = RequireUser();
        var membership = await locks.LockActiveOrganizationMembershipAsync(organizationId, userId, ct);

        return await CheckAsync(organizationId, permission, membership, ct);
    }

    /// <summary>Membership check passing when the role grants at least one of the permissions.</summary>
    public async Task<OrganizationMember> RequireAnyAsync(
        Guid organizationId,
        IReadOnlyCollection<OrganizationPermission> permissions,
        CancellationToken ct)
    {
        var userId = RequireUser();

        var membership = await db.OrganizationMembers
            .Where(m => m.OrganizationId == organizationId && m.UserId == userId && m.IsActive)
            .Where(m => db.Organizations.Any(o => o.Id == organizationId && o.IsActive))
            .SingleOrDefaultAsync(ct);

        return await CheckAsync(organizationId, permissions, membership, ct);
    }

    private Task<OrganizationMember> CheckAsync(
        Guid organizationId,
        OrganizationPermission permission,
        OrganizationMember? membership,
        CancellationToken ct) =>
        CheckAsync(organizationId, [permission], membership, ct);

    private async Task<OrganizationMember> CheckAsync(
        Guid organizationId,
        IReadOnlyCollection<OrganizationPermission> permissions,
        OrganizationMember? membership,
        CancellationToken ct)
    {
        if (membership is null)
        {
            throw new NotFoundException("Organization", organizationId);
        }

        if (!permissions.Any(p => OrganizationPermissions.Grants(membership.Role, p)))
        {
            var required = string.Join(" or ", permissions);

            audit.Record(
                "organization.access",
                nameof(Organization),
                organizationId.ToString(),
                allowed: false,
                denyReason: $"Requires {required}; role {membership.Role} does not grant it.",
                organizationId: organizationId);

            // Persist the denial: the exception below would otherwise discard the staged row.
            await db.SaveChangesAsync(ct);

            throw new ForbiddenException($"Your role ({membership.Role}) does not allow {required}.", "permission_denied");
        }

        return membership;
    }
}
