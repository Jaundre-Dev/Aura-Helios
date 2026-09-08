using Helios.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helios.Infrastructure.Persistence.MySql.Configurations;

/// <summary>
/// Append-only. Nothing in the codebase updates or deletes an audit row, and this table
/// is expected to become one of the two largest — hence the deliberate index set.
/// </summary>
public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Action).HasMaxLength(100).IsRequired();
        builder.Property(a => a.ResourceType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.ResourceId).HasMaxLength(100);
        builder.Property(a => a.DenyReason).HasMaxLength(500);
        builder.Property(a => a.IpAddress).HasMaxLength(45);
        builder.Property(a => a.Metadata).HasColumnType("json");

        builder.HasIndex(a => a.OccurredAt);
        builder.HasIndex(a => new { a.WorkspaceId, a.OccurredAt });
        builder.HasIndex(a => a.AgentRunId);

        // Denied decisions are what a security review actually reads.
        builder.HasIndex(a => new { a.Allowed, a.OccurredAt });
    }
}
