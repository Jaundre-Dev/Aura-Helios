using System.Security.Cryptography;
using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Abstractions.Webhooks;
using Helios.Application.Common;
using Helios.Application.Features.Identity;
using Helios.Contracts.Organizations;
using Helios.Contracts.Webhooks;
using Helios.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.Webhooks;

/// <summary>
/// Webhook endpoint management in the caller's current workspace. Same permission as API keys
/// (<c>ManageApiKeys</c>: Owner, Admin, Developer): both are integration configuration.
/// </summary>
public sealed class WebhookService(
    IHeliosDbContext db,
    OrganizationAccess access,
    IWorkspaceContext context,
    IOutboundUrlPolicy urlPolicy,
    IPayloadProtector protector,
    IAuditWriter audit)
{
    public const int MaxEndpointsPerWorkspace = 10;

    public async Task<CreatedWebhookResponse> CreateAsync(CreateWebhookRequest request, CancellationToken ct)
    {
        var (organizationId, workspaceId) = await RequireAsync(ct);

        if (urlPolicy.ValidateForRegistration(request.Url) is { } problem)
        {
            throw new BadRequestException(problem, "invalid_webhook_url");
        }

        var events = request.Events.Distinct(StringComparer.Ordinal).ToList();
        var unknown = events.Except(WebhookEventTypes.All, StringComparer.Ordinal).ToList();
        if (events.Count == 0 || unknown.Count > 0)
        {
            throw new BadRequestException(
                $"Subscribe to one or more of: {string.Join(", ", WebhookEventTypes.All)}.", "invalid_webhook_events");
        }

        if (await db.WebhookEndpoints.CountAsync(e => e.WorkspaceId == workspaceId && e.IsActive, ct) >= MaxEndpointsPerWorkspace)
        {
            throw new ConflictException($"A workspace can have at most {MaxEndpointsPerWorkspace} active webhooks.", "webhook_limit");
        }

        var secret = "whsec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var sealedSecret = protector.Protect(secret);

        var endpoint = new WebhookEndpoint
        {
            OrganizationId = organizationId,
            WorkspaceId = workspaceId,
            Url = request.Url,
            Description = request.Description?.Trim(),
            Events = string.Join(',', events),
            SecretEnvelope = sealedSecret.Envelope,
            SecretKeyId = sealedSecret.KeyId
        };

        db.WebhookEndpoints.Add(endpoint);
        audit.Record("webhook.create", nameof(WebhookEndpoint), endpoint.Id.ToString(), organizationId: organizationId,
            metadataJson: System.Text.Json.JsonSerializer.Serialize(new { url = request.Url, events }));
        await db.SaveChangesAsync(ct);

        return new CreatedWebhookResponse(ToResponse(endpoint), secret);
    }

    public async Task<IReadOnlyList<WebhookEndpointResponse>> ListAsync(CancellationToken ct)
    {
        var (_, workspaceId) = await RequireAsync(ct);

        var endpoints = await db.WebhookEndpoints.AsNoTracking()
            .Where(e => e.WorkspaceId == workspaceId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(ct);

        return endpoints.Select(ToResponse).ToList();
    }

    /// <summary>Stops future deliveries; pending ones are dead-lettered when they come due.</summary>
    public async Task DeactivateAsync(Guid id, CancellationToken ct)
    {
        var (organizationId, _) = await RequireAsync(ct);

        var endpoint = await db.WebhookEndpoints.SingleOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new NotFoundException("Webhook", id);

        endpoint.IsActive = false;
        audit.Record("webhook.deactivate", nameof(WebhookEndpoint), id.ToString(), organizationId: organizationId);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<WebhookDeliveryResponse>> ListDeliveriesAsync(Guid endpointId, CancellationToken ct)
    {
        await RequireAsync(ct);

        if (!await db.WebhookEndpoints.AnyAsync(e => e.Id == endpointId, ct))
        {
            throw new NotFoundException("Webhook", endpointId);
        }

        return await db.WebhookDeliveries.AsNoTracking()
            .Where(d => d.EndpointId == endpointId)
            .OrderByDescending(d => d.CreatedAt)
            .Take(200)
            .Select(d => new WebhookDeliveryResponse(d.Id, d.EventId, d.EventType, d.Status.ToString(), d.Attempts,
                d.LastStatusCode, d.LastError, d.CreatedAt, d.DeliveredAt, d.NextAttemptAt))
            .ToListAsync(ct);
    }

    private async Task<(Guid OrganizationId, Guid WorkspaceId)> RequireAsync(CancellationToken ct)
    {
        if (context.ApiKeyId is not null)
        {
            throw new ForbiddenException("API keys cannot manage webhooks; sign in to the portal.", "user_required");
        }

        var workspaceId = context.WorkspaceId
            ?? throw new ForbiddenException("Select a workspace first.", "workspace_required");

        var organizationId = await db.Workspaces.Where(w => w.Id == workspaceId).Select(w => w.OrganizationId).SingleAsync(ct);
        await access.RequireAsync(organizationId, OrganizationPermission.ManageApiKeys, ct);

        return (organizationId, workspaceId);
    }

    private static WebhookEndpointResponse ToResponse(WebhookEndpoint e) =>
        new(e.Id, e.WorkspaceId, e.Url, e.Description, e.EventList, e.IsActive, e.CreatedAt);
}
