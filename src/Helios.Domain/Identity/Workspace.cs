using Helios.Domain.Common;

namespace Helios.Domain.Identity;

/// <summary>Isolation boundary for projects, agents, policies, credentials and audit.</summary>
public class Workspace : AuditableEntity, IAggregateRoot
{
    public Guid OrganizationId { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}
