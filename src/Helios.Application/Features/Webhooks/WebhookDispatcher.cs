using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Helios.Application.Abstractions.Execution;
using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Abstractions.Webhooks;
using Helios.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.Application.Features.Webhooks;

/// <summary>Delivery timing, bound from <c>Helios:Webhooks</c>.</summary>
public sealed record WebhookPolicy
{
    public int MaxAttempts { get; init; } = 8;

    /// <summary>Delay after the first failure; each later delay is four times longer, capped at six hours.</summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan Lease { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Finished (delivered or dead-lettered) deliveries are deleted after this long.</summary>
    public TimeSpan DeliveryRetention { get; init; } = TimeSpan.FromDays(30);

    public TimeSpan DelayAfter(int attempts) =>
        TimeSpan.FromTicks(Math.Min(TimeSpan.FromHours(6).Ticks, BaseDelay.Ticks * (long)Math.Pow(4, Math.Max(0, attempts - 1))));
}

/// <summary>
/// Sends due webhook deliveries. Delivery is at-least-once: a lease that expires mid-send can
/// cause a repeat, so receivers deduplicate on the event id (documented with the signature scheme).
/// Each delivery is sent from a scope confined to its workspace.
/// <para>
/// Headers: <c>Helios-Event-Id</c>, <c>Helios-Event-Type</c> and
/// <c>Helios-Signature: t={unix seconds},v1={hex HMAC-SHA256 of "{t}.{body}"}</c>.
/// </para>
/// </summary>
public sealed class WebhookDispatcher(ITenantScopeFactory scopes, WebhookPolicy policy)
{
    public async Task<bool> DeliverNextAsync(CancellationToken ct)
    {
        ClaimedDelivery? claim;
        using (var system = scopes.CreateSystem())
        {
            claim = await system.ServiceProvider.GetRequiredService<IWebhookQueue>().ClaimNextAsync(policy.Lease, ct);
        }

        if (claim is null)
        {
            return false;
        }

        using var tenant = scopes.CreateForWorkspace(claim.WorkspaceId);
        await DeliverAsync(tenant.ServiceProvider, claim.DeliveryId, ct);
        return true;
    }

    private async Task DeliverAsync(IServiceProvider services, Guid deliveryId, CancellationToken ct)
    {
        var db = services.GetRequiredService<IHeliosDbContext>();
        var protector = services.GetRequiredService<IPayloadProtector>();
        var sender = services.GetRequiredService<IWebhookSender>();
        var clock = services.GetRequiredService<TimeProvider>();

        var delivery = await db.WebhookDeliveries.SingleAsync(d => d.Id == deliveryId, ct);
        var endpoint = await db.WebhookEndpoints.SingleAsync(e => e.Id == delivery.EndpointId, ct);

        if (!endpoint.IsActive)
        {
            delivery.Status = WebhookDeliveryStatus.Failed;
            delivery.LastError = "endpoint_inactive";
            delivery.LeaseExpiresAt = null;
            await db.SaveChangesAsync(ct);
            return;
        }

        var secret = protector.Unprotect(endpoint.SecretKeyId, endpoint.SecretEnvelope);
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds();

        var headers = new Dictionary<string, string>
        {
            ["Helios-Event-Id"] = delivery.EventId,
            ["Helios-Event-Type"] = delivery.EventType,
            ["Helios-Signature"] = $"t={timestamp},v1={Sign(secret, delivery.PayloadJson, timestamp)}",
        };

        var result = await sender.SendAsync(new Uri(endpoint.Url), delivery.PayloadJson, headers, ct);
        var now = clock.GetUtcNow();

        delivery.LastStatusCode = result.StatusCode;
        delivery.LastError = result.Succeeded ? null : Truncate(result.Error ?? $"HTTP {result.StatusCode}");
        delivery.LeaseExpiresAt = null;

        if (result.Succeeded)
        {
            delivery.Status = WebhookDeliveryStatus.Delivered;
            delivery.DeliveredAt = now;
        }
        else if (delivery.Attempts >= policy.MaxAttempts)
        {
            delivery.Status = WebhookDeliveryStatus.Failed;
        }
        else
        {
            delivery.NextAttemptAt = now + policy.DelayAfter(delivery.Attempts);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>The <c>v1</c> signature receivers recompute to authenticate a delivery.</summary>
    public static string Sign(string secret, string body, long timestamp) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{timestamp}.{body}"))));

    private static string Truncate(string value) => value.Length <= 1000 ? value : value[..1000];
}
