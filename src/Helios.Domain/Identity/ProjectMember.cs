using Helios.Contracts.Identity;
using Helios.Domain.Common;

namespace Helios.Domain.Identity;

/// <summary>
/// Optional narrowing. A workspace member reaches every project by default; a row here
/// raises that user's role for one project, it never lowers it below workspace level.
/// </summary>
public class ProjectMember : AuditableEntity
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }

    /// <summary>
    /// Denormalised from the parent project so the isolation filter is a direct column
    /// comparison. Filtering through a navigation would make this row reachable whenever
    /// the join is written by hand, which is exactly the hole the filter exists to close.
    /// </summary>
    public Guid WorkspaceId { get; set; }

    public Guid UserId { get; set; }

    public WorkspaceRole Role { get; set; } = WorkspaceRole.Engineer;
}
