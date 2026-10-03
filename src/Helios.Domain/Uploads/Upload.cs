using Helios.Contracts.Catalogue;
using Helios.Contracts.Uploads;
using Helios.Domain.Common;

namespace Helios.Domain.Uploads;

/// <summary>
/// A customer document accepted for processing. Only files that passed type, structure and (where
/// configured) malware checks become uploads; rejected files are never stored. Content lives in the
/// tenant-scoped object store and is deleted at <see cref="ExpiresAt"/> or on request, while this
/// metadata row remains for audit and billing.
/// </summary>
public class Upload : Entity, IAggregateRoot
{
    public Guid OrganizationId { get; set; }
    public Guid WorkspaceId { get; set; }
    public ApiEnvironment Environment { get; set; }

    /// <summary>Opaque object-store reference; null once the content has been deleted.</summary>
    public string? StorageRef { get; set; }

    /// <summary>A sanitised display name. Never used as a path.</summary>
    public required string FileName { get; set; }

    /// <summary>The type established from the file's own bytes, not from the client's claim.</summary>
    public required string MediaType { get; set; }

    public long SizeBytes { get; set; }
    public int PageCount { get; set; }

    /// <summary>True when every page carries a text layer (a "digital" PDF rather than a scan).</summary>
    public bool HasTextLayer { get; set; }

    public ScanState ScanState { get; set; }
    public string? Scanner { get; set; }

    public required string Sha256 { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public Guid? CreatedByApiKeyId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public bool IsAvailableAt(DateTimeOffset now) => DeletedAt is null && StorageRef is not null && ExpiresAt > now;
}
