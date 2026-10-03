using Helios.Domain.Billing;
using Helios.Domain.Catalogue;
using Helios.Domain.Execution;
using Helios.Domain.Identity;
using Helios.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helios.Infrastructure.Persistence.MySql.Configurations;

// Money columns are decimal(19,6): exact, sub-cent precision, never floating point.

public sealed class LedgerAccountConfiguration : IEntityTypeConfiguration<LedgerAccount>
{
    public void Configure(EntityTypeBuilder<LedgerAccount> builder)
    {
        builder.ToTable("ledger_accounts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.Currency).HasMaxLength(3).IsRequired();
        builder.Property(a => a.Balance).HasPrecision(19, 6);

        // One account per owner, type and currency. No FK on the owner: platform accounts use an
        // all-zero owner id that is not an organisation.
        builder.HasIndex(a => new { a.OrganizationId, a.Type, a.Currency }).IsUnique();
    }
}

public sealed class LedgerTransactionConfiguration : IEntityTypeConfiguration<LedgerTransaction>
{
    public void Configure(EntityTypeBuilder<LedgerTransaction> builder)
    {
        builder.ToTable("ledger_transactions");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.PostingKey).HasMaxLength(150).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(500).IsRequired();
        builder.Property(t => t.Currency).HasMaxLength(3).IsRequired();

        // The exactly-once guarantee for every posting.
        builder.HasIndex(t => t.PostingKey).IsUnique();
        builder.HasIndex(t => new { t.OrganizationId, t.CreatedAt });
        builder.HasIndex(t => t.ApiRequestId);
        builder.HasIndex(t => t.PaymentId);

        builder.HasMany(t => t.Entries)
            .WithOne()
            .HasForeignKey(e => e.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("ledger_entries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Amount).HasPrecision(19, 6);
        builder.HasIndex(e => e.AccountId);

        builder.HasOne<LedgerAccount>()
            .WithMany()
            .HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("reservations");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Amount).HasPrecision(19, 6);
        builder.Property(r => r.SettledAmount).HasPrecision(19, 6);
        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.State).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(r => r.ApiRequestId).IsUnique();
        builder.HasIndex(r => new { r.OrganizationId, r.State });

        builder.HasOne<ApiRequest>()
            .WithMany()
            .HasForeignKey(r => r.ApiRequestId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PriceVersionConfiguration : IEntityTypeConfiguration<PriceVersion>
{
    public void Configure(EntityTypeBuilder<PriceVersion> builder)
    {
        builder.ToTable("price_versions");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Environment).HasConversion<string>().HasMaxLength(10);
        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.Property(p => p.Unit).HasMaxLength(50).IsRequired();
        builder.Property(p => p.UnitPrice).HasPrecision(19, 6);
        builder.Property(p => p.MinimumCharge).HasPrecision(19, 6);
        builder.Property(p => p.TaxTreatment).HasMaxLength(50).IsRequired();

        builder.HasIndex(p => new { p.ProductId, p.Environment, p.EffectiveFrom }).IsUnique();

        builder.HasOne<ApiProduct>()
            .WithMany()
            .HasForeignKey(p => p.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class UsageEventConfiguration : IEntityTypeConfiguration<UsageEvent>
{
    public void Configure(EntityTypeBuilder<UsageEvent> builder)
    {
        builder.ToTable("usage_events");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.ProductSlug).HasMaxLength(100).IsRequired();
        builder.Property(u => u.ProductVersion).HasMaxLength(20).IsRequired();
        builder.Property(u => u.Environment).HasConversion<string>().HasMaxLength(10);
        builder.Property(u => u.Unit).HasMaxLength(50).IsRequired();
        builder.Property(u => u.Quantity).HasPrecision(19, 6);
        builder.Property(u => u.Amount).HasPrecision(19, 6);
        builder.Property(u => u.Currency).HasMaxLength(3).IsRequired();

        // Unique settlement identity: one usage record per request, whatever retries happen.
        builder.HasIndex(u => u.ApiRequestId).IsUnique();
        builder.HasIndex(u => new { u.OrganizationId, u.OccurredAt });

        builder.HasOne<ApiRequest>()
            .WithMany()
            .HasForeignKey(u => u.ApiRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PriceVersion>()
            .WithMany()
            .HasForeignKey(u => u.PriceVersionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("jobs");
        builder.HasKey(j => j.Id);

        builder.Property(j => j.ProductSlug).HasMaxLength(100).IsRequired();
        builder.Property(j => j.ProductVersion).HasMaxLength(20).IsRequired();
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(j => j.Phase).HasConversion<string>().HasMaxLength(20);
        builder.Property(j => j.LeaseOwner).HasMaxLength(200);
        builder.Property(j => j.InputEnvelope).HasColumnType("mediumblob");
        builder.Property(j => j.InputKeyId).HasMaxLength(64);
        builder.Property(j => j.ProviderReference).HasMaxLength(200);
        builder.Property(j => j.LastError).HasMaxLength(1000);
        builder.Property(j => j.FencingToken).IsConcurrencyToken();

        builder.Ignore(j => j.IsTerminal);

        builder.HasIndex(j => j.ApiRequestId).IsUnique();

        // The claim query's access path.
        builder.HasIndex(j => new { j.Status, j.AvailableAt });
        builder.HasIndex(j => new { j.Status, j.LeaseExpiresAt });

        builder.HasOne<ApiRequest>()
            .WithMany()
            .HasForeignKey(j => j.ApiRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(j => j.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
