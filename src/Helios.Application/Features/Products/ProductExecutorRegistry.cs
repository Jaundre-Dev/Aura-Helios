namespace Helios.Application.Features.Products;

/// <summary>
/// Every executor the process can run, keyed by product slug and version. A product whose release
/// state says it is callable but which has no executor here is reported as not callable — the
/// catalogue never offers what the code cannot do.
/// </summary>
public sealed class ProductExecutorRegistry(IEnumerable<IProductExecutor> executors)
{
    private readonly Dictionary<(string Slug, string Version), IProductExecutor> _executors =
        executors.ToDictionary(e => (e.ProductSlug, e.Version));

    public IProductExecutor? Find(string slug, string? version) =>
        version is not null && _executors.TryGetValue((slug, version), out var executor) ? executor : null;

    public bool Has(string slug, string? version) => Find(slug, version) is not null;
}
