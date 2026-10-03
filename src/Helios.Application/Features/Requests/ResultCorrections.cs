using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Helios.Application.Common;

namespace Helios.Application.Features.Requests;

/// <summary>One exported value: where it sits, what was extracted, what a reviewer set, and what stands.</summary>
public sealed record ExportRow(string Section, string Name, string? Original, string? Corrected, string? Final, int? Page, string? Source);

/// <summary>
/// Corrections to a stored result, addressed by path, and the export of a result with them applied.
/// Two path forms exist: <c>fields.&lt;name&gt;</c> for a document field (present or null — filling a
/// missing field is the commonest correction) and <c>&lt;array&gt;[i].&lt;property&gt;</c> for a plain
/// value inside a list item (a statement transaction, an invoice line). Evidence is never editable.
/// The original result is not modified; corrections are applied to a copy.
/// </summary>
public static partial class ResultCorrections
{
    public const int MaxCorrections = 100;
    public const int MaxStringLength = 1000;
    public const int MaxListItems = 20;

    public static Dictionary<string, string[]> Validate(JsonElement result, IReadOnlyDictionary<string, JsonElement> corrections)
    {
        var errors = new Dictionary<string, string[]>();

        if (corrections.Count > MaxCorrections)
        {
            errors["corrections"] = [$"At most {MaxCorrections} corrections per decision."];
            return errors;
        }

        foreach (var (path, value) in corrections)
        {
            var key = $"corrections.{path}";

            if (Resolve(result, path) is { } problem)
            {
                errors[key] = [problem];
            }
            else if (ValueProblem(value) is { } invalid)
            {
                errors[key] = [invalid];
            }
        }

        return errors;
    }

    /// <summary>The result with corrections applied in order, later ones overriding earlier ones.</summary>
    public static JsonNode Apply(JsonElement result, IEnumerable<KeyValuePair<string, JsonElement>> corrections)
    {
        var root = JsonNode.Parse(result.GetRawText())!;

        foreach (var (path, value) in corrections)
        {
            var field = FieldPath().Match(path);
            if (field.Success)
            {
                var fields = root["fields"]!.AsObject();
                var name = field.Groups["field"].Value;
                var evidence = fields[name]?["evidence"]?.DeepClone();

                fields[name] = new JsonObject
                {
                    ["value"] = JsonNode.Parse(value.GetRawText()),
                    ["evidence"] = evidence,
                    ["corrected"] = true
                };
                continue;
            }

            var item = ItemPath().Match(path);
            var target = root[item.Groups["array"].Value]![int.Parse(item.Groups["index"].Value, CultureInfo.InvariantCulture)]!.AsObject();
            target[item.Groups["prop"].Value] = JsonNode.Parse(value.GetRawText());
            target["corrected"] = true;
        }

        return root;
    }

    /// <summary>
    /// Flattens a result into rows: document fields, then list items, then other values. Each row
    /// shows the extracted value, any correction and the value that stands, with its page and source.
    /// </summary>
    public static IReadOnlyList<ExportRow> Rows(JsonElement original, IReadOnlyDictionary<string, JsonElement> corrections)
    {
        var rows = new List<ExportRow>();

        foreach (var property in original.EnumerateObject())
        {
            var value = property.Value;

            if (property.Name == "fields" && value.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in value.EnumerateObject())
                {
                    var extracted = field.Value.ValueKind == JsonValueKind.Object && field.Value.TryGetProperty("value", out var v) ? v : (JsonElement?)null;
                    var (page, source) = EvidenceOf(field.Value);
                    rows.Add(Row("fields", field.Name, extracted, Lookup(corrections, $"fields.{field.Name}"), page, source));
                }
            }
            else if (value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(i => i.ValueKind == JsonValueKind.Object))
            {
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    var (page, source) = EvidenceOf(item);
                    foreach (var prop in item.EnumerateObject().Where(p => IsPlain(p.Value)))
                    {
                        var path = $"{property.Name}[{index}].{prop.Name}";
                        rows.Add(Row($"{property.Name}[{index}]", prop.Name, prop.Value, Lookup(corrections, path), page, source));
                    }

                    index++;
                }
            }
            else if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in value.EnumerateObject().Where(p => IsPlain(p.Value)))
                {
                    rows.Add(Row(property.Name, prop.Name, prop.Value, null, null, null));
                }
            }
            else if (IsPlain(value))
            {
                rows.Add(Row("result", property.Name, value, null, null, null));
            }
        }

        return rows;
    }

    /// <summary>
    /// RFC 4180 CSV. Text cells that a spreadsheet would run as a formula are prefixed with an
    /// apostrophe; plain numbers (including negative amounts) are left as numbers.
    /// </summary>
    public static string ToCsv(IEnumerable<ExportRow> rows)
    {
        var csv = new StringBuilder("section,name,original,corrected,final,page,source\r\n");

        foreach (var r in rows)
        {
            csv.AppendJoin(',', new[]
            {
                Csv.Cell(r.Section), Csv.Cell(r.Name), Csv.Cell(r.Original), Csv.Cell(r.Corrected), Csv.Cell(r.Final),
                Csv.Cell(r.Page?.ToString(CultureInfo.InvariantCulture)), Csv.Cell(r.Source)
            }).Append("\r\n");
        }

        return csv.ToString();
    }

    private static JsonElement? Lookup(IReadOnlyDictionary<string, JsonElement> corrections, string path) =>
        corrections.TryGetValue(path, out var value) ? value : null;

    private static ExportRow Row(string section, string name, JsonElement? original, JsonElement? corrected, int? page, string? source)
    {
        var originalText = Text(original);
        var correctedText = corrected is null ? null : Text(corrected) ?? string.Empty;
        return new ExportRow(section, name, originalText, correctedText, corrected is null ? originalText : Text(corrected), page, source);
    }

    private static (int? Page, string? Source) EvidenceOf(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("evidence", out var evidence) &&
            evidence.ValueKind == JsonValueKind.Object)
        {
            int? page = evidence.TryGetProperty("page", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;
            var source = evidence.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            return (page, source);
        }

        return (null, null);
    }

    private static string? Text(JsonElement? element) => element switch
    {
        null => null,
        { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
        { ValueKind: JsonValueKind.String } e => e.GetString(),
        { ValueKind: JsonValueKind.True } => "true",
        { ValueKind: JsonValueKind.False } => "false",
        { ValueKind: JsonValueKind.Array } e => string.Join(" | ", e.EnumerateArray().Select(i => Text(i) ?? string.Empty)),
        { } e => e.GetRawText()
    };

    private static bool IsPlain(JsonElement value) =>
        value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null ||
        (value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(i => i.ValueKind is JsonValueKind.String or JsonValueKind.Number));


    /// <summary>Why a path cannot be corrected, or null when it can.</summary>
    private static string? Resolve(JsonElement result, string path)
    {
        var field = FieldPath().Match(path);
        if (field.Success)
        {
            return result.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Object &&
                   fields.TryGetProperty(field.Groups["field"].Value, out _)
                ? null
                : "This result has no such field.";
        }

        var item = ItemPath().Match(path);
        if (!item.Success)
        {
            return "Use 'fields.<name>' or '<list>[<index>].<property>'.";
        }

        var prop = item.Groups["prop"].Value;
        if (prop is "evidence" or "corrected")
        {
            return "Evidence and correction markers cannot be edited.";
        }

        if (!result.TryGetProperty(item.Groups["array"].Value, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return "This result has no such list.";
        }

        var index = int.Parse(item.Groups["index"].Value, CultureInfo.InvariantCulture);
        if (index >= array.GetArrayLength() || array[index].ValueKind != JsonValueKind.Object ||
            !array[index].TryGetProperty(prop, out var existing) || !IsPlain(existing))
        {
            return "No such plain value at this position.";
        }

        return null;
    }

    private static string? ValueProblem(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String when value.GetString()!.Length > MaxStringLength => $"Text values are limited to {MaxStringLength} characters.",
        JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => null,
        JsonValueKind.Array when value.GetArrayLength() <= MaxListItems &&
                                 value.EnumerateArray().All(i => i.ValueKind == JsonValueKind.String && i.GetString()!.Length <= 500) => null,
        JsonValueKind.Array => $"Lists are limited to {MaxListItems} text items of 500 characters.",
        _ => "Use text, a number, true/false, null or a list of text."
    };

    [GeneratedRegex(@"^fields\.(?<field>[A-Za-z][A-Za-z0-9]{0,63})$")]
    private static partial Regex FieldPath();

    [GeneratedRegex(@"^(?<array>[A-Za-z][A-Za-z0-9]{0,63})\[(?<index>\d{1,4})\]\.(?<prop>[A-Za-z][A-Za-z0-9]{0,63})$")]
    private static partial Regex ItemPath();
}
