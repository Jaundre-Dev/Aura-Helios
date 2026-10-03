using Helios.Domain.Platform;
using Helios.Infrastructure.Persistence.MySql.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helios.Infrastructure.Persistence.MySql.Configurations;

public sealed class PlatformStaffConfiguration : IEntityTypeConfiguration<PlatformStaffMember>
{
    public void Configure(EntityTypeBuilder<PlatformStaffMember> builder)
    {
        builder.ToTable("platform_staff");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.TotpSecretEnvelope).HasMaxLength(256);
        builder.Property(s => s.TotpKeyId).HasMaxLength(64);

        // One staff record per account; removal deactivates it.
        builder.HasIndex(s => s.UserId).IsUnique();

        builder.HasOne<HeliosUser>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
