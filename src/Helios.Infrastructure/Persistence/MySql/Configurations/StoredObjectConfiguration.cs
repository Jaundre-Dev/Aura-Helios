using Helios.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helios.Infrastructure.Persistence.MySql.Configurations;

public sealed class StoredObjectConfiguration : IEntityTypeConfiguration<StoredObject>
{
    public void Configure(EntityTypeBuilder<StoredObject> builder)
    {
        builder.ToTable("stored_objects");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Name).HasMaxLength(400).IsRequired();
        builder.Property(o => o.ContentType).HasMaxLength(200).IsRequired();

        // The bytes. LONGBLOB rather than a fixed width, so no column is ever the 16-byte size the
        // MySQL driver reads back as a GUID, and an artifact of any bounded size fits.
        builder.Property(o => o.Content).HasColumnType("longblob").IsRequired();

        // Reads are by primary key within a workspace; the index keeps a workspace listing cheap.
        builder.HasIndex(o => o.WorkspaceId);
    }
}
