using Helios.Domain.Identity;
using Helios.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Abstractions.Persistence;

/// <summary>
/// The data seam. Application handlers query through this rather than through a
/// per-aggregate repository — WP0.2 decided a generic repository over EF Core adds a
/// layer and removes capability. Only Phase 0 sets are exposed; later phases add theirs.
/// </summary>
/// <remarks>
/// This puts EF Core abstractions in the Application layer, which is deliberate and does
/// not weaken the architecture rule that matters: no <em>model provider</em> SDK reaches
/// Domain or Application. That rule is about vendor lock-in on inference, not on the ORM.
/// </remarks>
public interface IHeliosDbContext
{
    DbSet<Organization> Organizations { get; }
    DbSet<Workspace> Workspaces { get; }
    DbSet<WorkspaceMember> WorkspaceMembers { get; }
    DbSet<Project> Projects { get; }
    DbSet<ProjectMember> ProjectMembers { get; }
    DbSet<AuditLog> AuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
