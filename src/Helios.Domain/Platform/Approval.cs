using Helios.Contracts.Agents;
using Helios.Contracts.Common;
using Helios.Domain.Common;

namespace Helios.Domain.Platform;

public enum ApprovalStatus
{
    Pending = 0,
    Granted,
    Rejected,
    Expired
}

/// <summary>Human control gate. High-impact actions block here (plan section 8).</summary>
public class Approval : AuditableEntity, IAggregateRoot
{
    public Guid? AgentRunId { get; set; }
    public Guid WorkspaceId { get; set; }

    public required string Action { get; set; }
    public ToolPermission Permission { get; set; }
    public RiskLevel Risk { get; set; }
    public required string Description { get; set; }
    public string? PayloadPreview { get; set; }

    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionReason { get; set; }
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddHours(24);
}
