namespace Helios.Application.Abstractions.Execution;

/// <summary>
/// Extends a running job's lease, only while the caller still holds it: the update is conditioned
/// on the job id, the claim's fencing token and the running state. Uses its own connection, so it
/// can run beside an executor that is using the request's database context.
/// </summary>
public interface IJobLeases
{
    /// <returns>False when the lease was lost (another worker took over, or the job left Running).</returns>
    Task<bool> RenewAsync(Guid jobId, long fencingToken, TimeSpan lease, CancellationToken cancellationToken);
}
