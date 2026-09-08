namespace Helios.Contracts.Events;

/// <summary>
/// Every important state change becomes an event, is published to Redis and
/// reaches the UI through SignalR (plan section 10).
/// </summary>
public abstract record HeliosEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    public Guid? WorkspaceId { get; init; }
    public Guid? ProjectId { get; init; }
    public Guid? AgentRunId { get; init; }
    public Guid? ActorUserId { get; init; }

    public abstract string EventType { get; }
}
