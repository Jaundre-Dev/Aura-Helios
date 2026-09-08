namespace Helios.Worker.Agents;

/// <summary>
/// Drains the Redis agent-run queue and drives each run through the state machine.
/// Long-running AI execution lives here, never inside an HTTP request (plan 28.1).
/// </summary>
public sealed class AgentRunWorker(ILogger<AgentRunWorker> logger) : BackgroundService
{
    private readonly ILogger<AgentRunWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Helios agent run worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            // TODO Phase 2:
            //   1. Dequeue an AgentRunJob from IJobQueue.
            //   2. Take a distributed lock on the run so only one worker owns it.
            //   3. Hand it to IAgentRuntime.ExecuteAsync.
            //   4. On approval-pending, release and let the approval event requeue it.
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
