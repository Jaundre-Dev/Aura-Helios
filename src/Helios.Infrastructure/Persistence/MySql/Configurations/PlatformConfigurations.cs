using Helios.Domain.ApiKeys;
using Helios.Domain.Billing;
using Helios.Domain.Catalogue;
using Helios.Domain.Identity;
using Helios.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helios.Infrastructure.Persistence.MySql.Configurations;

// Enums are stored as text throughout: reordering an enum must never silently change a
// product's release state, a key's environment or a request's billing state.

public sealed class ApiProductConfiguration : IEntityTypeConfiguration<ApiProduct>
{
    public void Configure(EntityTypeBuilder<ApiProduct> builder)
    {
        builder.ToTable("api_products");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Slug).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Category).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Summary).HasMaxLength(1000).IsRequired();
        builder.Property(p => p.Delivery).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.ReleaseState).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Sensitivity).HasConversion<string>().HasMaxLength(30);
        builder.Property(p => p.BillingUnit).HasMaxLength(200).IsRequired();
        builder.Property(p => p.CurrentVersion).HasMaxLength(20);
        builder.Property(p => p.Limitations).HasMaxLength(2000);

        builder.HasIndex(p => p.Slug).IsUnique();
    }
}

public sealed class ApiProductVersionConfiguration : IEntityTypeConfiguration<ApiProductVersion>
{
    public void Configure(EntityTypeBuilder<ApiProductVersion> builder)
    {
        builder.ToTable("api_product_versions");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.Version).HasMaxLength(20).IsRequired();
        builder.Property(v => v.ReleaseState).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.RequestSchemaJson).HasColumnType("json");
        builder.Property(v => v.ResponseSchemaJson).HasColumnType("json");
        builder.Property(v => v.RequestExample).HasColumnType("json");

        builder.HasIndex(v => new { v.ProductId, v.Version }).IsUnique();

        builder.HasOne<ApiProduct>()
            .WithMany()
            .HasForeignKey(v => v.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class EntitlementConfiguration : IEntityTypeConfiguration<Entitlement>
{
    public void Configure(EntityTypeBuilder<Entitlement> builder)
    {
        builder.ToTable("entitlements");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Environment).HasConversion<string>().HasMaxLength(10);
        builder.Property(e => e.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Purpose).HasMaxLength(1000);

        // One row per company, product and environment; state changes update it in place.
        builder.HasIndex(e => new { e.OrganizationId, e.ProductId, e.Environment }).IsUnique();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(e => e.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApiProduct>()
            .WithMany()
            .HasForeignKey(e => e.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys");
        builder.HasKey(k => k.Id);

        builder.Property(k => k.Name).HasMaxLength(200).IsRequired();
        builder.Property(k => k.PublicId).HasMaxLength(32).IsRequired();
        builder.Property(k => k.DisplayPrefix).HasMaxLength(40).IsRequired();

        // 32 bytes. Variable-width so the driver never mistakes it for a 16-byte GUID column.
        builder.Property(k => k.SecretHash).HasColumnType("varbinary(32)").IsRequired();

        builder.Property(k => k.Environment).HasConversion<string>().HasMaxLength(10);
        builder.Property(k => k.Scopes).HasMaxLength(2000).IsRequired();
        builder.Property(k => k.MonthlyBudget).HasPrecision(19, 6);

        builder.HasIndex(k => k.PublicId).IsUnique();
        builder.HasIndex(k => new { k.WorkspaceId, k.CreatedAt });
        builder.HasIndex(k => k.OrganizationId);

        builder.Ignore(k => k.ScopeList);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(k => k.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(k => k.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ApiRequestConfiguration : IEntityTypeConfiguration<ApiRequest>
{
    public void Configure(EntityTypeBuilder<ApiRequest> builder)
    {
        builder.ToTable("api_requests");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.ProductSlug).HasMaxLength(100).IsRequired();
        builder.Property(r => r.ProductVersion).HasMaxLength(20).IsRequired();
        builder.Property(r => r.Environment).HasConversion<string>().HasMaxLength(10);
        builder.Property(r => r.Channel).HasMaxLength(10).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.IdempotencyKey).HasMaxLength(255);
        builder.Property(r => r.FingerprintKeyId).HasMaxLength(64).IsRequired();
        builder.Property(r => r.PayloadFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(r => r.ResultJson).HasColumnType("json");
        builder.Property(r => r.WarningsJson).HasColumnType("json");
        builder.Property(r => r.ErrorCode).HasMaxLength(100);
        builder.Property(r => r.UsageUnit).HasMaxLength(50).IsRequired();
        builder.Property(r => r.UsageQuantity).HasPrecision(18, 6);
        builder.Property(r => r.BillingState).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.BillingAmount).HasPrecision(18, 6);
        builder.Property(r => r.ReservedAmount).HasPrecision(19, 6);

        builder.HasOne<Domain.Billing.PriceVersion>()
            .WithMany()
            .HasForeignKey(r => r.PriceVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Idempotency identity (plan section 9): tenant + environment + product/version + key.
        // MySQL unique indexes admit many NULLs, so requests without a key never collide.
        builder.HasIndex(r => new { r.OrganizationId, r.Environment, r.ProductSlug, r.ProductVersion, r.IdempotencyKey })
            .IsUnique();

        builder.HasIndex(r => new { r.WorkspaceId, r.CreatedAt });
        builder.HasIndex(r => r.ApiKeyId);

        // Monthly spend checks sum a company's, or one key's, commitments since the month began.
        builder.HasIndex(r => new { r.OrganizationId, r.CreatedAt });
        builder.HasIndex(r => new { r.ApiKeyId, r.CreatedAt });

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(r => r.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(r => r.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApiProduct>()
            .WithMany()
            .HasForeignKey(r => r.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class BillingProfileConfiguration : IEntityTypeConfiguration<BillingProfile>
{
    public void Configure(EntityTypeBuilder<BillingProfile> builder)
    {
        builder.ToTable("billing_profiles");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.LegalName).HasMaxLength(200).IsRequired();
        builder.Property(b => b.BillingEmail).HasMaxLength(256).IsRequired();
        builder.Property(b => b.AddressLine1).HasMaxLength(200).IsRequired();
        builder.Property(b => b.AddressLine2).HasMaxLength(200);
        builder.Property(b => b.City).HasMaxLength(100).IsRequired();
        builder.Property(b => b.Province).HasMaxLength(100);
        builder.Property(b => b.PostalCode).HasMaxLength(20).IsRequired();
        builder.Property(b => b.CountryCode).HasMaxLength(2).IsRequired();
        builder.Property(b => b.RegistrationNumber).HasMaxLength(50);
        builder.Property(b => b.VatNumber).HasMaxLength(20);
        builder.Property(b => b.Currency).HasMaxLength(3).IsRequired();

        builder.HasIndex(b => b.OrganizationId).IsUnique();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(b => b.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
