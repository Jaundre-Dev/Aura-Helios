using System.Text.Json;
using Helios.Application.Features.Evaluation;
using Helios.Application.Features.Products.Documents;
using Helios.Infrastructure.Documents;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Helios.UnitTests;

/// <summary>
/// The evaluation harness on synthetic invoices: per-path accuracy, missed and invented values,
/// targets, and redaction of document values unless explicitly requested.
/// </summary>
public class EvaluatorTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static byte[] Pdf(params string[] lines)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        var y = 800d;
        foreach (var line in lines)
        {
            page.AddText(line, 11, new PdfPoint(50, y), font);
            y -= 18;
        }

        return builder.Build();
    }

    private static readonly Dictionary<string, byte[]> Files = new()
    {
        ["a.pdf"] = Pdf("Invoice No: A-1", "Invoice Date: 01/09/2026", "Subtotal 100.00", "VAT 15.00", "Total Due 115.00"),
        ["b.pdf"] = Pdf("Invoice No: B-2", "Invoice Date: 02/09/2026", "Total Due 230.00"),
        ["c.pdf"] = Pdf("Invoice No: C-3", "Total Due 50.00"),
    };

    private static EvaluationManifest Manifest(EvaluationTargets? targets) => new(
        "synthetic-invoices", InvoiceExtractionExecutor.Slug, "1", targets,
        [
            new LabelledDocument("a.pdf", new Dictionary<string, JsonElement>
            {
                ["fields.invoiceNumber"] = J("\"A-1\""), ["fields.total"] = J("115.00"), ["fields.dueDate"] = J("null"),
            }),
            new LabelledDocument("b.pdf", new Dictionary<string, JsonElement>
            {
                // Labelled wrongly on purpose: the extractor reads 230.00.
                ["fields.invoiceNumber"] = J("\"b-2\""), ["fields.total"] = J("231.00"), ["fields.dueDate"] = J("null"),
            }),
            new LabelledDocument("c.pdf", new Dictionary<string, JsonElement>
            {
                // The PDF has no invoice date, but the label says there is one: missed.
                ["fields.invoiceNumber"] = J("\"C-3\""), ["fields.invoiceDate"] = J("\"2026-09-03\""),
            }),
        ]);

    private static Task<EvaluationReport> RunAsync(EvaluationTargets? targets, bool includeValues = false) =>
        Evaluator.RunAsync(Manifest(targets), new InvoiceExtractionExecutor(new PdfPigTextReader()),
            file => Task.FromResult(Files[file]), includeValues, TimeProvider.System, CancellationToken.None);

    [Fact]
    public async Task Accuracy_is_measured_per_path_with_missed_and_wrong_values_counted()
    {
        var report = await RunAsync(new EvaluationTargets(
            new Dictionary<string, double> { ["fields.total"] = 0.9 }, DefaultFieldAccuracy: 0.6,
            MaxReviewRate: 0.5, MaxP95Milliseconds: 60_000, MaxInventedValues: 0));

        var number = report.Fields.Single(f => f.Path == "fields.invoiceNumber");
        Assert.Equal((3, 3), (number.Labelled, number.Correct)); // case-insensitive by default

        var total = report.Fields.Single(f => f.Path == "fields.total");
        Assert.Equal((2, 1, 1), (total.Labelled, total.Correct, total.Wrong));
        Assert.Equal(0.9, total.Target);
        Assert.False(total.TargetMet);

        var date = report.Fields.Single(f => f.Path == "fields.invoiceDate");
        Assert.Equal(1, date.Missed);

        Assert.Equal(0, report.InventedValues);
        Assert.Equal(3, report.Documents);
        Assert.Equal(0, report.Errors);
        Assert.Equal(1.0 / 3, report.ReviewRate, 3); // c.pdf misses a required field
        Assert.False(report.AllTargetsMet);
        Assert.Contains(report.Targets, t => t.Target == "accuracy:fields.total" && !t.Met);
        Assert.Contains(report.Targets, t => t.Target == "max_review_rate" && t.Met);
    }

    [Fact]
    public async Task Values_are_left_out_of_the_report_unless_asked_for()
    {
        var redacted = await RunAsync(null);
        var mismatch = redacted.DocumentOutcomes.Single(d => d.File == "b.pdf").Mismatches.Single();
        Assert.Equal(("fields.total", "wrong", null, null), (mismatch.Path, mismatch.Outcome, mismatch.Expected, mismatch.Actual));
        Assert.DoesNotContain("231.00", Evaluator.ToMarkdown(redacted));

        var full = await RunAsync(null, includeValues: true);
        var detailed = full.DocumentOutcomes.Single(d => d.File == "b.pdf").Mismatches.Single();
        Assert.Equal(("231.00", "230.00"), (detailed.Expected, detailed.Actual));
    }

    [Fact]
    public async Task Without_written_targets_the_gate_cannot_pass()
    {
        var report = await RunAsync(null);

        Assert.False(report.AllTargetsMet);
        Assert.Contains("No targets were written", Evaluator.ToMarkdown(report));
    }

    [Fact]
    public async Task A_document_that_cannot_be_read_is_an_error_and_fails_the_gate()
    {
        var manifest = Manifest(new EvaluationTargets(null, 0.0, null, null, null)) with
        {
            Documents = [new LabelledDocument("broken.pdf", new Dictionary<string, JsonElement> { ["fields.total"] = J("1") })]
        };

        var report = await Evaluator.RunAsync(manifest, new InvoiceExtractionExecutor(new PdfPigTextReader()),
            _ => Task.FromResult("not a pdf"u8.ToArray()), false, TimeProvider.System, CancellationToken.None);

        Assert.Equal(1, report.Errors);
        Assert.False(report.AllTargetsMet);
        Assert.Contains(report.Targets, t => t.Target == "documents_processed" && !t.Met);
    }

    [Fact]
    public async Task A_dataset_for_another_product_is_refused()
    {
        var manifest = Manifest(null) with { Product = "documents.payslip" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Evaluator.RunAsync(manifest,
            new InvoiceExtractionExecutor(new PdfPigTextReader()), f => Task.FromResult(Files[f]), false, TimeProvider.System, CancellationToken.None));
    }

    [Theory]
    [InlineData("null", null, "correct")]
    [InlineData("null", "\"x\"", "invented")]
    [InlineData("\"x\"", null, "missed")]
    [InlineData("115", "115.004", "correct")]
    [InlineData("115", "115.01", "wrong")]
    [InlineData("\"Acme  Ltd\"", "\"acme ltd\"", "correct")]
    [InlineData("[\"1 Road\",\"Town 2000\"]", "[\"1 road\",\"Town  2000\"]", "correct")]
    [InlineData("\"115\"", "115", "wrong")]
    public void Comparison_rules(string expected, string? actual, string outcome)
    {
        Assert.Equal(outcome, Evaluator.Compare(J(expected), actual is null ? null : J(actual), caseSensitive: false));
    }

    [Fact]
    public void Paths_resolve_field_values_list_items_and_top_level_values()
    {
        var result = J("""{"type":"invoice","fields":{"total":{"value":5,"evidence":{}},"due":null},"transactions":[{"amount":-3}]}""");

        Assert.Equal("invoice", Evaluator.Resolve(result, "type")!.Value.GetString());
        Assert.Equal(5, Evaluator.Resolve(result, "fields.total")!.Value.GetInt32());
        Assert.Null(Evaluator.Resolve(result, "fields.due"));
        Assert.Equal(-3, Evaluator.Resolve(result, "transactions[0].amount")!.Value.GetInt32());
        Assert.Null(Evaluator.Resolve(result, "transactions[4].amount"));
        Assert.Null(Evaluator.Resolve(result, "fields.missing"));
    }
}
