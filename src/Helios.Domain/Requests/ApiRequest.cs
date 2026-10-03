using Helios.Contracts.Catalogue;
using Helios.Contracts.Requests;
using Helios.Domain.Common;

namespace Helios.Domain.Requests;

/// <summary>
/// The durable record of one product execution: who asked, under which product version and
/// environment, what happened, what was measured and what it cost. Raw input is never stored —
/// only a keyed fingerprint for idempotency — and the result expires under the retention policy.
/// </summary>
public class ApiRequest : Entity, IAggregateRoot
{
    public Guid OrganizationId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ProductId { get; set; }
    public required string ProductSlug { get; set; }
    public required string ProductVersion { get; set; }
    public ApiEnvironment Environment { get; set; }

    /// <summary><c>api</c> for API-key calls, <c>portal</c> for signed-in users.</summary>
    public required string Channel { get; set; }

    public Guid? ActorUserId { get; set; }
    public Guid? ApiKeyId { get; set; }

    public ApiRequestStatus Status { get; set; }

    public string? IdempotencyKey { get; set; }

    /// <summary>Which secret key derived the fingerprint, so it can be recomputed after rotation.</summary>
    public required string FingerprintKeyId { get; set; }

    /// <summary>HMAC of the canonical input. Keyed, so a low-entropy input cannot be brute-forced back out.</summary>
    public required string PayloadFingerprint { get; set; }

    public string? ResultJson { get; set; }
    public string? WarningsJson { get; set; }
    public bool ReviewRequired { get; set; }
    public string? ErrorCode { get; set; }

    public required string UsageUnit { get; set; }
    public decimal UsageQuantity { get; set; }

    public BillingState BillingState { get; set; }
    public required string Currency { get; set; }

    /// <summary>Fixed precision (decimal(18,6)); never binary floating point.</summary>
    public decimal BillingAmount { get; set; }

    /// <summary>The price version this request is charged under; null when not billable.</summary>
    public Guid? PriceVersionId { get; set; }

    /// <summary>The maximum held for this request at acceptance; null when not billable.</summary>
    public decimal? ReservedAmount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>After this the result payload is no longer served and is purged by retention.</summary>
    public DateTimeOffset? ResultExpiresAt { get; set; }
}
