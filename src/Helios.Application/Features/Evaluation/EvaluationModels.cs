using System.Text.Json;

namespace Helios.Application.Features.Evaluation;

/// <summary>
/// A labelled evaluation dataset, described by <c>manifest.json</c> in the dataset folder. Expected
/// values are keyed by the same paths reviewers use: <c>fields.total</c>, <c>type</c>,
/// <c>transactions[0].amount</c>. An expected <c>null</c> means "must not be extracted": a value
/// produced there counts as invented.
/// </summary>
public sealed record EvaluationManifest(
    string Name,
    string Product,
    string? Version,
    EvaluationTargets? Targets,
    IReadOnlyList<LabelledDocument> Documents,
    bool CaseSensitive = false);

/// <param name="FieldAccuracy">Per-path minimum accuracy (0–1).</param>
/// <param name="DefaultFieldAccuracy">Minimum for labelled paths without their own target.</param>
/// <param name="MaxReviewRate">Most documents (0–1) that may be flagged for review.</param>
/// <param name="MaxP95Milliseconds">95th-percentile processing time per document.</param>
/// <param name="MaxInventedValues">Values produced where the label says there is none; default zero.</param>
public sealed record EvaluationTargets(
    IReadOnlyDictionary<string, double>? FieldAccuracy,
    double? DefaultFieldAccuracy,
    double? MaxReviewRate,
    double? MaxP95Milliseconds,
    int? MaxInventedValues);

public sealed record LabelledDocument(string File, IReadOnlyDictionary<string, JsonElement> Expected);

public static class ComparisonOutcome
{
    public const string Correct = "correct";
    public const string Wrong = "wrong";
    public const string Missed = "missed";
    public const string Invented = "invented";
}

/// <summary>A labelled value that did not match. Values appear only when the run asked for them.</summary>
public sealed record Mismatch(string Path, string Outcome, string? Expected, string? Actual);

public sealed record DocumentOutcome(
    string File,
    string Status,
    bool Readable,
    bool ReviewRequired,
    double Milliseconds,
    IReadOnlyList<Mismatch> Mismatches,
    string? Error);

public sealed record FieldMetric(
    string Path,
    int Labelled,
    int Correct,
    int Wrong,
    int Missed,
    int Invented,
    double Accuracy,
    double? Target,
    bool? TargetMet);

public sealed record TargetResult(string Target, double Required, double Actual, bool Met);

public sealed record EvaluationReport(
    string Dataset,
    string Product,
    string Version,
    DateTimeOffset RanAt,
    int Documents,
    int Errors,
    int Unreadable,
    double ReviewRate,
    double P50Milliseconds,
    double P95Milliseconds,
    double OverallAccuracy,
    int InventedValues,
    IReadOnlyList<FieldMetric> Fields,
    IReadOnlyList<TargetResult> Targets,
    bool AllTargetsMet,
    bool ValuesIncluded,
    IReadOnlyList<DocumentOutcome> DocumentOutcomes);
