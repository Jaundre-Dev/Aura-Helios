using Helios.Domain.Identity;
using Helios.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helios.Infrastructure.Persistence.MySql.Configurations;

public sealed class WebhookEndpointConfiguration : IEntityTypeConfiguration<WebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<WebhookEndpoint> builder)
    {
        builder.ToTable("webhook_endpoints");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Url).HasMaxLength(2000).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.Events).HasMaxLength(500).IsRequired();

        // nonce + ciphertext + tag; variable width so it is never read back as a GUID.
        builder.Property(e => e.SecretEnvelope).HasColumnType("varbinary(256)").IsRequired();
        builder.Property(e => e.SecretKeyId).HasMaxLength(64).IsRequired();

        builder.Ignore(e => e.EventList);
        builder.HasIndex(e => new { e.WorkspaceId, e.IsActive });

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(e => e.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.ToTable("webhook_deliveries");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.EventId).HasMaxLength(100).IsRequired();
        builder.Property(d => d.EventType).HasMaxLength(50).IsRequired();
        builder.Property(d => d.PayloadJson).HasColumnType("json").IsRequired();
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.LastError).HasMaxLength(1000);

        // Outbox idempotency: one delivery per endpoint per event.
        builder.HasIndex(d => new { d.EndpointId, d.EventId }).IsUnique();
        builder.HasIndex(d => new { d.Status, d.NextAttemptAt });

        builder.HasOne<WebhookEndpoint>()
            .WithMany()
            .HasForeignKey(d => d.EndpointId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
