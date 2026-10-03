using Helios.Application.Abstractions.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.Application.Features.Execution;

/// <summary>
/// One worker tick: claim the next due job in a system scope (ids only), then process it in a scope
/// confined to the job's own workspace. The worker host calls this in a loop; tests call it
/// directly to step through crash, retry and reconciliation scenarios deterministically.
/// </summary>
public sealed class JobWorker(ITenantScopeFactory scopes, ExecutionPolicy policy)
{
    /// <returns>True when a job was claimed and processed; false when nothing was due.</returns>
    public async Task<bool> ProcessNextAsync(string workerId, CancellationToken ct)
    {
        ClaimedJob? claim;
        using (var system = scopes.CreateSystem())
        {
            claim = await system.ServiceProvider.GetRequiredService<IJobQueue>()
                .ClaimNextAsync(workerId, policy.Lease, ct);
        }

        if (claim is null)
        {
            return false;
        }

        using var tenant = scopes.CreateForWorkspace(claim.WorkspaceId);
        await tenant.ServiceProvider.GetRequiredService<JobRunner>().RunAsync(claim, ct);
        return true;
    }
}
