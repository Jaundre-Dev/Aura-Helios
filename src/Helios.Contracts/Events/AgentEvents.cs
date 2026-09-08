using Helios.Contracts.Agents;

namespace Helios.Contracts.Events;

public sealed record AgentStartedEvent : HeliosEvent
{
    public override string EventType => EventTypes.AgentStarted;
    public required Guid AgentId { get; init; }
    public required string AgentName { get; init; }
    public required string Goal { get; init; }
}

public sealed record AgentStateChangedEvent : HeliosEvent
{
    public override string EventType => EventTypes.AgentStateChanged;
    public required AgentRunState From { get; init; }
    public required AgentRunState To { get; init; }
    public string? Reason { get; init; }
}

public sealed record AgentCompletedEvent : HeliosEvent
{
    public override string EventType => EventTypes.AgentCompleted;
    public required TimeSpan Duration { get; init; }
    public string? Summary { get; init; }
}

public sealed record AgentFailedEvent : HeliosEvent
{
    public override string EventType => EventTypes.AgentFailed;
    public required string Error { get; init; }
    public bool WillRetry { get; init; }
}

public sealed record ToolStartedEvent : HeliosEvent
{
    public override string EventType => EventTypes.ToolStarted;
    public required string ToolName { get; init; }
    public string? ArgumentsPreview { get; init; }
}

public sealed record ToolCompletedEvent : HeliosEvent
{
    public override string EventType => EventTypes.ToolCompleted;
    public required string ToolName { get; init; }
    public required bool Success { get; init; }
    public TimeSpan Duration { get; init; }
}

public sealed record ApprovalRequestedEvent : HeliosEvent
{
    public override string EventType => EventTypes.ApprovalRequested;
    public required Guid ApprovalId { get; init; }
    public required ToolPermission Permission { get; init; }
    public required string Description { get; init; }
}
