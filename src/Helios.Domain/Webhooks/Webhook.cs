using Helios.Domain.Common;

namespace Helios.Domain.Webhooks;

/// <summary>
/// A customer-owned HTTPS destination for event notifications, in one workspace. The signing
/// secret is stored sealed under the server keyring and shown to the customer once.
/// </summary>
public class WebhookEndpoint : AuditableEntity, IAggregateRoot
{
    public Guid OrganizationId { get; set; }
    public Guid WorkspaceId { get; set; }
    public required string Url { get; set; }
    public string? Description { get; set; }

    /// <summary>Subscribed event types, comma separated.</summary>
    public required string Events { get; set; }

    public required byte[] SecretEnvelope { get; set; }
    public required string SecretKeyId { get; set; }
    public bool IsActive { get; set; } = true;

    public IReadOnlyList<string> EventList =>
        Events.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public enum WebhookDeliveryStatus
{
    Pending,
    Delivered,

    /// <summary>Dead-lettered after the final attempt; visible to the customer and to operations.</summary>
    Failed
}

/// <summary>
/// One event to one endpoint, written in the same transaction as the state change it reports
/// (a transactional outbox), so an event is never lost and never invented. Unique per endpoint and
/// event, so a retried state change cannot enqueue it twice.
/// </summary>
public class WebhookDelivery : Entity
{
    public Guid EndpointId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid WorkspaceId { get; set; }
    public required string EventId { get; set; }
    public required string EventType { get; set; }

    /// <summary>The redacted event body: request metadata, never result payloads.</summary>
    public required string PayloadJson { get; set; }

    public WebhookDeliveryStatus Status { get; set; } = WebhookDeliveryStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public int? LastStatusCode { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
}
