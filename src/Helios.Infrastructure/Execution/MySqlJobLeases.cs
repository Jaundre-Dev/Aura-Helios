using Helios.Application.Abstractions.Execution;
using Helios.Domain.Execution;
using Helios.Infrastructure.Persistence.MySql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.Infrastructure.Execution;

/// <summary>
/// Lease renewal as one conditional UPDATE on its own scope and connection. Matching on the fencing
/// token means a worker whose job was reclaimed can never extend the new owner's lease.
/// </summary>
public sealed class MySqlJobLeases(IServiceScopeFactory scopes, TimeProvider clock) : IJobLeases
{
    public async Task<bool> RenewAsync(Guid jobId, long fencingToken, TimeSpan lease, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
        var expires = clock.GetUtcNow() + lease;

        // By id and token only: a job id is not tenant data, and the token proves ownership.
        var updated = await db.Jobs
            .IgnoreQueryFilters()
            .Where(j => j.Id == jobId && j.FencingToken == fencingToken && j.Status == JobStatus.Running)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.LeaseExpiresAt, expires), cancellationToken);

        return updated == 1;
    }
}
