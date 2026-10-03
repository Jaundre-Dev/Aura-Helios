using System.Globalization;
using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Common;
using Helios.Contracts.Billing;
using Helios.Contracts.Catalogue;
using Helios.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.Billing;

/// <summary>
/// Versioned Rand prices. Versions are append-only: publishing one closes the previous version at
/// the new version's start, and nothing ever edits a version a request may have referenced. Prices
/// are a platform decision — no customer-facing endpoint creates them, and none are seeded.
/// </summary>
public sealed class PriceService(IHeliosDbContext db, IAuditWriter audit, TimeProvider clock)
{
    public static readonly string[] TaxTreatments = ["vat_exclusive_standard", "vat_inclusive_standard", "zero_rated", "exempt"];

    public async Task<PriceVersion?> FindActiveAsync(Guid productId, ApiEnvironment environment, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        return await db.PriceVersions.AsNoTracking()
            .Where(p => p.ProductId == productId && p.Environment == environment &&
                        p.EffectiveFrom <= now && (p.EffectiveTo == null || p.EffectiveTo > now))
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<PriceVersionResponse> PublishAsync(
        string productSlug,
        ApiEnvironment environment,
        string unit,
        decimal unitPrice,
        decimal minimumCharge,
        string taxTreatment,
        DateTimeOffset effectiveFrom,
        Guid? actor,
        CancellationToken ct)
    {
        if (unitPrice < 0 || minimumCharge < 0)
        {
            throw new ConflictException("Prices cannot be negative.", "invalid_price");
        }

        if (decimal.Round(unitPrice, PriceVersion.Precision) != unitPrice ||
            decimal.Round(minimumCharge, PriceVersion.Precision) != minimumCharge)
        {
            throw new ConflictException($"Prices carry at most {PriceVersion.Precision} decimal places.", "invalid_price");
        }

        if (!TaxTreatments.Contains(taxTreatment))
        {
            throw new ConflictException($"Unknown tax treatment '{taxTreatment}'.", "invalid_tax_treatment");
        }

        var product = await db.ApiProducts.SingleOrDefaultAsync(p => p.Slug == productSlug, ct)
            ?? throw new NotFoundException("Product", productSlug);

        var latest = await db.PriceVersions
            .Where(p => p.ProductId == product.Id && p.Environment == environment)
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

        if (latest is not null && effectiveFrom <= latest.EffectiveFrom)
        {
            throw new ConflictException("A new price must take effect after the current latest version.", "price_not_after_latest");
        }

        if (latest is not null && (latest.EffectiveTo is null || latest.EffectiveTo > effectiveFrom))
        {
            latest.EffectiveTo = effectiveFrom;
        }

        var version = new PriceVersion
        {
            ProductId = product.Id,
            Environment = environment,
            Currency = LedgerService.Currency,
            Unit = unit,
            UnitPrice = unitPrice,
            MinimumCharge = minimumCharge,
            TaxTreatment = taxTreatment,
            EffectiveFrom = effectiveFrom,
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = actor
        };

        db.PriceVersions.Add(version);

        audit.Record("price.publish", nameof(PriceVersion), version.Id.ToString(),
            metadataJson: string.Create(CultureInfo.InvariantCulture,
                $$"""{"product":"{{product.Slug}}","environment":"{{environment}}","unitPrice":{{unitPrice}},"minimumCharge":{{minimumCharge}},"effectiveFrom":"{{effectiveFrom:O}}"}"""));

        await db.SaveChangesAsync(ct);

        return new PriceVersionResponse(version.Id, product.Slug, environment, version.Currency, unit, unitPrice,
            minimumCharge, taxTreatment, effectiveFrom, null);
    }
}
