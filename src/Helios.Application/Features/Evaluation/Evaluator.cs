using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Helios.Application.Features.Products;
using Helios.Contracts.Catalogue;

namespace Helios.Application.Features.Evaluation;

/// <summary>
/// Measures a product version against a labelled dataset (plan section 14, P3 gate): per-path
/// accuracy, values invented where none exist, review rate and processing time, each compared with
/// the dataset's written targets. It runs the same executor code the API runs, in process, on the
/// owner's own machine; nothing is uploaded or stored. Without targets the gate cannot pass.
/// </summary>
public static partial class Evaluator
{
    public const double NumberTolerance = 0.005;

    public static async Task<EvaluationReport> RunAsync(
        EvaluationManifest manifest,
        IProductExecutor executor,
        Func<string, Task<byte[]>> readDocument,
        bool includeValues,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!string.Equals(manifest.Product, executor.ProductSlug, StringComparison.Ordinal) ||
            (manifest.Version is { } v && v != executor.Version))
        {
            throw new InvalidOperationException(
                $"The dataset is for {manifest.Product} v{manifest.Version ?? "any"}, not {executor.ProductSlug} v{executor.Version}.");
        }

        var outcomes = new List<DocumentOutcome>();
        var tallies = new Dictionary<string, int[]>(StringComparer.Ordinal); // correct, wrong, missed, invented

        // One untimed, uncounted run first, so start-up cost (JIT, font tables) is not reported as
        // the processing time of whichever document happens to come first.
        if (manifest.Documents.Count > 0)
        {
            await EvaluateAsync(manifest.Documents[0], executor, readDocument, manifest.CaseSensitive, false,
                new Dictionary<string, int[]>(StringComparer.Ordinal), ct);
        }

        foreach (var document in manifest.Documents)
        {
            outcomes.Add(await EvaluateAsync(document, executor, readDocument, manifest.CaseSensitive, includeValues, tallies, ct));
        }

        var processed = outcomes.Where(o => o.Status == "ok").ToList();
        var times = processed.Select(o => o.Milliseconds).Order().ToList();

        var fields = tallies.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t =>
        {
            var labelled = t.Value.Sum();
            var accuracy = labelled == 0 ? 0 : (double)t.Value[0] / labelled;
            var target = manifest.Targets?.FieldAccuracy?.GetValueOrDefault(t.Key) is { } specific and > 0
                ? specific
                : manifest.Targets?.DefaultFieldAccuracy;
            return new FieldMetric(t.Key, labelled, t.Value[0], t.Value[1], t.Value[2], t.Value[3], Math.Round(accuracy, 4),
                target, target is null ? null : accuracy >= target);
        }).ToList();

        var labelledTotal = fields.Sum(f => f.Labelled);
        var reviewRate = processed.Count == 0 ? 0 : (double)processed.Count(o => o.ReviewRequired) / processed.Count;
        var invented = fields.Sum(f => f.Invented);
        var p95 = Percentile(times, 0.95);

        var targets = new List<TargetResult>();
        targets.AddRange(fields.Where(f => f.Target is not null)
            .Select(f => new TargetResult($"accuracy:{f.Path}", f.Target!.Value, f.Accuracy, f.TargetMet!.Value)));

        if (manifest.Targets is { } t)
        {
            if (t.MaxReviewRate is { } maxReview)
            {
                targets.Add(new TargetResult("max_review_rate", maxReview, Math.Round(reviewRate, 4), reviewRate <= maxReview));
            }

            if (t.MaxP95Milliseconds is { } maxP95)
            {
                targets.Add(new TargetResult("max_p95_ms", maxP95, Math.Round(p95, 1), p95 <= maxP95));
            }

            var maxInvented = t.MaxInventedValues ?? 0;
            targets.Add(new TargetResult("max_invented_values", maxInvented, invented, invented <= maxInvented));
        }

        var errors = outcomes.Count(o => o.Status != "ok");
        if (errors > 0)
        {
            targets.Add(new TargetResult("documents_processed", outcomes.Count, processed.Count, false));
        }

        return new EvaluationReport(
            manifest.Name, executor.ProductSlug, executor.Version, clock.GetUtcNow(),
            outcomes.Count, errors, processed.Count(o => !o.Readable),
            Math.Round(reviewRate, 4), Math.Round(Percentile(times, 0.5), 1), Math.Round(p95, 1),
            labelledTotal == 0 ? 0 : Math.Round((double)fields.Sum(f => f.Correct) / labelledTotal, 4),
            invented, fields, targets,
            AllTargetsMet: targets.Count > 0 && targets.All(x => x.Met) && manifest.Targets is not null,
            includeValues, outcomes);
    }

    private static async Task<DocumentOutcome> EvaluateAsync(
        LabelledDocument document,
        IProductExecutor executor,
        Func<string, Task<byte[]>> readDocument,
        bool caseSensitive,
        bool includeValues,
        Dictionary<string, int[]> tallies,
        CancellationToken ct)
    {
        ProductOutcome outcome;
        var watch = new Stopwatch();

        try
        {
            var bytes = await readDocument(document.File);
            var uploadId = Guid.NewGuid();
            var input = executor.Parse(JsonSerializer.SerializeToElement(new { uploadId }));
            var context = new ProductExecutionContext(Guid.NewGuid(), ApiEnvironment.Sandbox, 1, new SingleDocument(uploadId, bytes));

            watch.Start();
            outcome = await executor.ExecuteAsync(input, context, ct);
            watch.Stop();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DocumentOutcome(document.File, "error", false, true, watch.Elapsed.TotalMilliseconds, [], ex.GetType().Name + ": " + ex.Message);
        }

        var result = outcome.Result;
        var readable = !(result.TryGetProperty("readable", out var r) && r.ValueKind == JsonValueKind.False);
        var mismatches = new List<Mismatch>();

        foreach (var (path, expected) in document.Expected)
        {
            var actual = Resolve(result, path);
            var verdict = Compare(expected, actual, caseSensitive);

            var tally = tallies.TryGetValue(path, out var existing) ? existing : tallies[path] = new int[4];
            tally[verdict switch
            {
                ComparisonOutcome.Correct => 0,
                ComparisonOutcome.Wrong => 1,
                ComparisonOutcome.Missed => 2,
                _ => 3
            }]++;

            if (verdict != ComparisonOutcome.Correct)
            {
                mismatches.Add(new Mismatch(path, verdict,
                    includeValues ? Text(expected) : null,
                    includeValues ? Text(actual) : null));
            }
        }

        return new DocumentOutcome(document.File, "ok", readable, outcome.ReviewRequired, Math.Round(watch.Elapsed.TotalMilliseconds, 2), mismatches, null);
    }

    /// <summary>
    /// The value at a path. A document field (an object with <c>value</c> and <c>evidence</c>)
    /// resolves to its value; anything absent resolves to null.
    /// </summary>
    public static JsonElement? Resolve(JsonElement result, string path)
    {
        JsonElement current = result;

        foreach (var segment in path.Split('.'))
        {
            var match = Segment().Match(segment);
            if (!match.Success || current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(match.Groups["name"].Value, out current))
            {
                return null;
            }

            if (match.Groups["index"].Success)
            {
                var index = int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture);
                if (current.ValueKind != JsonValueKind.Array || index >= current.GetArrayLength())
                {
                    return null;
                }

                current = current[index];
            }
        }

        if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty("value", out var value) && current.TryGetProperty("evidence", out _))
        {
            current = value;
        }

        return current.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : current;
    }

    public static string Compare(JsonElement expected, JsonElement? actual, bool caseSensitive)
    {
        if (expected.ValueKind == JsonValueKind.Null)
        {
            return actual is null ? ComparisonOutcome.Correct : ComparisonOutcome.Invented;
        }

        if (actual is not { } a)
        {
            return ComparisonOutcome.Missed;
        }

        var same = (expected.ValueKind, a.ValueKind) switch
        {
            (JsonValueKind.Number, JsonValueKind.Number) => Math.Abs(expected.GetDecimal() - a.GetDecimal()) <= (decimal)NumberTolerance,
            (JsonValueKind.Array, JsonValueKind.Array) =>
                expected.GetArrayLength() == a.GetArrayLength() &&
                expected.EnumerateArray().Zip(a.EnumerateArray()).All(p => SameText(Text(p.First), Text(p.Second), caseSensitive)),
            (JsonValueKind.True or JsonValueKind.False, JsonValueKind.True or JsonValueKind.False) => expected.ValueKind == a.ValueKind,
            (JsonValueKind.String, JsonValueKind.String) => SameText(expected.GetString(), a.GetString(), caseSensitive),
            _ => false
        };

        return same ? ComparisonOutcome.Correct : ComparisonOutcome.Wrong;
    }

    public static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        var rank = (int)Math.Ceiling(p * sorted.Count) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
    }

    /// <summary>A Markdown summary for the owner. Contains no document values unless the run included them.</summary>
    public static string ToMarkdown(EvaluationReport report)
    {
        var md = new StringBuilder();
        md.AppendLine($"# Evaluation: {report.Dataset}");
        md.AppendLine();
        md.AppendLine($"Product `{report.Product}` v{report.Version}, run {report.RanAt:yyyy-MM-dd HH:mm} UTC.");
        md.AppendLine();
        md.AppendLine(report.AllTargetsMet
            ? "**All written targets met.**"
            : report.Targets.Count == 0
                ? "**No targets were written for this dataset; the gate cannot pass without them.**"
                : "**Targets not met.**");
        md.AppendLine();
        md.AppendLine("| Measure | Value |");
        md.AppendLine("| --- | --- |");
        md.AppendLine($"| Documents | {report.Documents} ({report.Errors} errors, {report.Unreadable} unreadable) |");
        md.AppendLine($"| Overall accuracy | {report.OverallAccuracy:P1} |");
        md.AppendLine($"| Invented values | {report.InventedValues} |");
        md.AppendLine($"| Review rate | {report.ReviewRate:P1} |");
        md.AppendLine($"| Time per document p50 / p95 | {report.P50Milliseconds} ms / {report.P95Milliseconds} ms |");
        md.AppendLine();
        md.AppendLine("| Path | Labelled | Correct | Wrong | Missed | Invented | Accuracy | Target |");
        md.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var f in report.Fields)
        {
            md.AppendLine($"| `{f.Path}` | {f.Labelled} | {f.Correct} | {f.Wrong} | {f.Missed} | {f.Invented} | {f.Accuracy:P1} | " +
                          (f.Target is null ? "—" : $"{f.Target:P0} {(f.TargetMet == true ? "met" : "**missed**")}") + " |");
        }

        if (report.Targets.Count > 0)
        {
            md.AppendLine();
            md.AppendLine("| Target | Required | Actual | Result |");
            md.AppendLine("| --- | --- | --- | --- |");
            foreach (var t in report.Targets)
            {
                md.AppendLine($"| {t.Target} | {t.Required.ToString(CultureInfo.InvariantCulture)} | {t.Actual.ToString(CultureInfo.InvariantCulture)} | {(t.Met ? "met" : "**missed**")} |");
            }
        }

        md.AppendLine();
        md.AppendLine(report.ValuesIncluded
            ? "This report includes extracted and expected values. Handle it as the documents themselves."
            : "Document values are omitted; rerun with --include-values to see them.");

        return md.ToString();
    }

    private static bool SameText(string? a, string? b, bool caseSensitive) =>
        string.Equals(Normalise(a), Normalise(b), caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private static string Normalise(string? text) => Whitespace().Replace(text ?? string.Empty, " ").Trim();

    private static string? Text(JsonElement? element) => element switch
    {
        null => null,
        { ValueKind: JsonValueKind.String } e => e.GetString(),
        { } e => e.GetRawText()
    };

    [GeneratedRegex(@"^(?<name>[A-Za-z][A-Za-z0-9]*)(?:\[(?<index>\d+)\])?$")]
    private static partial Regex Segment();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>Hands the executor this one document, as upload access would.</summary>
    private sealed class SingleDocument(Guid id, byte[] content) : IUploadAccess
    {
        public Task<byte[]?> OpenAsync(Guid uploadId, CancellationToken cancellationToken) =>
            Task.FromResult(uploadId == id ? content : null);
    }
}
