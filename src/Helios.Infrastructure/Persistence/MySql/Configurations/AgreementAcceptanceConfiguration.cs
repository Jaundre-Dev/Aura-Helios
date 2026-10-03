using Helios.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helios.Infrastructure.Persistence.MySql.Configurations;

public sealed class AgreementAcceptanceConfiguration : IEntityTypeConfiguration<AgreementAcceptance>
{
    public void Configure(EntityTypeBuilder<AgreementAcceptance> builder)
    {
        builder.ToTable("agreement_acceptances");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Document).HasMaxLength(50).IsRequired();
        builder.Property(a => a.Version).HasMaxLength(50).IsRequired();
        builder.Property(a => a.IpAddress).HasMaxLength(64);

        // One acceptance per company, document and version.
        builder.HasIndex(a => new { a.OrganizationId, a.Document, a.Version }).IsUnique();

        builder.HasOne<Organization>().WithMany().HasForeignKey(a => a.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}
