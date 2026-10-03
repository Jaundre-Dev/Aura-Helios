using Helios.Application.Abstractions.Execution;
using Helios.Application.Abstractions.Security;
using Helios.Application.Features.Execution;
using Microsoft.Extensions.Options;

namespace Helios.Worker;

public sealed class WorkerOptions
{
    public const string SectionName = "Helios:Worker";

    /// <summary>Jobs processed in parallel by this process.</summary>
    public int Concurrency { get; init; } = 4;

    /// <summary>Idle wait between polls when no job is due.</summary>
    public double PollSeconds { get; init; } = 2;
}

/// <summary>
/// The worker loop: several independent pollers, each claiming and running one job at a time.
/// MySQL is the queue, so a restart loses nothing: an in-flight job's lease simply expires and the
/// job is reclaimed — and reconciled rather than re-run when its executor is not safe to repeat.
/// </summary>
public sealed class JobProcessingService(
    JobWorker worker,
    IOptions<WorkerOptions> options,
    ILogger<JobProcessingService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        logger.LogInformation("HELIOS worker started with {Concurrency} pollers.", settings.Concurrency);

        return Task.WhenAll(Enumerable.Range(0, Math.Max(1, settings.Concurrency))
            .Select(index => PollAsync($"{Environment.MachineName}:{Environment.ProcessId}:{index}", settings, stoppingToken)));
    }

    private async Task PollAsync(string workerId, WorkerOptions settings, CancellationToken stoppingToken)
    {
        var idle = TimeSpan.FromSeconds(settings.PollSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await worker.ProcessNextAsync(workerId, stoppingToken))
                {
                    await Task.Delay(idle, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The job keeps its lease and is reclaimed when it expires; nothing is lost.
                logger.LogError(ex, "Worker {WorkerId} failed while processing a job.", workerId);
                await Task.Delay(idle, stoppingToken);
            }
        }
    }
}

/// <summary>
/// The worker's acting identity, set per scope. Defaults to neither a workspace nor system, so a
/// scope nobody configured sees no tenant rows at all.
/// </summary>
public sealed class WorkerWorkspaceContext : IWorkspaceContext
{
    public Guid? UserId => null;
    public Guid? WorkspaceId { get; set; }
    public Guid? ApiKeyId => null;
    public bool IsSystem { get; set; }
    public string? IpAddress => null;
    public string? CorrelationId { get; set; }
}

public sealed class WorkerTenantScopes(IServiceScopeFactory scopes) : ITenantScopeFactory
{
    public IServiceScope CreateForWorkspace(Guid workspaceId)
    {
        var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WorkerWorkspaceContext>();
        context.WorkspaceId = workspaceId;
        context.CorrelationId = $"job:{Guid.CreateVersion7():N}";
        return scope;
    }

    public IServiceScope CreateSystem()
    {
        var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<WorkerWorkspaceContext>().IsSystem = true;
        return scope;
    }
}
