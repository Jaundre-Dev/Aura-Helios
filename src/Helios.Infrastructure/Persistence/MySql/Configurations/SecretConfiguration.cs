using Helios.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helios.Infrastructure.Persistence.MySql.Configurations;

public sealed class SecretConfiguration : IEntityTypeConfiguration<Secret>
{
    public void Configure(EntityTypeBuilder<Secret> builder)
    {
        builder.ToTable("secrets");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Reference).HasMaxLength(200).IsRequired();
        builder.Property(s => s.KeyId).HasMaxLength(64).IsRequired();

        // nonce(12) + ciphertext + tag(16) in one blob. 1052 covers a 1024-byte credential; a
        // single column also keeps any field from being the fixed 16-byte width the MySQL driver
        // would read back as a GUID.
        builder.Property(s => s.Envelope).HasColumnType("varbinary(1052)").IsRequired();

        // One value per reference per workspace; the database enforces it, not the store.
        builder.HasIndex(s => new { s.WorkspaceId, s.Reference }).IsUnique();
    }
}
