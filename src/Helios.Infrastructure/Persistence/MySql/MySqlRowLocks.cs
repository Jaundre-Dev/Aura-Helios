using Helios.Application.Abstractions.Persistence;
using Helios.Contracts.Billing;
using Helios.Domain.Billing;
using Helios.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Helios.Infrastructure.Persistence.MySql;

/// <summary>
/// MySQL locking reads. Queries are executed without further LINQ composition so EF sends the SQL
/// as written: composing would wrap it in a derived table, where the locking clause is not
/// guaranteed to apply to the base rows.
/// </summary>
public sealed class MySqlRowLocks(HeliosDbContext db, TimeProvider clock) : IRowLocks
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

    public async Task<LedgerAccount> LockLedgerAccountAsync(
        Guid ownerId,
        LedgerAccountType type,
        string currency,
        CancellationToken cancellationToken)
    {
        var typeName = await EnsureAccountRowAsync(ownerId, type, currency, cancellationToken);

        var rows = await db.LedgerAccounts
            .FromSqlInterpolated($"""
                SELECT * FROM ledger_accounts
                WHERE organization_id = {ownerId} AND type = {typeName} AND currency = {currency}
                FOR UPDATE
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return TrackFresh(rows.Single());
    }

    /// <summary>
    /// Makes the change tracker hold exactly the row the locking read returned. EF's identity
    /// resolution would otherwise hand back an instance already tracked from earlier in this
    /// context — with values from before the lock — and a balance computed from those would
    /// overwrite a concurrent committed change. A plain reload is no fix either: under REPEATABLE
    /// READ it returns the transaction's snapshot, not the latest committed row.
    /// </summary>
    private T TrackFresh<T>(T fresh) where T : Helios.Domain.Common.Entity
    {
        var tracked = db.ChangeTracker.Entries<T>().FirstOrDefault(e => e.Entity.Id == fresh.Id);

        if (tracked is null)
        {
            db.Attach(fresh);
            return fresh;
        }

        tracked.CurrentValues.SetValues(fresh);
        tracked.OriginalValues.SetValues(fresh);
        tracked.State = EntityState.Unchanged;
        return tracked.Entity;
    }

    public async Task<LedgerAccount> EnsurePlatformAccountAsync(
        LedgerAccountType type,
        string currency,
        CancellationToken cancellationToken)
    {
        // Plain read first: INSERT … ON DUPLICATE KEY locks an existing row exclusively, which would
        // serialise every tenant's settlement on this one account. Insert only on first use ever.
        var existing = await db.LedgerAccounts.SingleOrDefaultAsync(a =>
            a.OrganizationId == LedgerAccount.PlatformOwner && a.Type == type && a.Currency == currency, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        await EnsureAccountRowAsync(LedgerAccount.PlatformOwner, type, currency, cancellationToken);

        return await db.LedgerAccounts.SingleAsync(a =>
            a.OrganizationId == LedgerAccount.PlatformOwner && a.Type == type && a.Currency == currency, cancellationToken);
    }

    /// <summary>
    /// Creates the account on first use. A concurrent creator blocks on the unique key and then
    /// takes the no-op branch, so exactly one row ever exists per owner, type and currency.
    /// </summary>
    private async Task<string> EnsureAccountRowAsync(
        Guid ownerId,
        LedgerAccountType type,
        string currency,
        CancellationToken cancellationToken)
    {
        var typeName = type.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ledger_accounts (id, organization_id, type, currency, balance, created_at)
            VALUES ({Guid.CreateVersion7()}, {ownerId}, {typeName}, {currency}, 0, {clock.GetUtcNow()})
            ON DUPLICATE KEY UPDATE id = id
            """, cancellationToken);

        return typeName;
    }

    public async Task<Reservation?> LockReservationAsync(Guid apiRequestId, CancellationToken cancellationToken)
    {
        var rows = await db.Reservations
            .FromSqlInterpolated($"SELECT * FROM reservations WHERE api_request_id = {apiRequestId} FOR UPDATE")
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return rows.SingleOrDefault() is { } fresh ? TrackFresh(fresh) : null;
    }
}
