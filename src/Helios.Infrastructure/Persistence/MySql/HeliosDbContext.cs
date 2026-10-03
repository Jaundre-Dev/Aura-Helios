using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Domain.ApiKeys;
using Helios.Domain.Billing;
using Helios.Domain.Catalogue;
using Helios.Domain.Identity;
using Helios.Domain.Platform;
using Helios.Domain.Requests;
using Helios.Infrastructure.Persistence.MySql.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Helios.Infrastructure.Persistence.MySql;

/// <summary>
/// MySQL is the durable source of truth. Existing identity, audit and storage tables
/// are retained; API products, requests and billing arrive through additive migrations.
/// </summary>
public class HeliosDbContext(
    DbContextOptions<HeliosDbContext> options,
    IWorkspaceContext workspaceContext)
    : IdentityDbContext<HeliosUser, HeliosRole, Guid>(options), IHeliosDbContext
{
    private readonly IWorkspaceContext _workspaceContext = workspaceContext;

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Secret> Secrets => Set<Secret>();
    public DbSet<StoredObject> StoredObjects => Set<StoredObject>();
    public DbSet<ApiProduct> ApiProducts => Set<ApiProduct>();
    public DbSet<ApiProductVersion> ApiProductVersions => Set<ApiProductVersion>();
    public DbSet<Entitlement> Entitlements => Set<Entitlement>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<ApiRequest> ApiRequests => Set<ApiRequest>();
    public DbSet<BillingProfile> BillingProfiles => Set<BillingProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HeliosDbContext).Assembly);

        RenameIdentityTables(modelBuilder);
        ApplyWorkspaceIsolation(modelBuilder);

        // Last, so nothing configured above escapes the naming convention.
        SnakeCaseNaming.Apply(modelBuilder);
    }

    /// <summary>
    /// ASP.NET Core Identity defaults to AspNetUsers and friends. Rename them so the
    /// schema reads as one system rather than two bolted together.
    /// </summary>
    private static void RenameIdentityTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HeliosUser>().ToTable("users");
        modelBuilder.Entity<HeliosRole>().ToTable("roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
    }

    /// <summary>
    /// Isolation by default. A query that forgets to filter by workspace still cannot
    /// see another workspace's rows, because the filter is on the model rather than on
    /// the developer's memory.
    /// </summary>
    private void ApplyWorkspaceIsolation(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Workspace>()
            .HasQueryFilter(w => _workspaceContext.IsSystem || w.Id == _workspaceContext.WorkspaceId);

        modelBuilder.Entity<WorkspaceMember>()
            .HasQueryFilter(m => _workspaceContext.IsSystem || m.WorkspaceId == _workspaceContext.WorkspaceId);

        modelBuilder.Entity<Project>()
            .HasQueryFilter(p => _workspaceContext.IsSystem || p.WorkspaceId == _workspaceContext.WorkspaceId);

        modelBuilder.Entity<ProjectMember>()
            .HasQueryFilter(m => _workspaceContext.IsSystem || m.WorkspaceId == _workspaceContext.WorkspaceId);

        // System-scoped audit rows carry no workspace and stay invisible to workspace users.
        modelBuilder.Entity<AuditLog>()
            .HasQueryFilter(a => _workspaceContext.IsSystem || a.WorkspaceId == _workspaceContext.WorkspaceId);

        // A secret is reachable only from its own workspace. The store also refuses a system caller,
        // so this filter is the second lock rather than the only one.
        modelBuilder.Entity<Secret>()
            .HasQueryFilter(s => _workspaceContext.IsSystem || s.WorkspaceId == _workspaceContext.WorkspaceId);

        // A stored object is reachable only from its own workspace, so a reference minted in one
        // tenant resolves to nothing in another.
        modelBuilder.Entity<StoredObject>()
            .HasQueryFilter(o => _workspaceContext.IsSystem || o.WorkspaceId == _workspaceContext.WorkspaceId);

        // Keys and requests belong to a workspace. Key authentication must find a key before any
        // workspace is known, so it alone reads keys with IgnoreQueryFilters, by public id.
        modelBuilder.Entity<ApiKey>()
            .HasQueryFilter(k => _workspaceContext.IsSystem || k.WorkspaceId == _workspaceContext.WorkspaceId);

        modelBuilder.Entity<ApiRequest>()
            .HasQueryFilter(r => _workspaceContext.IsSystem || r.WorkspaceId == _workspaceContext.WorkspaceId);
    }
}
