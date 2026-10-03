using Helios.Contracts.Organizations;
using Helios.Domain.Common;

namespace Helios.Domain.Identity;

/// <summary>
/// Grants a user a role in a customer company. Absence of an active row is denial: listing,
/// reading and creating workspaces under an organisation all require one. Removal deactivates
/// the row rather than deleting it, so the audit trail can still name who held access.
/// </summary>
public class OrganizationMember : AuditableEntity
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    /// <summary>References the identity user. Domain never depends on the Identity types.</summary>
    public Guid UserId { get; set; }

    public OrganizationRole Role { get; set; } = OrganizationRole.Operator;

    public bool IsActive { get; set; } = true;
}
