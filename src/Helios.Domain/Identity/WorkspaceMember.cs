using Helios.Contracts.Identity;
using Helios.Domain.Common;

namespace Helios.Domain.Identity;

/// <summary>
/// Grants a user access to a workspace. Absence of a row is denial — there is no
/// implicit access, which is what makes the global query filter safe to rely on.
/// </summary>
public class WorkspaceMember : AuditableEntity
{
    public Guid WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }

    /// <summary>References the identity user. Domain never depends on the Identity types.</summary>
    public Guid UserId { get; set; }

    public WorkspaceRole Role { get; set; } = WorkspaceRole.Viewer;
}
