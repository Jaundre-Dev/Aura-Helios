using System.Text.Json;
using Helios.Contracts.Catalogue;

namespace Helios.Application.Features.Products;

/// <summary>
/// One product version's implementation. Executors are first-party code or typed adapters over an
/// approved provider; they never see tenant identity beyond what they need, never choose outbound
/// destinations from input, and never decide billing.
/// </summary>
public interface IProductExecutor
{
    string ProductSlug { get; }
    string Version { get; }

    /// <summary>
    /// Validates the request body before anything is recorded or charged. Throws
    /// <see cref="ProductInputException"/> with field errors for invalid input.
    /// </summary>
    ParsedProductInput Parse(JsonElement body);

    Task<ProductOutcome> ExecuteAsync(ParsedProductInput input, ApiEnvironment environment, CancellationToken cancellationToken);
}

/// <summary>
/// Validated input. <see cref="Canonical"/> is a stable serialisation used only to fingerprint the
/// request for idempotency; it is never stored or logged.
/// </summary>
public sealed record ParsedProductInput(object Value, string Canonical);

public sealed record ProductOutcome(
    JsonElement Result,
    IReadOnlyList<string> Warnings,
    bool ReviewRequired,
    string UsageUnit,
    decimal UsageQuantity);

/// <summary>Invalid input: reported as a 400 with field errors, never recorded or charged.</summary>
public sealed class ProductInputException(IDictionary<string, string[]> errors)
    : Exception("The request body is not valid for this product.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;

    public static ProductInputException For(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
