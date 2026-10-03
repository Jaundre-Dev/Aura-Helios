namespace Helios.Application.Abstractions.Webhooks;

/// <summary>
/// Decides which customer-supplied URLs HELIOS may call (plan section 13: SSRF). Registration-time
/// checks catch obvious abuse; the sender re-checks the actual resolved address on every connection,
/// because DNS can change after registration.
/// </summary>
public interface IOutboundUrlPolicy
{
    /// <summary>A reason the URL is not acceptable as a webhook destination, or null when it is.</summary>
    string? ValidateForRegistration(string url);
}

/// <summary>Sends one signed delivery. Never follows redirects; never connects to a private address.</summary>
public interface IWebhookSender
{
    Task<WebhookSendResult> SendAsync(
        Uri destination,
        string body,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken);
}

/// <param name="StatusCode">The HTTP status received, or null when no response arrived.</param>
public sealed record WebhookSendResult(int? StatusCode, string? Error)
{
    public bool Succeeded => StatusCode is >= 200 and < 300;
}

/// <summary>Durable delivery queue over the outbox table, claimed like jobs: leased, SKIP LOCKED.</summary>
public interface IWebhookQueue
{
    Task<ClaimedDelivery?> ClaimNextAsync(TimeSpan lease, CancellationToken cancellationToken);
}

public sealed record ClaimedDelivery(Guid DeliveryId, Guid WorkspaceId);
