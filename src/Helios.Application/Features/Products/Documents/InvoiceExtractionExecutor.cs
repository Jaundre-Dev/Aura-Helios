using System.Text.Json;
using Helios.Application.Abstractions.Documents;

namespace Helios.Application.Features.Products.Documents;

/// <summary>
/// <c>documents.invoice</c> v1: supplier, customer, reference, dates, VAT numbers, totals and simple
/// line items from a digital PDF invoice, each with the page, box and source line it came from, plus
/// arithmetic and format checks. Fields that are not clearly labelled are returned as null and listed
/// as missing — never guessed. Billed per document; a document with no text layer is not charged.
/// </summary>
public sealed class InvoiceExtractionExecutor(IPdfTextReader reader) : IProductExecutor
{
    public const string Slug = "documents.invoice";
    public const string CurrentVersion = "1";

    public const string Notice =
        "Values are read from labelled lines of the PDF's text layer. Checks test the extracted values' arithmetic " +
        "and format; they do not establish that the invoice is authentic, that the supplier is VAT-registered, or " +
        "that the amounts are owed.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string ProductSlug => Slug;
    public string Version => CurrentVersion;
    public ExecutionMode Mode => ExecutionMode.Asynchronous;
    public bool SafeToRepeat => true;
    public IReadOnlyCollection<string> SupportedMediaTypes => [DocumentInput.Pdf];
    public int MaxPages => 20;

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
            return new ProductOutcome(JsonSerializer.SerializeToElement(empty, Json), warnings, ReviewRequired: true, "document", 0m);
        }

        var extraction = InvoiceFieldExtractor.Extract(text);

        warnings.AddRange(extraction.MissingRequired.Select(f => $"Required field '{f}' was not found."));
        warnings.AddRange(extraction.Checks
            .Where(c => c.Outcome is "fail" or "differs")
            .Select(c => $"Check '{c.Name}' {c.Outcome}: {c.Detail}"));

        var result = new
        {
            readable = true,
            fields = new
            {
                invoiceNumber = Field(extraction.InvoiceNumber),
                invoiceDate = Field(extraction.InvoiceDate, d => d.ToString("yyyy-MM-dd")),
                dueDate = Field(extraction.DueDate, d => d.ToString("yyyy-MM-dd")),
                supplierName = Field(extraction.SupplierName),
                supplierVatNumber = Field(extraction.SupplierVatNumber),
                customerName = Field(extraction.CustomerName),
                customerVatNumber = Field(extraction.CustomerVatNumber),
                currency = Field(extraction.Currency),
                subtotal = Field(extraction.Subtotal),
                vat = Field(extraction.Vat),
                total = Field(extraction.Total),
            },
            lineItems = extraction.LineItems.Select(i => new
            {
                description = i.Description,
                quantity = i.Quantity,
                unitPrice = i.UnitPrice,
                amount = i.Amount,
                evidence = Evidence(i.Evidence)
            }),
            checks = extraction.Checks.Select(c => new { name = c.Name, outcome = c.Outcome, detail = c.Detail }),
            missingRequired = extraction.MissingRequired,
            notice = Notice
        };

        return new ProductOutcome(
            JsonSerializer.SerializeToElement(result, Json),
            warnings,
            ReviewRequired: extraction.ReviewRequired || unread.Count > 0,
            UsageUnit: "document",
            UsageQuantity: 1m);
    }

    public Task<ReconcileOutcome> ReconcileAsync(string? providerReference, ProductExecutionContext context, CancellationToken ct) =>
        Task.FromResult(ReconcileOutcome.NotCompleted);

    private static object? Field<T>(ExtractedField<T>? field) =>
        field is null ? null : new { value = field.Value, evidence = Evidence(field.Evidence) };

    private static object? Field<T>(ExtractedField<T>? field, Func<T, string> format) =>
        field is null ? null : new { value = format(field.Value), evidence = Evidence(field.Evidence) };

    private static object Evidence(Evidence e) => new { page = e.Page, box = e.Box, source = e.Line };
}
