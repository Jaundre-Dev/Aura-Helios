using Helios.Contracts.Common;
using Helios.Domain.Common;

namespace Helios.Domain.Identity;

/// <summary>The unit agents work against. Classification here drives routing.</summary>
public class Project : AuditableEntity, IAggregateRoot
{
    public Guid WorkspaceId { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? Description { get; set; }
    public DataClassification Classification { get; set; } = DataClassification.Internal;
    public bool IsActive { get; set; } = true;
}
