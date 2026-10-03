using System.Text.Json;
using System.Text.Json.Nodes;
using Helios.Application.Abstractions.Documents;

namespace Helios.Application.Features.Products.Documents;

/// <summary>What a document extractor found: the result body, warnings and whether a person should look.</summary>
public sealed record DocumentExtraction(object Body, IReadOnlyList<string> Warnings, bool ReviewRequired);

/// <summary>
/// Shared shape of the v1 document products that read a digital PDF's own text layer: asynchronous,
/// deterministic (safe to repeat), PDF only, billed one unit per document actually read. A document
/// with no text layer at all is returned as unreadable, flagged for review and not charged; pages
/// without text are named in warnings. Each result carries the product's notice of what it does not do.
/// </summary>
public abstract class TextLayerProduct(IPdfTextReader reader) : IProductExecutor
{
    public abstract string ProductSlug { get; }
    public string Version => "1";
    public ExecutionMode Mode => ExecutionMode.Asynchronous;
    public bool SafeToRepeat => true;
    public IReadOnlyCollection<string> SupportedMediaTypes => [DocumentInput.Pdf];
    public abstract int MaxPages { get; }

    /// <summary>What the result does not establish; returned with every result.</summary>
    public abstract string Notice { get; }

    protected abstract DocumentExtraction Extract(PdfText text);

    public ParsedProductInput Parse(JsonElement body) => DocumentInput.Parse(body);

    public Guid? UploadIdOf(ParsedProductInput input) => DocumentInput.UploadId(input);

    public decimal EstimateMaxUnits(ParsedProductInput input) => 1m;

    public async Task<ProductOutcome> ExecuteAsync(ParsedProductInput input, ProductExecutionContext context, CancellationToken ct)
    {
        var text = await DocumentInput.ReadAsync(input, context, reader, ct);
        var unread = text.Pages.Where(p => !p.HasText).Select(p => p.Number).ToList();
        var warnings = unread.Select(DocumentInput.NoTextWarning).ToList();

        if (unread.Count == text.Pages.Count)
        {
            var empty = new { readable = false, notice = Notice };
            return new ProductOutcome(JsonSerializer.SerializeToElement(empty, DocumentText.Json), warnings, ReviewRequired: true, "document", 0m);
        }

        var extraction = Extract(text);
        warnings.AddRange(extraction.Warnings);

        var body = JsonSerializer.SerializeToNode(extraction.Body, DocumentText.Json)!.AsObject();
        var result = new JsonObject { ["readable"] = true };
        foreach (var (name, value) in body.ToList())
        {
            body.Remove(name);
            result[name] = value;
        }

        result["notice"] = Notice;

        return new ProductOutcome(
            JsonSerializer.SerializeToElement(result, DocumentText.Json),
            warnings,
            ReviewRequired: extraction.ReviewRequired || unread.Count > 0,
            UsageUnit: "document",
            UsageQuantity: 1m);
    }

    public Task<ReconcileOutcome> ReconcileAsync(string? providerReference, ProductExecutionContext context, CancellationToken ct) =>
        Task.FromResult(ReconcileOutcome.NotCompleted);

    /// <summary>Warnings for missing required fields and for checks that did not pass.</summary>
    protected static List<string> StandardWarnings(IEnumerable<string> missingRequired, IEnumerable<InvoiceCheck> checks)
    {
        var warnings = missingRequired.Select(f => $"Required field '{f}' was not found.").ToList();
        warnings.AddRange(checks.Where(c => c.Outcome is "fail" or "differs").Select(c => $"Check '{c.Name}' {c.Outcome}: {c.Detail}"));
        return warnings;
    }
}
