using Helios.Domain.Identity;
using Helios.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helios.Infrastructure.Persistence.MySql.Configurations;

public sealed class CreditAdjustmentApprovalConfiguration : IEntityTypeConfiguration<CreditAdjustmentApproval>
{
    public void Configure(EntityTypeBuilder<CreditAdjustmentApproval> builder)
    {
        builder.ToTable("credit_adjustment_approvals");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Amount).HasPrecision(19, 6);
        builder.Property(a => a.Reason).HasMaxLength(500).IsRequired();
        builder.Property(a => a.Reference).HasMaxLength(100).IsRequired();
        builder.Property(a => a.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.DecisionReason).HasMaxLength(500);

        // A reference identifies one adjustment per company, whether posted directly or approved.
        builder.HasIndex(a => new { a.OrganizationId, a.Reference }).IsUnique();
        builder.HasIndex(a => new { a.State, a.RequestedAt });

        builder.HasOne<Organization>().WithMany().HasForeignKey(a => a.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}
