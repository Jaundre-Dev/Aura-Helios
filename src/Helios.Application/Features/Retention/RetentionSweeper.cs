using Helios.Application.Abstractions.Execution;
using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Abstractions.Storage;
using Helios.Application.Features.Webhooks;
using Helios.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.Application.Features.Retention;

/// <summary>
/// Enforces retention (plan section 13): deletes uploaded document content, stored result payloads
/// and corrected values once they expire, keeping the metadata needed for billing and audit, and
/// deletes finished webhook deliveries after <see cref="WebhookPolicy.DeliveryRetention"/>. Finding
/// what is due is the only cross-tenant step; each workspace's deletions run in a scope confined to it.
/// </summary>
public sealed class RetentionSweeper(ITenantScopeFactory scopes, WebhookPolicy webhooks, TimeProvider clock)
{
    /// <returns>The number of uploads, results, corrections and deliveries purged.</returns>
    public async Task<int> SweepAsync(int maxWorkspaces, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var deliveriesBefore = now - webhooks.DeliveryRetention;
        List<Guid> workspaces;

        using (var system = scopes.CreateSystem())
        {
            var db = system.ServiceProvider.GetRequiredService<IHeliosDbContext>();

            var withUploads = db.Uploads
                .Where(u => u.DeletedAt == null && u.ExpiresAt <= now)
                .Select(u => u.WorkspaceId);

            var withResults = db.ApiRequests
                .Where(r => r.ResultJson != null && r.ResultExpiresAt <= now)
                .Select(r => r.WorkspaceId);

            var withCorrections = db.ReviewDecisions
                .Where(d => d.CorrectionsJson != null &&
                            db.ApiRequests.Any(r => r.Id == d.ApiRequestId && r.ResultExpiresAt <= now))
                .Select(d => d.WorkspaceId);

            var withDeliveries = db.WebhookDeliveries
                .Where(d => d.Status != WebhookDeliveryStatus.Pending && d.CreatedAt <= deliveriesBefore)
                .Select(d => d.WorkspaceId);

            workspaces = await withUploads.Union(withResults).Union(withCorrections).Union(withDeliveries)
                .Distinct().Take(maxWorkspaces).ToListAsync(ct);
        }

        var purged = 0;
        foreach (var workspaceId in workspaces)
        {
            using var tenant = scopes.CreateForWorkspace(workspaceId);
            purged += await SweepWorkspaceAsync(tenant.ServiceProvider, now, deliveriesBefore, ct);
        }

        return purged;
    }

    private static async Task<int> SweepWorkspaceAsync(IServiceProvider services, DateTimeOffset now, DateTimeOffset deliveriesBefore, CancellationToken ct)
    {
        var db = services.GetRequiredService<IHeliosDbContext>();
        var store = services.GetRequiredService<IObjectStore>();
        var audit = services.GetRequiredService<IAuditWriter>();

        var uploads = await db.Uploads.Where(u => u.DeletedAt == null && u.ExpiresAt <= now).Take(500).ToListAsync(ct);
        foreach (var upload in uploads)
        {
            if (upload.StorageRef is { } reference)
            {
                await store.DeleteAsync(reference, ct);
            }

            upload.StorageRef = null;
            upload.DeletedAt = now;
            audit.Record("upload.expire", "Upload", upload.Id.ToString(), organizationId: upload.OrganizationId);
        }

        await db.SaveChangesAsync(ct);

        var results = await db.ApiRequests
            .Where(r => r.ResultJson != null && r.ResultExpiresAt <= now)
            .Take(500)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ResultJson, (string?)null), ct);

        // Corrected values are copies of document data: they go when the result goes. The decision
        // itself (who, what, when, why) is kept for audit.
        var corrections = await db.ReviewDecisions
            .Where(d => d.CorrectionsJson != null &&
                        db.ApiRequests.Any(r => r.Id == d.ApiRequestId && r.ResultExpiresAt <= now))
            .Take(500)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.CorrectionsJson, (string?)null)
                .SetProperty(d => d.PurgedAt, now), ct);

        // Finished deliveries are operational history, not records the business must keep. Pending
        // ones are never touched: they are the outbox.
        var deliveries = await db.WebhookDeliveries
            .Where(d => d.Status != WebhookDeliveryStatus.Pending && d.CreatedAt <= deliveriesBefore)
            .Take(1000)
            .ExecuteDeleteAsync(ct);

        return uploads.Count + results + corrections + deliveries;
    }
}
