using Helios.Domain.Common;

namespace Helios.Domain.Identity;

/// <summary>
/// Evidence that a company accepted a version of a legal document (customer terms, the processing
/// agreement): who accepted, when and from where. Append-only; a new version needs a new acceptance.
/// </summary>
public class AgreementAcceptance : Entity
{
    public Guid OrganizationId { get; set; }
    public required string Document { get; set; }
    public required string Version { get; set; }
    public Guid AcceptedBy { get; set; }
    public DateTimeOffset AcceptedAt { get; set; }
    public string? IpAddress { get; set; }
}
