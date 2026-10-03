using Helios.Application.Abstractions.Execution;
using Helios.Domain.Execution;
using Helios.Infrastructure.Persistence.MySql;
using Microsoft.EntityFrameworkCore;

namespace Helios.Infrastructure.Execution;

/// <summary>
/// Claims jobs with <c>SELECT … FOR UPDATE SKIP LOCKED</c> inside a short transaction. Rows another
/// worker is claiming are skipped rather than waited on, so workers never block each other and
/// never claim the same job. The fencing token increments on every claim.
/// </summary>
public sealed class MySqlJobQueue(HeliosDbContext db, TimeProvider clock) : IJobQueue
{
    public async Task<ClaimedJob?> ClaimNextAsync(string workerId, TimeSpan lease, CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async ct =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var now = clock.GetUtcNow();
            var queued = nameof(JobStatus.Queued);
            var reconciling = nameof(JobStatus.Reconciling);
            var running = nameof(JobStatus.Running);

            var candidates = await db.Jobs
                .FromSqlInterpolated($"""
                    SELECT * FROM jobs
                    WHERE (status IN ({queued}, {reconciling}) AND available_at <= {now})
                       OR (status = {running} AND lease_expires_at < {now})
                    ORDER BY available_at
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED
                    """)
                // Claiming spans tenants by design; it only hands out ids. The job itself is then
                // processed in a scope confined to its own workspace.
                .IgnoreQueryFilters()
                .ToListAsync(ct);

            if (candidates.SingleOrDefault() is not { } job)
            {
                await transaction.RollbackAsync(ct);
                return null;
            }

            var reclaimed = job.Status == JobStatus.Running;

            job.Phase = job.Status == JobStatus.Reconciling ? JobPhase.Reconcile : job.Phase;
            job.Status = JobStatus.Running;
            job.LeaseOwner = workerId;
            job.LeaseExpiresAt = now + lease;
            job.FencingToken++;

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return new ClaimedJob(job.Id, job.OrganizationId, job.WorkspaceId, job.FencingToken, job.Phase, reclaimed);
        }, cancellationToken);
    }
}
