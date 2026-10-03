using Helios.Application.Abstractions.Persistence;
using Helios.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Helios.Infrastructure.Persistence.MySql;

/// <summary>
/// MySQL locking reads. Queries are executed without further LINQ composition so EF sends the SQL
/// as written: composing would wrap it in a derived table, where the locking clause is not
/// guaranteed to apply to the base rows.
/// </summary>
public sealed class MySqlRowLocks(HeliosDbContext db) : IRowLocks
{
    public async Task<OrganizationMember?> LockActiveOrganizationMembershipAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var rows = await db.OrganizationMembers
            .FromSqlInterpolated($"""
                SELECT m.* FROM organization_members AS m
                INNER JOIN organizations AS o ON o.id = m.organization_id
                WHERE m.organization_id = {organizationId}
                  AND m.user_id = {userId}
                  AND m.is_active = 1
                  AND o.is_active = 1
                FOR SHARE
                """)
            .ToListAsync(cancellationToken);

        return rows.SingleOrDefault();
    }
}
