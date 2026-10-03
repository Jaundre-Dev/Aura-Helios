using Helios.Application.Abstractions.Execution;
using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Abstractions.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.Application.Features.Retention;

/// <summary>
/// Enforces retention (plan section 13): deletes uploaded document content and stored result
/// payloads once they expire, keeping the metadata needed for billing and audit. Finding what is due
/// is the only cross-tenant step; each workspace's deletions run in a scope confined to it.
/// </summary>
public sealed class RetentionSweeper(ITenantScopeFactory scopes, TimeProvider clock)
{
    /// <returns>The number of uploads and results purged.</returns>
    public async Task<int> SweepAsync(int maxWorkspaces, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
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

            workspaces = await withUploads.Union(withResults).Union(withCorrections).Distinct().Take(maxWorkspaces).ToListAsync(ct);
        }

        var purged = 0;
        foreach (var workspaceId in workspaces)
        {
            using var tenant = scopes.CreateForWorkspace(workspaceId);
            purged += await SweepWorkspaceAsync(tenant.ServiceProvider, now, ct);
        }

        return purged;
    }

    private static async Task<int> SweepWorkspaceAsync(IServiceProvider services, DateTimeOffset now, CancellationToken ct)
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

        return uploads.Count + results + corrections;
    }
}
