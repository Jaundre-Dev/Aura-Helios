using System.Text.Json;
using Helios.Application.Abstractions.Persistence;
using Helios.Contracts.Requests;
using Helios.Contracts.Webhooks;
using Helios.Domain.Requests;
using Helios.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.Webhooks;

/// <summary>
/// Stages webhook deliveries on the caller's change tracker, so they commit in the same transaction
/// as the request state they describe: no event for a change that rolled back, no change without
/// its event. Payloads carry request metadata only — results are fetched with an API key.
/// </summary>
public sealed class WebhookOutbox(IHeliosDbContext db, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string EventTypeFor(ApiRequestStatus status) => status switch
    {
        ApiRequestStatus.Succeeded => WebhookEventTypes.RequestSucceeded,
        ApiRequestStatus.Failed => WebhookEventTypes.RequestFailed,
        ApiRequestStatus.NeedsReview => WebhookEventTypes.RequestNeedsReview,
        ApiRequestStatus.Cancelled => WebhookEventTypes.RequestCancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Only terminal states are published.")
    };

    public async Task EnqueueAsync(ApiRequest request, CancellationToken ct)
    {
        var eventType = EventTypeFor(request.Status);

        // Filtered explicitly to the request's own workspace, so this also works for platform staff
        // resolving a request, whose session belongs to no workspace.
        var endpoints = await db.WebhookEndpoints
            .IgnoreQueryFilters()
            .Where(e => e.WorkspaceId == request.WorkspaceId && e.IsActive)
            .ToListAsync(ct);

        var subscribed = endpoints.Where(e => e.EventList.Contains(eventType, StringComparer.Ordinal)).ToList();
        if (subscribed.Count == 0)
        {
            return;
        }

        // Deterministic per request and event type, so a delivery can never be enqueued twice.
        var eventId = $"evt_{request.Id:N}_{eventType.Replace('.', '_')}";

        // A request can reach the same state twice (sent back from review to reconciliation, then
        // to review again). The customer was already told; the unique index would otherwise roll
        // back the state change itself.
        var subscribedIds = subscribed.Select(e => e.Id).ToList();
        var alreadyQueued = await db.WebhookDeliveries
            .IgnoreQueryFilters()
            .Where(d => subscribedIds.Contains(d.EndpointId) && d.EventId == eventId)
            .Select(d => d.EndpointId)
            .ToListAsync(ct);

        subscribed = subscribed.Where(e => !alreadyQueued.Contains(e.Id)).ToList();
        if (subscribed.Count == 0)
        {
            return;
        }

        var now = clock.GetUtcNow();

        var payload = JsonSerializer.Serialize(new
        {
            id = eventId,
            type = eventType,
            createdAt = now,
            data = new
            {
                requestId = request.Id,
                product = request.ProductSlug,
                version = request.ProductVersion,
                environment = request.Environment.ToString(),
                status = request.Status.ToString(),
                error = request.ErrorCode,
                completedAt = request.CompletedAt,
                usage = new { unit = request.UsageUnit, quantity = request.UsageQuantity },
                billing = new { state = request.BillingState.ToString(), currency = request.Currency, amount = request.BillingAmount },
                resultUrl = $"/api/v1/requests/{request.Id}/result"
            }
        }, Json);

        foreach (var endpoint in subscribed)
        {
            db.WebhookDeliveries.Add(new WebhookDelivery
            {
                EndpointId = endpoint.Id,
                OrganizationId = request.OrganizationId,
                WorkspaceId = request.WorkspaceId,
                EventId = eventId,
                EventType = eventType,
                PayloadJson = payload,
                NextAttemptAt = now,
                CreatedAt = now
            });
        }
    }
}
