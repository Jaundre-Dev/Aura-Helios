using Helios.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helios.Infrastructure.Persistence.MySql.Configurations;

public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Name).HasMaxLength(200).IsRequired();
        builder.Property(o => o.Slug).HasMaxLength(100).IsRequired();

        builder.HasIndex(o => o.Slug).IsUnique();

        builder.HasMany(o => o.Workspaces)
            .WithOne()
            .HasForeignKey(w => w.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.ToTable("workspaces");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Name).HasMaxLength(200).IsRequired();
        builder.Property(w => w.Slug).HasMaxLength(100).IsRequired();
        builder.Property(w => w.Description).HasMaxLength(1000);

        builder.HasIndex(w => new { w.OrganizationId, w.Slug }).IsUnique();
    }
}

public sealed class WorkspaceMemberConfiguration : IEntityTypeConfiguration<WorkspaceMember>
{
    public void Configure(EntityTypeBuilder<WorkspaceMember> builder)
    {
        builder.ToTable("workspace_members");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(20);

        // One membership per user per workspace. The database enforces it, not the handler.
        builder.HasIndex(m => new { m.WorkspaceId, m.UserId }).IsUnique();
        builder.HasIndex(m => m.UserId);

        builder.HasOne(m => m.Workspace)
            .WithMany()
            .HasForeignKey(m => m.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Slug).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(2000);

        // Stored as text: a reordered enum must never silently reclassify existing data,
        // and RESTRICTED is the value that decides whether a prompt may leave the machine.
        builder.Property(p => p.Classification).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(p => new { p.WorkspaceId, p.Slug }).IsUnique();
    }
}

public sealed class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> builder)
    {
        builder.ToTable("project_members");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(m => new { m.ProjectId, m.UserId }).IsUnique();
        builder.HasIndex(m => m.UserId);
        builder.HasIndex(m => m.WorkspaceId);

        builder.HasOne(m => m.Project)
            .WithMany()
            .HasForeignKey(m => m.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
