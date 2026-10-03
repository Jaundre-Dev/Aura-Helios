using System.Globalization;
using System.Text.RegularExpressions;
using Helios.Application.Abstractions.Documents;

namespace Helios.Application.Features.Products.Documents;

/// <summary>Where a value was read: page, normalised box and the exact source line.</summary>
public sealed record Evidence(int Page, double[] Box, string Line);

public sealed record ExtractedField<T>(T Value, Evidence Evidence);

public sealed record InvoiceLineItem(string Description, decimal Quantity, decimal UnitPrice, decimal Amount, Evidence Evidence);

/// <param name="Outcome"><c>pass</c>, <c>fail</c>, <c>differs</c> (worth a look, not an error) or <c>not_evaluated</c>.</param>
public sealed record InvoiceCheck(string Name, string Outcome, string? Detail = null);

public sealed record InvoiceExtraction(
    ExtractedField<string>? InvoiceNumber,
    ExtractedField<DateOnly>? InvoiceDate,
    ExtractedField<DateOnly>? DueDate,
    ExtractedField<string>? SupplierName,
    ExtractedField<string>? SupplierVatNumber,
    ExtractedField<string>? CustomerName,
    ExtractedField<string>? CustomerVatNumber,
    ExtractedField<string>? Currency,
    ExtractedField<decimal>? Subtotal,
    ExtractedField<decimal>? Vat,
    ExtractedField<decimal>? Total,
    IReadOnlyList<InvoiceLineItem> LineItems,
    IReadOnlyList<InvoiceCheck> Checks,
    IReadOnlyList<string> MissingRequired)
{
    public bool ReviewRequired => MissingRequired.Count > 0 || Checks.Any(c => c.Outcome == "fail");
}

/// <summary>
/// Deterministic invoice field extraction from a PDF text layer. Every value is taken from a line
/// that carries an explicit label ("Invoice No", "Total Due", "VAT", "Bill To"…), and returned with
/// the line it came from. Nothing is inferred from position alone, so an unlabelled supplier name
/// stays null rather than guessed. Arithmetic, VAT-rate and date checks flag inconsistencies; they
/// are checks on the extracted values, not an audit of the invoice.
/// <para>
/// Known limits (v1): English labels; day-first numeric dates (South African convention); one
/// invoice per document; line items only where quantity × unit price = amount on one line.
/// </para>
/// </summary>
public static partial class InvoiceFieldExtractor
{
    public const decimal StandardVatRate = 0.15m;

    private sealed record Line(int Page, PdfTextLine Text)
    {
        public string Value => Text.Text;
        public Evidence Evidence => new(Page, Text.Box.ToArray(), Text.Text);
    }

    public static InvoiceExtraction Extract(PdfText document)
    {
        var lines = document.Pages
            .SelectMany(p => p.Lines.Select(l => new Line(p.Number, l)))
            .ToList();

        var invoiceNumber = First(lines, InvoiceNumberPattern(), m => m.Groups["v"].Value.TrimEnd('.', ','));
        var invoiceDate = FirstDate(lines, InvoiceDatePattern()) ?? FirstDate(lines, BareDatePattern());
        var dueDate = FirstDate(lines, DueDatePattern());

        var supplierName = Labelled(lines, SupplierLabelPattern());
        var customerName = Labelled(lines, CustomerLabelPattern());
        var (supplierVat, customerVat) = VatNumbers(lines);

        var subtotal = LastAmount(lines.Where(l => SubtotalPattern().IsMatch(l.Value)));
        var vat = LastAmount(lines.Where(l =>
            VatAmountPattern().IsMatch(l.Value) &&
            !VatNumberPattern().IsMatch(l.Value) &&
            !SubtotalPattern().IsMatch(l.Value) &&
            !l.Value.Contains("invoice", StringComparison.OrdinalIgnoreCase)));
        var total = TotalAmount(lines);
        var currency = Currency(lines);

        var used = new HashSet<Line>(new[] { subtotal?.Line, vat?.Line, total?.Line }.OfType<Line>());
        var items = LineItems(lines.Where(l => !used.Contains(l)));

        var missing = new List<string>();
        if (invoiceNumber is null) missing.Add("invoiceNumber");
        if (invoiceDate is null) missing.Add("invoiceDate");
        if (total is null) missing.Add("total");

        var checks = Checks(subtotal?.Field, vat?.Field, total?.Field, items, invoiceDate, dueDate, supplierVat);

        return new InvoiceExtraction(
            invoiceNumber, invoiceDate, dueDate, supplierName, supplierVat, customerName, customerVat, currency,
            subtotal?.Field, vat?.Field, total?.Field, items, checks, missing);
    }

    private static IReadOnlyList<InvoiceCheck> Checks(
        ExtractedField<decimal>? subtotal,
        ExtractedField<decimal>? vat,
        ExtractedField<decimal>? total,
        IReadOnlyList<InvoiceLineItem> items,
        ExtractedField<DateOnly>? invoiceDate,
        ExtractedField<DateOnly>? dueDate,
        ExtractedField<string>? supplierVat)
    {
        var checks = new List<InvoiceCheck>();

        checks.Add(subtotal is not null && vat is not null && total is not null
            ? Math.Abs(subtotal.Value + vat.Value - total.Value) <= 0.01m
                ? new InvoiceCheck("totals_add_up", "pass")
                : new InvoiceCheck("totals_add_up", "fail", $"Subtotal {subtotal.Value} + VAT {vat.Value} = {subtotal.Value + vat.Value}, but total is {total.Value}.")
            : new InvoiceCheck("totals_add_up", "not_evaluated", "Subtotal, VAT and total were not all found."));

        var itemsBase = subtotal?.Value ?? (vat is null ? total?.Value : null);
        checks.Add(items.Count > 0 && itemsBase is { } expected
            ? Math.Abs(items.Sum(i => i.Amount) - expected) <= 0.01m
                ? new InvoiceCheck("line_items_sum", "pass")
                : new InvoiceCheck("line_items_sum", "fail", $"Line items sum to {items.Sum(i => i.Amount)}, not {expected}.")
            : new InvoiceCheck("line_items_sum", "not_evaluated"));

        if (subtotal is { Value: > 0 } && vat is not null)
        {
            var rate = Math.Round(vat.Value / subtotal.Value, 4);
            checks.Add(Math.Abs(rate - StandardVatRate) <= 0.002m
                ? new InvoiceCheck("vat_rate", "pass")
                : new InvoiceCheck("vat_rate", "differs",
                    $"VAT is {rate:P1} of the subtotal; the standard rate is 15%. Zero-rated or mixed supplies can differ legitimately."));
        }
        else
        {
            checks.Add(new InvoiceCheck("vat_rate", "not_evaluated"));
        }

        checks.Add(invoiceDate is not null && dueDate is not null
            ? dueDate.Value >= invoiceDate.Value
                ? new InvoiceCheck("due_after_issue", "pass")
                : new InvoiceCheck("due_after_issue", "fail", "The due date is before the invoice date.")
            : new InvoiceCheck("due_after_issue", "not_evaluated"));

        checks.Add(supplierVat is null
            ? new InvoiceCheck("supplier_vat_format", "not_evaluated")
            : SaVatFormat().IsMatch(supplierVat.Value)
                ? new InvoiceCheck("supplier_vat_format", "pass", "Format only; registration is not verified.")
                : new InvoiceCheck("supplier_vat_format", "fail", "A South African VAT number is ten digits starting with 4."));

        return checks;
    }

    private static ExtractedField<string>? First(IEnumerable<Line> lines, Regex pattern, Func<Match, string> value)
    {
        foreach (var line in lines)
        {
            var match = pattern.Match(line.Value);
            if (match.Success && value(match) is { Length: > 0 } text)
            {
                return new ExtractedField<string>(text, line.Evidence);
            }
        }

        return null;
    }

    private static ExtractedField<DateOnly>? FirstDate(IEnumerable<Line> lines, Regex pattern)
    {
        foreach (var line in lines)
        {
            var match = pattern.Match(line.Value);
            if (match.Success && ParseDate(match.Groups["v"].Value) is { } date)
            {
                return new ExtractedField<DateOnly>(date, line.Evidence);
            }
        }

        return null;
    }

    /// <summary>"Label: value" on one line, or the label alone with the value on the next line.</summary>
    private static ExtractedField<string>? Labelled(IReadOnlyList<Line> lines, Regex pattern)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var match = pattern.Match(lines[i].Value);
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

    private static (ExtractedField<string>? Supplier, ExtractedField<string>? Customer) VatNumbers(IEnumerable<Line> lines)
    {
        ExtractedField<string>? supplier = null, customer = null;

        foreach (var line in lines)
        {
            var match = VatNumberPattern().Match(line.Value);
            if (!match.Success)
            {
                continue;
            }

            var digits = new string(match.Groups["v"].Value.Where(char.IsAsciiDigit).ToArray());
            var field = new ExtractedField<string>(digits, line.Evidence);

            if (CustomerContextPattern().IsMatch(line.Value))
            {
                customer ??= field;
            }
            else if (supplier is null)
            {
                supplier = field;
            }
            else
            {
                customer ??= field;
            }
        }

        return (supplier, customer);
    }

    private sealed record AmountOnLine(ExtractedField<decimal> Field, Line Line);

    private static AmountOnLine? LastAmount(IEnumerable<Line> lines)
    {
        AmountOnLine? found = null;
        foreach (var line in lines)
        {
            if (TrailingAmount(line.Value) is { } amount)
            {
                found = new AmountOnLine(new ExtractedField<decimal>(amount, line.Evidence), line);
            }
        }

        return found;
    }

    /// <summary>Prefers an explicit "Total due/incl/payable", then "Amount/Balance due", then a bare "Total".</summary>
    private static AmountOnLine? TotalAmount(IReadOnlyList<Line> lines)
    {
        var candidates = lines.Where(l => !SubtotalPattern().IsMatch(l.Value)).ToList();

        return LastAmount(candidates.Where(l => ExplicitTotalPattern().IsMatch(l.Value)))
               ?? LastAmount(candidates.Where(l => BareTotalPattern().IsMatch(l.Value)));
    }

    private static ExtractedField<string>? Currency(IEnumerable<Line> lines)
    {
        foreach (var line in lines)
        {
            var match = CurrencyPattern().Match(line.Value);
            if (match.Success)
            {
                var code = match.Value.Trim().ToUpperInvariant() switch
                {
                    "R" or "ZAR" => "ZAR",
                    var other => other
                };
                return new ExtractedField<string>(code, line.Evidence);
            }
        }

        return null;
    }

    private static IReadOnlyList<InvoiceLineItem> LineItems(IEnumerable<Line> lines)
    {
        var items = new List<InvoiceLineItem>();

        foreach (var line in lines)
        {
            var match = LineItemPattern().Match(line.Value);
            if (!match.Success)
            {
                continue;
            }

            var quantity = ParseMoney(match.Groups["qty"].Value);
            var unit = ParseMoney(match.Groups["unit"].Value);
            var amount = ParseMoney(match.Groups["amount"].Value);

            if (quantity is > 0 && unit is not null && amount is not null &&
                Math.Abs(quantity.Value * unit.Value - amount.Value) <= 0.01m)
            {
                items.Add(new InvoiceLineItem(match.Groups["desc"].Value.Trim(), quantity.Value, unit.Value, amount.Value, line.Evidence));
            }
        }

        return items;
    }

    private static decimal? TrailingAmount(string line)
    {
        var match = TrailingAmountPattern().Match(line);
        return match.Success ? ParseMoney(match.Groups["amount"].Value) : null;
    }

    /// <summary>
    /// Parses South African and international amount formats: "R 1 234,56", "R1,234.56", "1234.56",
    /// "1 234.56". With both separators, the last one is the decimal point.
    /// </summary>
    public static decimal? ParseMoney(string raw)
    {
        var text = CurrencySymbolPattern().Replace(raw, string.Empty).Replace(" ", string.Empty).Replace(" ", string.Empty);
        if (text.Length == 0)
        {
            return null;
        }

        var lastComma = text.LastIndexOf(',');
        var lastDot = text.LastIndexOf('.');

        if (lastComma >= 0 && lastDot >= 0)
        {
            text = lastComma > lastDot
                ? text.Replace(".", string.Empty).Replace(',', '.')
                : text.Replace(",", string.Empty);
        }
        else if (lastComma >= 0)
        {
            text = text.Length - lastComma - 1 == 2 && text.Count(c => c == ',') == 1
                ? text.Replace(',', '.')
                : text.Replace(",", string.Empty);
        }

        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "yyyy/MM/dd", "yyyy.MM.dd",
        "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy", "d.M.yyyy", "dd.MM.yyyy",
        "d MMMM yyyy", "d MMM yyyy", "dd MMMM yyyy", "dd MMM yyyy", "MMMM d yyyy", "MMM d yyyy",
    ];

    public static DateOnly? ParseDate(string raw)
    {
        var match = DateTokenPattern().Match(raw);
        if (!match.Success)
        {
            return null;
        }

        var token = match.Value.Replace(",", string.Empty).Replace(".", " ", StringComparison.Ordinal).Trim();
        token = MultiSpace().Replace(token, " ");

        foreach (var candidate in new[] { match.Value.Replace(",", string.Empty).Trim(), token })
        {
            if (DateOnly.TryParseExact(candidate, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return date;
            }
        }

        return null;
    }

    private static bool LooksLikeLabel(string line) => line.TrimEnd().EndsWith(':') || line.Contains(':');

    [GeneratedRegex(@"\b(?:tax\s+)?invoice\s*(?:no\.?|number|num\.?|#|ref(?:erence)?)\s*[:#.]?\s*(?<v>[A-Z0-9][A-Z0-9\-/]{1,30})", RegexOptions.IgnoreCase)]
    private static partial Regex InvoiceNumberPattern();

    [GeneratedRegex(@"\b(?:invoice\s+date|date\s+of\s+issue|issue\s+date|tax\s+(?:invoice\s+)?date)\s*[:.]?\s*(?<v>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex InvoiceDatePattern();

    [GeneratedRegex(@"^\s*date\s*[:.]?\s*(?<v>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex BareDatePattern();

    [GeneratedRegex(@"\b(?:due\s+date|payment\s+due|due\s+by|due\s+on)\s*[:.]?\s*(?<v>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex DueDatePattern();

    [GeneratedRegex(@"^\s*(?:from|supplier|seller|vendor)\s*:\s*(?<v>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex SupplierLabelPattern();

    [GeneratedRegex(@"^\s*(?:bill(?:ed)?\s+to|customer|client|sold\s+to|to)\s*:\s*(?<v>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex CustomerLabelPattern();

    [GeneratedRegex(@"\bvat\s*(?:reg(?:istration)?\.?\s*)?(?:no\.?|number|#)?\s*[:.]?\s*(?<v>\d[\d ]{8,12}\d)\b", RegexOptions.IgnoreCase)]
    private static partial Regex VatNumberPattern();

    [GeneratedRegex(@"\b(?:customer|client|bill(?:ed)?\s+to|buyer|recipient)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CustomerContextPattern();

    [GeneratedRegex(@"\b(?:sub\s*-?\s*total|total\s+excl(?:uding|\.)?(?:\s+vat)?|amount\s+excl(?:uding|\.)?(?:\s+vat)?|nett?\s+amount)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SubtotalPattern();

    [GeneratedRegex(@"^\s*(?:output\s+)?(?:vat|tax)\b", RegexOptions.IgnoreCase)]
    private static partial Regex VatAmountPattern();

    [GeneratedRegex(@"\b(?:total\s+(?:due|payable|incl(?:uding|\.)?(?:\s+vat)?|amount)|amount\s+(?:due|payable)|balance\s+due|grand\s+total)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitTotalPattern();

    [GeneratedRegex(@"^\s*total\b", RegexOptions.IgnoreCase)]
    private static partial Regex BareTotalPattern();

    [GeneratedRegex(@"(?<amount>-?(?:R|ZAR)?\s?\d{1,3}(?:[ ,]\d{3})*[.,]\d{2}|-?(?:R|ZAR)?\s?\d+[.,]\d{2})\s*$")]
    private static partial Regex TrailingAmountPattern();

    [GeneratedRegex(@"^(?<desc>.*?[A-Za-z].*?)\s+(?<qty>\d+(?:[.,]\d+)?)\s+(?<unit>(?:R\s?)?\d{1,3}(?:[ ,]\d{3})*[.,]\d{2}|(?:R\s?)?\d+[.,]\d{2})\s+(?<amount>(?:R\s?)?\d{1,3}(?:[ ,]\d{3})*[.,]\d{2}|(?:R\s?)?\d+[.,]\d{2})\s*$")]
    private static partial Regex LineItemPattern();

    [GeneratedRegex(@"(?<![A-Za-z])(?:ZAR|USD|EUR|GBP)(?![A-Za-z])|(?<![A-Za-z])R(?=\s?\d)")]
    private static partial Regex CurrencyPattern();

    [GeneratedRegex(@"ZAR|R(?=\s?\d)|\$|€|£")]
    private static partial Regex CurrencySymbolPattern();

    [GeneratedRegex(@"\d{4}[-/.]\d{1,2}[-/.]\d{1,2}|\d{1,2}[-/.]\d{1,2}[-/.]\d{4}|\d{1,2}\s+[A-Za-z]{3,9}\.?,?\s+\d{4}|[A-Za-z]{3,9}\.?\s+\d{1,2},?\s+\d{4}")]
    private static partial Regex DateTokenPattern();

    [GeneratedRegex(@"^4\d{9}$")]
    private static partial Regex SaVatFormat();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpace();
}
