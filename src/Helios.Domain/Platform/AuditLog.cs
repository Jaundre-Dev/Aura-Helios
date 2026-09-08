using Helios.Domain.Common;

namespace Helios.Domain.Platform;

/// <summary>Append-only. Never updated, never deleted.</summary>
public class AuditLog : Entity
{
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? ActorUserId { get; set; }
    public Guid? AgentRunId { get; set; }
    public Guid? WorkspaceId { get; set; }

    public required string Action { get; set; }
    public required string ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public bool Allowed { get; set; }
    public string? DenyReason { get; set; }
    public string? Metadata { get; set; }
    public string? IpAddress { get; set; }
}
