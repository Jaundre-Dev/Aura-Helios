using System.Text.Json;
using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Common;
using Helios.Application.Features.Billing;
using Helios.Application.Features.Webhooks;
using Helios.Contracts.Platform;
using Helios.Contracts.Requests;
using Helios.Domain.Execution;
using Helios.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.Platform;

/// <summary>
/// Operational queues: requests whose provider outcome could not be established automatically,
/// failed and reconciling jobs, and dead-lettered webhooks. These read across tenants by design
/// (query filters are bypassed explicitly), return metadata only, and every intervention is
/// audited against the tenant it touched.
/// </summary>
public sealed class PlatformOperationsService(
    IHeliosDbContext db,
    IUnitOfWork unitOfWork,
    PlatformAccess access,
    LedgerService ledger,
    WebhookOutbox outbox,
    IAuditWriter audit,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<PlatformJobResponse>> ListJobsAsync(JobStatus? status, int limit, CancellationToken ct)
    {
        await access.RequireAsync(PlatformPermission.ViewOperations, ct);

        var jobs = db.Jobs.IgnoreQueryFilters().AsNoTracking();
        jobs = status is { } wanted
            ? jobs.Where(j => j.Status == wanted)
            : jobs.Where(j => j.Status == JobStatus.NeedsReview || j.Status == JobStatus.Reconciling);

        var rows = await (
            from job in jobs
            join request in db.ApiRequests.IgnoreQueryFilters() on job.ApiRequestId equals request.Id
            orderby job.CreatedAt
            select new { job, request })
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);

        return rows.Select(x => new PlatformJobResponse(x.job.Id, x.request.Id, x.request.OrganizationId,
            x.request.ProductSlug, x.request.ProductVersion, x.request.Environment, x.job.Status.ToString(),
            x.request.Status, x.request.BillingState, x.request.ReservedAmount, x.job.Attempts,
            x.job.ReconcileAttempts, x.job.ProviderReference, x.job.LastError, x.job.CreatedAt,
            x.job.AvailableAt)).ToList();
    }

    /// <summary>
    /// Resolves a request that automatic reconciliation handed to a person, with its money still
    /// held. <see cref="ResolutionAction.Release"/> fails the request and returns the reservation
    /// in full — nothing is charged for an outcome nobody could establish.
    /// <see cref="ResolutionAction.Reconcile"/> asks the provider again with a fresh budget.
    /// There is deliberately no "settle": charging needs a result the customer received.
    /// </summary>
    public async Task<PlatformJobResponse> ResolveAsync(Guid requestId, ResolveRequestRequest decision, CancellationToken ct)
    {
        await access.RequireAsync(PlatformPermission.ResolveRequests, ct);

        try
        {
            await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var job = await db.Jobs.IgnoreQueryFilters().SingleOrDefaultAsync(j => j.ApiRequestId == requestId, token)
                    ?? throw new NotFoundException("Request", requestId);
                var request = await db.ApiRequests.IgnoreQueryFilters().SingleAsync(r => r.Id == requestId, token);

                if (job.Status != JobStatus.NeedsReview)
                {
                    throw new ConflictException($"This request's job is {job.Status}, not awaiting review.", "not_awaiting_review");
                }

                var now = clock.GetUtcNow();

                // Moving the fencing token makes this a conditional update: a concurrent
                // resolution of the same job commits nothing.
                job.FencingToken++;

                if (decision.Action == ResolutionAction.Release)
                {
                    if (request.PriceVersionId is not null)
                    {
                        await ledger.ReleaseAsync(request.Id, "resolved: outcome unknown", token);
                        request.BillingState = BillingState.Released;
                    }

                    request.Status = ApiRequestStatus.Failed;
                    request.ErrorCode = "outcome_unknown_released";
                    request.BillingAmount = 0m;
                    request.CompletedAt = now;

                    job.Status = JobStatus.Failed;
                    job.CompletedAt = now;
                    job.InputEnvelope = null;
                    job.InputKeyId = null;

                    await outbox.EnqueueAsync(request, token);
                }
                else
                {
                    job.Status = JobStatus.Reconciling;
                    job.Phase = JobPhase.Reconcile;
                    job.ReconcileAttempts = 0;
                    job.AvailableAt = now;

                    request.Status = ApiRequestStatus.Reconciling;
                    request.ErrorCode = null;
                }

                audit.Record("platform.request.resolve", "ApiRequest", request.Id.ToString(),
                    organizationId: request.OrganizationId,
                    workspaceId: request.WorkspaceId,
                    metadataJson: JsonSerializer.Serialize(new { action = decision.Action.ToString(), reason = decision.Reason }));

                return true;
            }, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("This request was resolved by someone else at the same time.", "not_awaiting_review");
        }

        return await ListOneAsync(requestId, ct);
    }

    public async Task<IReadOnlyList<PlatformDeliveryResponse>> ListDeliveriesAsync(WebhookDeliveryStatus? status, int limit, CancellationToken ct)
    {
        await access.RequireAsync(PlatformPermission.ViewOperations, ct);

        var wanted = status ?? WebhookDeliveryStatus.Failed;

        return await db.WebhookDeliveries.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.Status == wanted)
            .OrderByDescending(d => d.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(d => new PlatformDeliveryResponse(d.Id, d.OrganizationId, d.EndpointId, d.EventId, d.EventType,
                d.Status.ToString(), d.Attempts, d.LastStatusCode, d.LastError, d.CreatedAt, d.NextAttemptAt))
            .ToListAsync(ct);
    }

    /// <summary>Re-queues a dead-lettered delivery to a still-active endpoint, with a fresh attempt budget.</summary>
    public async Task<PlatformDeliveryResponse> RetryDeliveryAsync(Guid deliveryId, CancellationToken ct)
    {
        await access.RequireAsync(PlatformPermission.RetryDeliveries, ct);

        var delivery = await db.WebhookDeliveries.IgnoreQueryFilters().SingleOrDefaultAsync(d => d.Id == deliveryId, ct)
            ?? throw new NotFoundException("Webhook delivery", deliveryId);

        if (delivery.Status != WebhookDeliveryStatus.Failed)
        {
            throw new ConflictException($"This delivery is {delivery.Status}; only dead letters can be retried.", "not_dead_lettered");
        }

        var endpointActive = await db.WebhookEndpoints.IgnoreQueryFilters()
            .AnyAsync(e => e.Id == delivery.EndpointId && e.IsActive, ct);

        if (!endpointActive)
        {
            throw new ConflictException("The endpoint has been deactivated by its owner.", "endpoint_inactive");
        }

        delivery.Status = WebhookDeliveryStatus.Pending;
        delivery.Attempts = 0;
        delivery.NextAttemptAt = clock.GetUtcNow();
        delivery.LeaseExpiresAt = null;

        audit.Record("platform.webhook.retry", nameof(WebhookDelivery), delivery.Id.ToString(),
            organizationId: delivery.OrganizationId, workspaceId: delivery.WorkspaceId);

        await db.SaveChangesAsync(ct);

        return new PlatformDeliveryResponse(delivery.Id, delivery.OrganizationId, delivery.EndpointId, delivery.EventId,
            delivery.EventType, delivery.Status.ToString(), delivery.Attempts, delivery.LastStatusCode, delivery.LastError,
            delivery.CreatedAt, delivery.NextAttemptAt);
    }

    private async Task<PlatformJobResponse> ListOneAsync(Guid requestId, CancellationToken ct)
    {
        var row = await (
            from job in db.Jobs.IgnoreQueryFilters().AsNoTracking()
            where job.ApiRequestId == requestId
            join request in db.ApiRequests.IgnoreQueryFilters() on job.ApiRequestId equals request.Id
            select new { job, request })
            .SingleAsync(ct);

        return new PlatformJobResponse(row.job.Id, row.request.Id, row.request.OrganizationId, row.request.ProductSlug,
            row.request.ProductVersion, row.request.Environment, row.job.Status.ToString(), row.request.Status,
            row.request.BillingState, row.request.ReservedAmount, row.job.Attempts, row.job.ReconcileAttempts,
            row.job.ProviderReference, row.job.LastError, row.job.CreatedAt, row.job.AvailableAt);
    }
}
