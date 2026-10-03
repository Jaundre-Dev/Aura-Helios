using Helios.Domain.Common;

namespace Helios.Domain.Platform;

public enum AdjustmentApprovalState
{
    Pending,
    Approved,
    Rejected
}

/// <summary>
/// A credit adjustment above the approval threshold, waiting for a second staff member (four-eyes
/// control). Nothing touches the ledger until someone other than the requester approves it.
/// </summary>
public class CreditAdjustmentApproval : Entity
{
    public Guid OrganizationId { get; set; }
    public decimal Amount { get; set; }
    public required string Reason { get; set; }
    public required string Reference { get; set; }
    public Guid RequestedBy { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public AdjustmentApprovalState State { get; set; } = AdjustmentApprovalState.Pending;
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionReason { get; set; }
}
