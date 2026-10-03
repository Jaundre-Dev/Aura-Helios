using System.Text.Json;
using Helios.Application.Abstractions.Documents;

namespace Helios.Application.Features.Products.Documents;

/// <summary>
/// <c>ocr.general</c> v1: the text of a digital PDF, page by page and line by line, with each line's
/// position as evidence. It reads the PDF's own text layer — exact and deterministic — and does not
/// OCR images: a page without a text layer is returned empty, flagged for review and not charged.
/// Billed per page actually read.
/// </summary>
public sealed class OcrGeneralExecutor(IPdfTextReader reader) : IProductExecutor
{
    public const string Slug = "ocr.general";
    public const string CurrentVersion = "1";

    public const string Notice =
        "Text was read from the PDF's own text layer, not by optical recognition. Pages without a text layer " +
        "(scans, photos) are not read by this version.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string ProductSlug => Slug;
    public string Version => CurrentVersion;
    public ExecutionMode Mode => ExecutionMode.Asynchronous;

    /// <summary>Deterministic and local: re-reading the same document is harmless.</summary>
    public bool SafeToRepeat => true;

    public IReadOnlyCollection<string> SupportedMediaTypes => [DocumentInput.Pdf];
    public int MaxPages => 50;

    public ParsedProductInput Parse(JsonElement body) => DocumentInput.Parse(body);

    public Guid? UploadIdOf(ParsedProductInput input) => DocumentInput.UploadId(input);

    public decimal EstimateMaxUnits(ParsedProductInput input) => MaxPages;

    public decimal EstimateMaxUnits(ParsedProductInput input, UploadFacts? upload) => upload?.PageCount ?? MaxPages;

    public async Task<ProductOutcome> ExecuteAsync(ParsedProductInput input, ProductExecutionContext context, CancellationToken ct)
    {
        var text = await DocumentInput.ReadAsync(input, context, reader, ct);

        var pages = text.Pages.Select(page => new
        {
            page = page.Number,
            hasTextLayer = page.HasText,
            text = string.Join('\n', page.Lines.Select(l => l.Text)),
            lines = page.Lines.Select(l => new { text = l.Text, box = l.Box.ToArray() })
        }).ToList();

        var unread = text.Pages.Where(p => !p.HasText).Select(p => p.Number).ToList();
        var pagesWithText = text.Pages.Count - unread.Count;

        var result = new
        {
            method = "pdf_text_layer",
            pageCount = text.Pages.Count,
            pagesWithText,
            pages,
            notice = Notice
        };

        return new ProductOutcome(
            JsonSerializer.SerializeToElement(result, Json),
            unread.Select(DocumentInput.NoTextWarning).ToList(),
            ReviewRequired: unread.Count > 0,
            UsageUnit: "page",
            UsageQuantity: pagesWithText);
    }

    public Task<ReconcileOutcome> ReconcileAsync(string? providerReference, ProductExecutionContext context, CancellationToken ct) =>
        Task.FromResult(ReconcileOutcome.NotCompleted);
}
