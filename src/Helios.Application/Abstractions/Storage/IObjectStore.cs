namespace Helios.Application.Abstractions.Storage;

/// <summary>
/// Durable storage for run outputs and uploads — a report, a diff, a scan result, a fetched file.
/// Every object belongs to a workspace and is reached only through the current workspace context,
/// so a reference minted in one tenant resolves to nothing in another. The backend (a database blob
/// today, a filesystem or object store later) lives behind this seam; callers hold only the
/// opaque reference the store hands back.
/// </summary>
public interface IObjectStore
{
    /// <summary>Stores an object and returns the opaque, workspace-scoped reference to read it back by.</summary>
    Task<string> PutAsync(ObjectToStore item, CancellationToken cancellationToken);

    /// <summary>Opens an object by its reference, or null when it does not exist in this workspace.</summary>
    Task<StoredObjectContent?> GetAsync(string storageRef, CancellationToken cancellationToken);

    /// <summary>Removes an object. Returns false when the reference is unknown in this workspace.</summary>
    Task<bool> DeleteAsync(string storageRef, CancellationToken cancellationToken);
}

/// <summary>An object to persist. Content is bounded — the store enforces a maximum size.</summary>
public sealed record ObjectToStore(string Name, string ContentType, byte[] Content);

/// <summary>An object read back from the store, with its metadata and bytes.</summary>
public sealed record StoredObjectContent(
    string Ref,
    string Name,
    string ContentType,
    long SizeBytes,
    byte[] Content);
