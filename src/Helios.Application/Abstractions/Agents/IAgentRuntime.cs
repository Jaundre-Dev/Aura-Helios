namespace Helios.Application.Abstractions.Agents;

/// <summary>
/// Owns the execution loop and the state machine (plan section 7). Runs are started
/// here but executed by the worker, never inside an HTTP request.
/// </summary>
public interface IAgentRuntime
{
    Task<Guid> StartRunAsync(StartAgentRunRequest request, CancellationToken cancellationToken);

    Task ExecuteAsync(Guid agentRunId, CancellationToken cancellationToken);

    Task CancelAsync(Guid agentRunId, string reason, CancellationToken cancellationToken);

    Task ResumeAfterApprovalAsync(Guid agentRunId, Guid approvalId, CancellationToken cancellationToken);
}

public sealed record StartAgentRunRequest
{
    public required Guid AgentDefinitionId { get; init; }
    public required Guid WorkspaceId { get; init; }
    public Guid? ProjectId { get; init; }
    public required string Goal { get; init; }
    public Guid? RequestedBy { get; init; }
}
