using Helios.Application.Abstractions.Security;
using Helios.Application.Abstractions.Storage;
using Helios.Domain.Platform;
using Helios.Infrastructure.Persistence.MySql;
using Microsoft.EntityFrameworkCore;

namespace Helios.Infrastructure.Storage;

/// <summary>
/// The tenant-scoped object store. Objects belong to a workspace and are reached only through the
/// current <see cref="IWorkspaceContext"/> and the same global query filter as every other
/// aggregate, so a reference minted in one workspace resolves to nothing in another and a system
/// caller with no workspace is refused rather than guessing which tenant a bare reference belongs
/// to. The reference handed back is opaque — the object's id — so a caller cannot forge a path into
/// another tenant's storage.
///
/// Content lives in the database row for now, behind <see cref="IObjectStore"/>; the backend can
/// become a filesystem or object store later without the reference or the callers changing.
/// </summary>
public sealed class MySqlObjectStore(
    HeliosDbContext db,
    IWorkspaceContext workspaceContext,
    IAuditWriter audit) : IObjectStore
{
    /// <summary>
    /// The largest object the store accepts. Bounded because the content is materialised in memory
    /// and, for now, held in a row; larger inputs belong to a streaming backend, not this one.
    /// </summary>
    public const long MaxSizeBytes = 16 * 1024 * 1024;

    public async Task<string> PutAsync(ObjectToStore item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.ContentType);
        ArgumentNullException.ThrowIfNull(item.Content);
        var workspaceId = RequireWorkspace();

        if (item.Content.Length == 0)
        {
            throw new ArgumentException("An empty object cannot be stored.", nameof(item));
        }

        if (item.Content.Length > MaxSizeBytes)
        {
            throw new ArgumentException(
                $"The object is {item.Content.Length} bytes; the maximum is {MaxSizeBytes}.", nameof(item));
        }

        var stored = new StoredObject
        {
            WorkspaceId = workspaceId,
            Name = item.Name,
            ContentType = item.ContentType,
            Content = item.Content,
            SizeBytes = item.Content.Length
        };

        db.StoredObjects.Add(stored);

        audit.Record(
            action: "object.put",
            resourceType: nameof(StoredObject),
            resourceId: stored.Id.ToString(),
            metadataJson: $"{{\"name\":{Json(item.Name)},\"contentType\":{Json(item.ContentType)},\"sizeBytes\":{item.Content.Length}}}");

        await db.SaveChangesAsync(cancellationToken);
        return stored.Id.ToString();
    }

    public async Task<StoredObjectContent?> GetAsync(string storageRef, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageRef);
        RequireWorkspace();

        if (!Guid.TryParse(storageRef, out var id))
        {
            return null;
        }

        var stored = await db.StoredObjects
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        return stored is null
            ? null
            : new StoredObjectContent(
                stored.Id.ToString(), stored.Name, stored.ContentType, stored.SizeBytes, stored.Content);
    }

    public async Task<bool> DeleteAsync(string storageRef, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageRef);
        RequireWorkspace();

        if (!Guid.TryParse(storageRef, out var id))
        {
            return false;
        }

        var stored = await db.StoredObjects.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (stored is null)
        {
            return false;
        }

        db.StoredObjects.Remove(stored);
        audit.Record(action: "object.delete", resourceType: nameof(StoredObject), resourceId: storageRef);

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private Guid RequireWorkspace()
    {
        if (workspaceContext.IsSystem || workspaceContext.WorkspaceId is not { } workspaceId)
        {
            throw new InvalidOperationException(
                "Stored objects are workspace-scoped; there is no workspace on the current context to resolve one against.");
        }

        return workspaceId;
    }

    private static string Json(string value) => System.Text.Json.JsonSerializer.Serialize(value);
}
