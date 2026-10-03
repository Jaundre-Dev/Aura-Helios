using Helios.Domain.Identity;

namespace Helios.Application.Abstractions.Persistence;

/// <summary>
/// Locking reads for decisions that must stay true until the transaction commits. A plain read
/// inside a MySQL REPEATABLE READ transaction is a snapshot: a concurrent revocation could commit
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
}
