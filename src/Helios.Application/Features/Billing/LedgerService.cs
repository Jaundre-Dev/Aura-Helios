using Helios.Application.Abstractions.Persistence;
using Helios.Contracts.Billing;
using Helios.Application.Common;
using Helios.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.Billing;

/// <summary>
/// The append-only, double-entry Rand ledger (plan section 9). Every money movement is one balanced
/// transaction with a unique posting key, so repeating a reserve, settle, release or credit is a
/// no-op rather than a second movement. Customer accounts are locked for the duration of the
/// caller's transaction, which serialises spending per company: two concurrent requests can never
/// both reserve the same rand.
/// </summary>
/// <remarks>
/// Every method must run inside <see cref="IUnitOfWork.ExecuteInTransactionAsync{T}"/>; the caller's
/// commit is what makes a posting and the state change it accompanies atomic.
/// </remarks>
public sealed class LedgerService(IHeliosDbContext db, IRowLocks locks, TimeProvider clock)
{
    public const string Currency = "ZAR";

    /// <summary>Moves the request's maximum cost from available to reserved, or throws 402.</summary>
    public async Task<Reservation> ReserveAsync(Guid organizationId, Guid apiRequestId, decimal amount, CancellationToken ct)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "A reservation must be positive.");
        }

        // Fixed order — available, then reserved — everywhere, so postings cannot deadlock each other.
        var available = await locks.LockLedgerAccountAsync(organizationId, LedgerAccountType.CustomerAvailable, Currency, ct);
        var reserved = await locks.LockLedgerAccountAsync(organizationId, LedgerAccountType.CustomerReserved, Currency, ct);

        // Read under lock: no cached or snapshot balance decides whether money exists.
        if (available.Balance < amount)
        {
            throw new InsufficientCreditException(available.Balance, amount);
        }

        await PostAsync(LedgerTransactionType.Reserve, $"reserve:{apiRequestId}", organizationId,
            $"Reserved for request {apiRequestId}",
            [(available, -amount), (reserved, amount)],
            apiRequestId: apiRequestId, ct: ct);

        var reservation = new Reservation
        {
            OrganizationId = organizationId,
            ApiRequestId = apiRequestId,
            Amount = amount,
            Currency = Currency,
            CreatedAt = clock.GetUtcNow()
        };

        db.Reservations.Add(reservation);
        return reservation;
    }

    /// <summary>
    /// Charges <paramref name="charge"/> (capped at the reservation) to revenue and returns the rest
    /// to available. Returns false, changing nothing, when the reservation was already resolved.
    /// </summary>
    public async Task<bool> SettleAsync(Guid apiRequestId, decimal charge, CancellationToken ct)
    {
        var reservation = await locks.LockReservationAsync(apiRequestId, ct)
            ?? throw new InvalidOperationException($"Request {apiRequestId} has no reservation to settle.");

        if (reservation.State != ReservationState.Held)
        {
            return false;
        }

        // The estimate is the contractual maximum; usage can never charge beyond what was held.
        charge = Math.Clamp(charge, 0m, reservation.Amount);
        var refund = reservation.Amount - charge;

        var available = await locks.LockLedgerAccountAsync(reservation.OrganizationId, LedgerAccountType.CustomerAvailable, Currency, ct);
        var reserved = await locks.LockLedgerAccountAsync(reservation.OrganizationId, LedgerAccountType.CustomerReserved, Currency, ct);
        var revenue = await locks.EnsurePlatformAccountAsync(LedgerAccountType.Revenue, Currency, ct);

        await PostAsync(LedgerTransactionType.Settle, $"settle:{apiRequestId}", reservation.OrganizationId,
            $"Charged for request {apiRequestId}",
            [(reserved, -reservation.Amount), (revenue, charge), (available, refund)],
            apiRequestId: apiRequestId, ct: ct);

        reservation.State = ReservationState.Settled;
        reservation.SettledAmount = charge;
        reservation.ResolvedAt = clock.GetUtcNow();
        return true;
    }

    /// <summary>Returns the whole reservation to available. False when already resolved.</summary>
    public async Task<bool> ReleaseAsync(Guid apiRequestId, string reason, CancellationToken ct)
    {
        var reservation = await locks.LockReservationAsync(apiRequestId, ct);

        if (reservation is null || reservation.State != ReservationState.Held)
        {
            return false;
        }

        var available = await locks.LockLedgerAccountAsync(reservation.OrganizationId, LedgerAccountType.CustomerAvailable, Currency, ct);
        var reserved = await locks.LockLedgerAccountAsync(reservation.OrganizationId, LedgerAccountType.CustomerReserved, Currency, ct);

        await PostAsync(LedgerTransactionType.Release, $"release:{apiRequestId}", reservation.OrganizationId,
            $"Released for request {apiRequestId}: {reason}",
            [(reserved, -reservation.Amount), (available, reservation.Amount)],
            apiRequestId: apiRequestId, ct: ct);

        reservation.State = ReservationState.Released;
        reservation.SettledAmount = 0m;
        reservation.ResolvedAt = clock.GetUtcNow();
        return true;
    }

    /// <summary>
    /// Credits a verified payment to the company's available balance. Idempotent per payment:
    /// a duplicate or replayed gateway callback posts nothing. Returns false when already credited.
    /// </summary>
    public async Task<bool> CreditTopUpAsync(Guid organizationId, Guid paymentId, decimal amount, CancellationToken ct)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "A top-up must be positive.");
        }

        var available = await locks.LockLedgerAccountAsync(organizationId, LedgerAccountType.CustomerAvailable, Currency, ct);
        var clearing = await locks.EnsurePlatformAccountAsync(LedgerAccountType.GatewayClearing, Currency, ct);

        return await PostAsync(LedgerTransactionType.TopUp, $"topup:{paymentId}", organizationId,
            $"Top-up from payment {paymentId}",
            [(clearing, -amount), (available, amount)],
            paymentId: paymentId, ct: ct);
    }

    /// <summary>
    /// An audited manual credit (positive) or debit (negative) by platform staff, e.g. a goodwill
    /// credit. Never used to "fix" a posting; that is a reversal.
    /// </summary>
    public async Task<bool> AdjustAsync(Guid organizationId, string postingKey, decimal amount, string reason, Guid? actor, CancellationToken ct)
    {
        var available = await locks.LockLedgerAccountAsync(organizationId, LedgerAccountType.CustomerAvailable, Currency, ct);
        var adjustments = await locks.EnsurePlatformAccountAsync(LedgerAccountType.Adjustments, Currency, ct);

        if (amount < 0 && available.Balance < -amount)
        {
            throw new InsufficientCreditException(available.Balance, -amount);
        }

        return await PostAsync(LedgerTransactionType.Adjustment, $"adjust:{postingKey}", organizationId, reason,
            [(adjustments, -amount), (available, amount)], createdBy: actor, ct: ct);
    }

    public async Task<BalanceResponse> GetBalanceAsync(Guid organizationId, CancellationToken ct)
    {
        var accounts = await db.LedgerAccounts.AsNoTracking()
            .Where(a => a.OrganizationId == organizationId && a.Currency == Currency &&
                        (a.Type == LedgerAccountType.CustomerAvailable || a.Type == LedgerAccountType.CustomerReserved))
            .ToListAsync(ct);

        var available = accounts.SingleOrDefault(a => a.Type == LedgerAccountType.CustomerAvailable)?.Balance ?? 0m;
        var reserved = accounts.SingleOrDefault(a => a.Type == LedgerAccountType.CustomerReserved)?.Balance ?? 0m;

        return new BalanceResponse(Currency, available + reserved, reserved, available);
    }

    /// <summary>
    /// Writes one balanced transaction. Customer account balances move with it (their rows are
    /// locked by the caller); platform accounts carry entries only. Returns false when the posting
    /// key already exists — the movement already happened.
    /// </summary>
    private async Task<bool> PostAsync(
        LedgerTransactionType type,
        string postingKey,
        Guid organizationId,
        string description,
        IReadOnlyList<(LedgerAccount Account, decimal Amount)> lines,
        Guid? apiRequestId = null,
        Guid? paymentId = null,
        Guid? createdBy = null,
        CancellationToken ct = default)
    {
        if (await db.LedgerTransactions.AnyAsync(t => t.PostingKey == postingKey, ct))
        {
            return false;
        }

        if (lines.Sum(l => l.Amount) != 0m)
        {
            throw new InvalidOperationException($"Ledger posting '{postingKey}' does not balance.");
        }

        var transaction = new LedgerTransaction
        {
            Type = type,
            PostingKey = postingKey,
            OrganizationId = organizationId,
            ApiRequestId = apiRequestId,
            PaymentId = paymentId,
            Description = description,
            Currency = Currency,
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = createdBy
        };

        foreach (var (account, amount) in lines.Where(l => l.Amount != 0m))
        {
            transaction.Entries.Add(new LedgerEntry { TransactionId = transaction.Id, AccountId = account.Id, Amount = amount });

            if (account.Type is LedgerAccountType.CustomerAvailable or LedgerAccountType.CustomerReserved)
            {
                account.Balance += amount;
            }
        }

        db.LedgerTransactions.Add(transaction);
        return true;
    }
}
