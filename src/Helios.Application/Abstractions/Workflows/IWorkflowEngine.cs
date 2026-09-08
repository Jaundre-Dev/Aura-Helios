namespace Helios.Application.Abstractions.Workflows;

/// <summary>Plan section 12. Workflows are versioned and produce WorkflowRun records.</summary>
public interface IWorkflowEngine
{
    Task<Guid> StartAsync(Guid workflowVersionId, string inputJson, CancellationToken cancellationToken);

    Task ExecuteAsync(Guid workflowRunId, CancellationToken cancellationToken);

    Task CancelAsync(Guid workflowRunId, string reason, CancellationToken cancellationToken);
}

public enum WorkflowNodeKind
{
    Trigger = 0,
    Agent,
    Model,
    Tool,
    Condition,
    HumanApproval,
    Parallel,
    Loop,
    Transform,
    Wait,
    Webhook,
    Notification
}
