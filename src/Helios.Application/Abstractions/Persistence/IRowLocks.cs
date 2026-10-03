using Helios.Contracts.Billing;
using Helios.Domain.Billing;
using Helios.Domain.Identity;

namespace Helios.Application.Abstractions.Persistence;

/// <summary>
/// Locking reads for decisions that must stay true until the transaction commits. A plain read
/// inside a MySQL REPEATABLE READ transaction is a snapshot: a concurrent writer could commit
/// between the check and the write it authorises. These reads take a row lock instead, so the
/// competing write waits. Only meaningful inside <see cref="IUnitOfWork.ExecuteInTransactionAsync{T}"/>.
/// </summary>
public interface IRowLocks
{
    /// <summary>
    /// The caller's active membership of an active organisation, share-locked until commit, or
    /// null when there is none. A concurrent deactivation of the membership blocks until then.
    /// </summary>
    Task<OrganizationMember?> LockActiveOrganizationMembershipAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The ledger account, created if it does not yet exist, exclusively locked until commit. Every
    /// posting locks its accounts through this, which serialises concurrent spending of one wallet.
    /// </summary>
    Task<LedgerAccount> LockLedgerAccountAsync(
        Guid ownerId,
        LedgerAccountType type,
        string currency,
        CancellationToken cancellationToken);

    /// <summary>
    /// A platform account (revenue, clearing, adjustments), created if missing, <em>without</em> a
    /// lock. Platform accounts receive entries from every tenant; locking them would serialise all
    /// settlements globally, and nothing is ever spent from them, so they need no projection.
    /// </summary>
    Task<LedgerAccount> EnsurePlatformAccountAsync(
        LedgerAccountType type,
        string currency,
        CancellationToken cancellationToken);

    /// <summary>A request's reservation, exclusively locked until commit, or null when there is none.</summary>
    Task<Reservation?> LockReservationAsync(Guid apiRequestId, CancellationToken cancellationToken);
}
