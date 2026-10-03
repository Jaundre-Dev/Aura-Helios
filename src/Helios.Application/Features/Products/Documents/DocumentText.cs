using System.Text.Json;
using System.Text.RegularExpressions;
using Helios.Application.Abstractions.Documents;

namespace Helios.Application.Features.Products.Documents;

/// <summary>One line of a document's text layer, with the evidence that points back to it.</summary>
public sealed record DocLine(int Page, PdfTextLine Text)
{
    public string Value => Text.Text;
    public Evidence Evidence => new(Page, Text.Box.ToArray(), Text.Text);
}

/// <summary>An amount read from the end of a line, with any debit/credit marker printed beside it.</summary>
public sealed record TrailingAmount(decimal Value, string? Marker);

/// <summary>
/// Shared, deterministic helpers for reading labelled values from a PDF text layer. Every value is
/// taken from a line that carries an explicit label and is returned with that line as evidence;
/// nothing is inferred from position alone. Parsing of amounts and dates follows
/// <see cref="InvoiceFieldExtractor.ParseMoney"/> and <see cref="InvoiceFieldExtractor.ParseDate"/>.
/// </summary>
public static partial class DocumentText
{
    public static List<DocLine> Lines(PdfText document) =>
        document.Pages.SelectMany(p => p.Lines.Select(l => new DocLine(p.Number, l))).ToList();

    /// <summary>"Label: value" on one line, or the label alone with the value on the next line of the same page.</summary>
    public static ExtractedField<string>? Labelled(IReadOnlyList<DocLine> lines, Regex label)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var match = label.Match(lines[i].Value);
            if (!match.Success)
            {
                continue;
            }

            var value = match.Groups["v"].Value.Trim();
            if (value.Length > 0)
            {
                return new ExtractedField<string>(value, lines[i].Evidence);
            }

            if (i + 1 < lines.Count && lines[i + 1].Page == lines[i].Page && !LooksLikeLabel(lines[i + 1].Value))
            {
                return new ExtractedField<string>(lines[i + 1].Value.Trim(), lines[i + 1].Evidence);
            }
        }

        return null;
    }

    /// <summary>The first date on a line matching <paramref name="label"/> (group <c>v</c> holds the text after the label).</summary>
    public static ExtractedField<DateOnly>? LabelledDate(IEnumerable<DocLine> lines, Regex label)
    {
        foreach (var line in lines)
        {
            var match = label.Match(line.Value);
            if (match.Success && InvoiceFieldExtractor.ParseDate(match.Groups["v"].Value) is { } date)
            {
                return new ExtractedField<DateOnly>(date, line.Evidence);
            }
        }

        return null;
    }

    /// <summary>Two dates on one labelled line ("Period: 01/08/2026 to 31/08/2026").</summary>
    public static (ExtractedField<DateOnly> From, ExtractedField<DateOnly> To)? LabelledRange(IEnumerable<DocLine> lines, Regex label)
    {
        foreach (var line in lines)
        {
            var match = label.Match(line.Value);
            if (!match.Success)
            {
                continue;
            }

            var dates = DateTokens().Matches(match.Groups["v"].Value)
                .Select(m => InvoiceFieldExtractor.ParseDate(m.Value))
                .OfType<DateOnly>()
                .ToList();

            if (dates.Count >= 2)
            {
                return (new ExtractedField<DateOnly>(dates[0], line.Evidence), new ExtractedField<DateOnly>(dates[1], line.Evidence));
            }
        }

        return null;
    }

    /// <summary>The amount at the end of the last line matching <paramref name="label"/>.</summary>
    public static ExtractedField<decimal>? LabelledAmount(IEnumerable<DocLine> lines, Regex label, bool last = true)
    {
        ExtractedField<decimal>? found = null;

        foreach (var line in lines.Where(l => label.IsMatch(l.Value)))
        {
            if (TrailingAmounts(line.Value, 1).Amounts is [var amount])
            {
                found = new ExtractedField<decimal>(Signed(amount), line.Evidence);
                if (!last)
                {
                    return found;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Up to <paramref name="max"/> amounts at the end of a line, left to right, and the text before
    /// them. Each amount may carry a sign or a "Cr"/"Dr" marker.
    /// </summary>
    public static (IReadOnlyList<TrailingAmount> Amounts, string Before) TrailingAmounts(string line, int max)
    {
        var amounts = new List<TrailingAmount>();
        var rest = line.TrimEnd();

        while (amounts.Count < max)
        {
            var match = TrailingMoney().Match(rest);
            if (!match.Success || InvoiceFieldExtractor.ParseMoney(match.Groups["amount"].Value) is not { } value)
            {
                break;
            }

            var marker = match.Groups["marker"].Success ? match.Groups["marker"].Value.ToUpperInvariant() : null;
            if (match.Groups["minus"].Success || match.Groups["minus2"].Success)
            {
                marker ??= "-";
            }

            amounts.Insert(0, new TrailingAmount(Math.Abs(value), marker));
            rest = rest[..match.Index].TrimEnd();
        }

        return (amounts, rest);
    }

    /// <summary>An amount with its printed sign applied: "-" and "DR" are negative.</summary>
    public static decimal Signed(TrailingAmount amount) =>
        amount.Marker is "-" or "DR" ? -amount.Value : amount.Value;

    /// <summary>
    /// Keeps the last four digits of an account number and masks the rest, so the result never
    /// carries a full account number (plan section 11 masking policy).
    /// </summary>
    public static string MaskAccountNumber(string raw)
    {
        var digits = new string(raw.Where(char.IsAsciiDigit).ToArray());
        return digits.Length <= 4 ? new string('*', digits.Length) : new string('*', digits.Length - 4) + digits[^4..];
    }

    public static bool LooksLikeLabel(string line) => line.Contains(':');

    // JSON shapes shared by the document products.

    public static object? Field<T>(ExtractedField<T>? field) =>
        field is null ? null : new { value = field.Value, evidence = EvidenceJson(field.Evidence) };

    public static object? DateField(ExtractedField<DateOnly>? field) =>
        field is null ? null : new { value = field.Value.ToString("yyyy-MM-dd"), evidence = EvidenceJson(field.Evidence) };

    public static object EvidenceJson(Evidence e) => new { page = e.Page, box = e.Box, source = e.Line };

    public static object CheckJson(InvoiceCheck c) => new { name = c.Name, outcome = c.Outcome, detail = c.Detail };

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // A money token, optionally signed, optionally followed by a Cr/Dr marker, at the end of the text.
    [GeneratedRegex(@"(?:^|\s)(?<minus>-)?(?<amount>(?:R|ZAR)?\s?\d{1,3}(?:[ ,]\d{3})*[.,]\d{2}|(?:R|ZAR)?\s?\d+[.,]\d{2})(?<minus2>-)?\s*(?<marker>Cr|CR|Dr|DR)?\s*$")]
    private static partial Regex TrailingMoney();

    [GeneratedRegex(@"\d{4}[-/.]\d{1,2}[-/.]\d{1,2}|\d{1,2}[-/.]\d{1,2}[-/.]\d{4}|\d{1,2}\s+[A-Za-z]{3,9}\.?,?\s+\d{4}|[A-Za-z]{3,9}\.?\s+\d{1,2},?\s+\d{4}")]
    private static partial Regex DateTokens();
}
