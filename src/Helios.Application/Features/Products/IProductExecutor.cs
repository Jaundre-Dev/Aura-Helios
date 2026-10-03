using System.Text.Json;
using Helios.Contracts.Catalogue;

namespace Helios.Application.Features.Products;

public enum ExecutionMode
{
    /// <summary>Bounded and fast: executed inline and answered with 200 when it completes.</summary>
    Synchronous,

    /// <summary>Accepted with 202 and executed by a worker; the caller polls or receives a webhook.</summary>
    Asynchronous
}

/// <summary>
/// One product version's implementation. Executors are first-party code or typed adapters over an
/// approved provider; they never see tenant identity beyond what they need, never choose outbound
/// destinations from input, and never decide billing.
/// </summary>
public interface IProductExecutor
{
    string ProductSlug { get; }
    string Version { get; }

    ExecutionMode Mode { get; }

    /// <summary>
    /// True when running the same input twice has no external effect or cost — deterministic
    /// first-party code. False for anything that calls a provider: an attempt interrupted mid-call
    /// is then reconciled with the provider, never blindly repeated.
    /// </summary>
    bool SafeToRepeat { get; }

    /// <summary>
    /// Validates the request body before anything is recorded or charged. Throws
    /// <see cref="ProductInputException"/> with field errors for invalid input.
    /// </summary>
    ParsedProductInput Parse(JsonElement body);

    /// <summary>The most billable units this input can consume; the reservation is sized from it.</summary>
    decimal EstimateMaxUnits(ParsedProductInput input);

    /// <summary>Sizing for document products, which know the referenced upload's page count.</summary>
    decimal EstimateMaxUnits(ParsedProductInput input, UploadFacts? upload) => EstimateMaxUnits(input);

    /// <summary>The upload this input refers to, for products that process a document.</summary>
    Guid? UploadIdOf(ParsedProductInput input) => null;

    /// <summary>Detected media types this version can process; checked at acceptance.</summary>
    IReadOnlyCollection<string> SupportedMediaTypes => [];

    /// <summary>Most pages this version accepts; 0 when it takes no documents.</summary>
    int MaxPages => 0;

    /// <summary>
    /// Runs the product. A business outcome (including "no match" or "invalid") is a returned
    /// <see cref="ProductOutcome"/>. Throw <see cref="ProviderUnavailableException"/> when the work
    /// certainly did not happen, and <see cref="ProviderOutcomeUnknownException"/> when it may have.
    /// </summary>
    Task<ProductOutcome> ExecuteAsync(ParsedProductInput input, ProductExecutionContext context, CancellationToken cancellationToken);

    /// <summary>Asks the provider what happened to an earlier, ambiguous attempt.</summary>
    Task<ReconcileOutcome> ReconcileAsync(string? providerReference, ProductExecutionContext context, CancellationToken cancellationToken);
}

/// <param name="Uploads">Reads uploads in the executing request's own workspace only.</param>
public sealed record ProductExecutionContext(Guid RequestId, ApiEnvironment Environment, int Attempt, IUploadAccess? Uploads = null);

/// <summary>What acceptance established about a referenced upload.</summary>
public sealed record UploadFacts(Guid Id, string MediaType, int PageCount, bool HasTextLayer);

/// <summary>Opens upload content for an executor, confined to the current workspace.</summary>
public interface IUploadAccess
{
    /// <summary>The bytes, or null when the upload is gone (deleted or expired since acceptance).</summary>
    Task<byte[]?> OpenAsync(Guid uploadId, CancellationToken cancellationToken);
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

public enum ReconcileStatus
{
    /// <summary>The provider completed the work; settle with the returned outcome.</summary>
    Completed,

    /// <summary>The provider confirms the work never happened; release without charge.</summary>
    NotCompleted,

    /// <summary>The provider cannot say yet; ask again later.</summary>
    StillUnknown
}

public sealed record ReconcileOutcome(ReconcileStatus Status, ProductOutcome? Outcome = null)
{
    public static ReconcileOutcome NotCompleted { get; } = new(ReconcileStatus.NotCompleted);
    public static ReconcileOutcome StillUnknown { get; } = new(ReconcileStatus.StillUnknown);
    public static ReconcileOutcome Completed(ProductOutcome outcome) => new(ReconcileStatus.Completed, outcome);
}

/// <summary>Invalid input: reported as a 400 with field errors, never recorded or charged.</summary>
public sealed class ProductInputException(IDictionary<string, string[]> errors)
    : Exception("The request body is not valid for this product.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;

    public static ProductInputException For(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}

/// <summary>The work certainly did not happen (connection refused, provider said "not processed"). Retryable.</summary>
public sealed class ProviderUnavailableException(string message) : Exception(message);

/// <summary>
/// The work may have happened (timeout after sending, dropped response). Never retried blindly:
/// the job moves to reconciliation with whatever provider reference is known.
/// </summary>
public sealed class ProviderOutcomeUnknownException(string message, string? providerReference) : Exception(message)
{
    public string? ProviderReference { get; } = providerReference;
}
