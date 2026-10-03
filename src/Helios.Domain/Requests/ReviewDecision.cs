using Helios.Contracts.Requests;
using Helios.Domain.Common;

namespace Helios.Domain.Requests;

/// <summary>
/// A person's (or an integration's) decision about a request's result: approve, correct or reject.
/// Append-only: the original result is never edited, every decision is kept with who made it and
/// why, and the corrections in force are the decisions applied in order (plan section 7,
/// ReviewDecision). Corrected values are personal data and are purged with the result.
/// </summary>
public class ReviewDecision : Entity
{
    public Guid ApiRequestId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid WorkspaceId { get; set; }

    public ReviewDecisionType Decision { get; set; }

    /// <summary>Path → new value as JSON; null for approve/reject and after retention purge.</summary>
    public string? CorrectionsJson { get; set; }

    public string? Reason { get; set; }

    public Guid? ActorUserId { get; set; }
    public Guid? ApiKeyId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When retention removed the corrected values; the decision itself is kept.</summary>
    public DateTimeOffset? PurgedAt { get; set; }
}
