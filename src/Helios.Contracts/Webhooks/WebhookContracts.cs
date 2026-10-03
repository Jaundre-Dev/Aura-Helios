namespace Helios.Contracts.Webhooks;

/// <summary>The event types a customer can subscribe to.</summary>
public static class WebhookEventTypes
{
    public const string RequestSucceeded = "request.succeeded";
    public const string RequestFailed = "request.failed";
    public const string RequestNeedsReview = "request.needs_review";
    public const string RequestCancelled = "request.cancelled";

    /// <summary>A person or integration approved, corrected or rejected a result.</summary>
    public const string RequestReviewed = "request.reviewed";

    public static readonly IReadOnlyList<string> All = [RequestSucceeded, RequestFailed, RequestNeedsReview, RequestCancelled, RequestReviewed];
}

public sealed record CreateWebhookRequest(string Url, IReadOnlyList<string> Events, string? Description = null);

public sealed record WebhookEndpointResponse(
    Guid Id,
    Guid WorkspaceId,
    string Url,
    string? Description,
    IReadOnlyList<string> Events,
    bool IsActive,
    DateTimeOffset CreatedAt);

/// <summary>Returned once. Verify deliveries with HMAC-SHA256 of <c>"{t}.{body}"</c> under this secret.</summary>
public sealed record CreatedWebhookResponse(WebhookEndpointResponse Endpoint, string SigningSecret);

public sealed record WebhookDeliveryResponse(
    Guid Id,
    string EventId,
    string EventType,
    string Status,
    int Attempts,
    int? LastStatusCode,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset NextAttemptAt);
