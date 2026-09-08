namespace Helios.Contracts.Agents;

/// <summary>Plan section 7.1. Transitions are enforced by the agent runtime.</summary>
public enum AgentRunState
{
    Created = 0,
    Queued,
    Planning,
    AwaitingApproval,
    Executing,
    WaitingForTool,
    Evaluating,
    Completed,
    Failed,
    Retrying,
    NeedsHuman,
    Cancelled
}
