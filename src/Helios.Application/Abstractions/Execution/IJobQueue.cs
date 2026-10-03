using Helios.Domain.Execution;

namespace Helios.Application.Abstractions.Execution;

/// <summary>
/// The durable job queue: MySQL rows claimed with <c>FOR UPDATE SKIP LOCKED</c>, so concurrent
/// workers never claim the same job, and a crashed worker's job is reclaimed once its lease
/// expires. Delivery is at-least-once; fencing tokens and unique settlement keys make the
/// effects exactly-once.
/// </summary>
public interface IJobQueue
{
    /// <summary>
    /// Leases the next due job — queued and available, reconciling and due, or running with an
    /// expired lease — to <paramref name="workerId"/>, incrementing its fencing token.
    /// </summary>
    Task<ClaimedJob?> ClaimNextAsync(string workerId, TimeSpan lease, CancellationToken cancellationToken);
}

/// <param name="ReclaimedFromExpiredLease">
/// True when another worker held this job and its lease ran out: that worker may have been
/// mid-execution, so a non-repeatable executor must reconcile rather than run again.
/// </param>
public sealed record ClaimedJob(
    Guid JobId,
    Guid OrganizationId,
    Guid WorkspaceId,
    long FencingToken,
    JobPhase Phase,
    bool ReclaimedFromExpiredLease);
