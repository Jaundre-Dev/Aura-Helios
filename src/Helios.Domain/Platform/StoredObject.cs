using Helios.Domain.Common;

namespace Helios.Domain.Platform;

/// <summary>
/// A stored object — the bytes behind an <see cref="Artifact.StorageRef"/> or an upload. Isolated
/// by <see cref="WorkspaceId"/> through the same global query filter as every other tenant-scoped
/// aggregate, so a reference is meaningless outside the workspace that created it. The content lives
/// in the row for now; the abstraction over it (<c>IObjectStore</c>) lets the backend become a
/// filesystem or object store later without the callers or the reference changing.
/// </summary>
public class StoredObject : AuditableEntity, IAggregateRoot
{
    public Guid WorkspaceId { get; set; }

    public required string Name { get; set; }
    public required string ContentType { get; set; }
    public required byte[] Content { get; set; }
    public long SizeBytes { get; set; }
}
