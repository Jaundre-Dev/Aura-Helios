using Helios.Domain.Identity;
using Helios.Domain.Uploads;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helios.Infrastructure.Persistence.MySql.Configurations;

public sealed class UploadConfiguration : IEntityTypeConfiguration<Upload>
{
    public void Configure(EntityTypeBuilder<Upload> builder)
    {
        builder.ToTable("uploads");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Environment).HasConversion<string>().HasMaxLength(10);
        builder.Property(u => u.StorageRef).HasMaxLength(64);
        builder.Property(u => u.FileName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.MediaType).HasMaxLength(100).IsRequired();
        builder.Property(u => u.ScanState).HasConversion<string>().HasMaxLength(20);
        builder.Property(u => u.Scanner).HasMaxLength(50);
        builder.Property(u => u.Sha256).HasMaxLength(64).IsRequired();

        builder.HasIndex(u => new { u.WorkspaceId, u.CreatedAt });

        // The retention sweep's access path.
        builder.HasIndex(u => new { u.DeletedAt, u.ExpiresAt });

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(u => u.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
