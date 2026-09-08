namespace Helios.Application.Abstractions.Messaging;

/// <summary>Redis-backed. Long-running AI work never lives in an HTTP request lifetime.</summary>
public interface IJobQueue
{
    Task EnqueueAsync<TJob>(TJob job, CancellationToken cancellationToken) where TJob : class;

    Task<TJob?> DequeueAsync<TJob>(TimeSpan wait, CancellationToken cancellationToken) where TJob : class;
}

/// <summary>The message that hands a run from the API to the worker runtime.</summary>
public sealed record AgentRunJob(Guid AgentRunId, Guid WorkspaceId, int Attempt = 1);
