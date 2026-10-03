using Helios.Application.Abstractions.Persistence;
using Helios.Application.Features.Products;
using Helios.Contracts.Catalogue;
using Helios.Domain.Catalogue;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.Catalogue;

/// <summary>
/// The public product catalogue. "Callable" is computed, not stored: a product is callable in an
/// environment only when its release state allows it and this process has an executor for its
/// current version. Planned products are listed with that state and no execution path.
/// </summary>
public sealed class CatalogueService(IHeliosDbContext db, ProductExecutorRegistry executors)
{
    public async Task<IReadOnlyList<ProductSummaryResponse>> ListAsync(CancellationToken ct)
    {
        var products = await db.ApiProducts
            .AsNoTracking()
            .OrderBy(p => p.Category)
            .ThenBy(p => p.Name)
            .ToListAsync(ct);

        return products.Select(ToSummary).ToList();
    }

    public async Task<ProductDetailResponse?> GetAsync(string slug, CancellationToken ct)
    {
        var product = await db.ApiProducts.AsNoTracking().SingleOrDefaultAsync(p => p.Slug == slug, ct);
        if (product is null)
        {
            return null;
        }

        var versions = await db.ApiProductVersions
            .AsNoTracking()
            .Where(v => v.ProductId == product.Id)
            .OrderByDescending(v => v.PublishedAt)
            .Select(v => new ProductVersionResponse(
                v.Version, v.ReleaseState, v.MaxInputBytes, v.RequestSchemaJson, v.ResponseSchemaJson,
                v.RequestExample, v.PublishedAt))
            .ToListAsync(ct);

        return new ProductDetailResponse(ToSummary(product), versions);
    }

    internal bool IsCallable(ApiProduct product, ApiEnvironment environment) =>
        product.IsCallableIn(environment) && executors.Has(product.Slug, product.CurrentVersion);

    private ProductSummaryResponse ToSummary(ApiProduct p) =>
        new(p.Slug, p.Name, p.Category, p.Summary, p.Delivery, p.ReleaseState, p.Sensitivity, p.BillingUnit,
            p.CurrentVersion,
            IsCallable(p, ApiEnvironment.Sandbox),
            IsCallable(p, ApiEnvironment.Live),
            p.Limitations);
}
