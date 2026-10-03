using Helios.Application.Abstractions.Persistence;
using Helios.Application.Common;
using Helios.Application.Features.Identity;
using Helios.Contracts.Billing;
using Helios.Contracts.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.Billing;

/// <summary>
/// The finance views: balance, ledger history and usage — all company-wide and all requiring
/// <c>ViewBilling</c> (Owner, Finance). They show money and units, never request results.
/// </summary>
public sealed class BillingQueryService(IHeliosDbContext db, OrganizationAccess access, LedgerService ledger)
{
    public async Task<BalanceResponse> GetBalanceAsync(Guid organizationId, CancellationToken ct)
    {
        await access.RequireAsync(organizationId, OrganizationPermission.ViewBilling, ct);
        return await ledger.GetBalanceAsync(organizationId, ct);
    }

    /// <summary>The company's ledger transactions, newest first, as changes to available and reserved.</summary>
    public async Task<IReadOnlyList<LedgerTransactionResponse>> GetTransactionsAsync(Guid organizationId, int limit, CancellationToken ct)
    {
        await access.RequireAsync(organizationId, OrganizationPermission.ViewBilling, ct);

        var accounts = await db.LedgerAccounts.AsNoTracking()
            .Where(a => a.OrganizationId == organizationId)
            .ToDictionaryAsync(a => a.Id, a => a.Type, ct);

        var transactions = await db.LedgerTransactions.AsNoTracking()
            .Include(t => t.Entries)
            .Where(t => t.OrganizationId == organizationId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);

        return transactions.Select(t => new LedgerTransactionResponse(
            t.Id,
            t.Type,
            t.Description,
            Change(t, accounts, LedgerAccountType.CustomerAvailable),
            Change(t, accounts, LedgerAccountType.CustomerReserved),
            t.Currency,
            t.ApiRequestId,
            t.PaymentId,
            t.CreatedAt)).ToList();
    }

    /// <summary>Settled usage per product and environment for a period. Sandbox usage is listed at R0.</summary>
    public async Task<UsageResponse> GetUsageAsync(Guid organizationId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        await access.RequireAsync(organizationId, OrganizationPermission.ViewBilling, ct);

        if (to <= from || to - from > TimeSpan.FromDays(366))
        {
            throw new BadRequestException("Choose a period of up to one year, with 'to' after 'from'.", "invalid_period");
        }

        // Company-wide: usage rows are workspace-filtered by default, so the filter is lifted and
        // the company is matched explicitly after the permission check above.
        var lines = await db.UsageEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.OrganizationId == organizationId && u.OccurredAt >= from && u.OccurredAt < to)
            .GroupBy(u => new { u.ProductSlug, u.Environment, u.Unit })
            .Select(g => new UsageLine(
                g.Key.ProductSlug,
                g.Key.Environment,
                g.Key.Unit,
                g.Sum(u => u.Quantity),
                g.Sum(u => u.Amount),
                g.Count()))
            .ToListAsync(ct);

        var ordered = lines.OrderBy(l => l.Product).ThenBy(l => l.Environment).ToList();
        return new UsageResponse(from, to, LedgerService.Currency, ordered, ordered.Sum(l => l.Amount));
    }

    private static decimal Change(
        Domain.Billing.LedgerTransaction transaction,
        IReadOnlyDictionary<Guid, LedgerAccountType> accounts,
        LedgerAccountType type) =>
        transaction.Entries
            .Where(e => accounts.TryGetValue(e.AccountId, out var accountType) && accountType == type)
            .Sum(e => e.Amount);
}
