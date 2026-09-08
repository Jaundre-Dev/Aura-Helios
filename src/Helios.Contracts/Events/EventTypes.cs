namespace Helios.Contracts.Events;

/// <summary>Canonical event names. Keep in sync with the web client.</summary>
public static class EventTypes
{
    public const string AgentStarted    = "agent.started";
    public const string AgentCompleted  = "agent.completed";
    public const string AgentFailed     = "agent.failed";
    public const string AgentStateChanged = "agent.state_changed";

    public const string ModelRequestStarted   = "model.request_started";
    public const string ModelRequestCompleted = "model.request_completed";

    public const string ToolStarted   = "tool.started";
    public const string ToolCompleted = "tool.completed";
    public const string ToolFailed    = "tool.failed";

    public const string ApprovalRequested = "approval.requested";
    public const string ApprovalGranted   = "approval.granted";
    public const string ApprovalRejected  = "approval.rejected";

    public const string WorkflowStarted   = "workflow.started";
    public const string WorkflowCompleted = "workflow.completed";
    public const string WorkflowFailed    = "workflow.failed";

    public const string SecurityFindingCreated  = "security.finding_created";
    public const string SecurityFindingResolved = "security.finding_resolved";

    public const string TestStarted   = "test.started";
    public const string TestCompleted = "test.completed";
    public const string TestFailed    = "test.failed";

    public const string IncidentCreated  = "incident.created";
    public const string IncidentUpdated  = "incident.updated";
    public const string IncidentResolved = "incident.resolved";

    public const string ArtifactCreated = "artifact.created";
}
