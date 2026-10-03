using System.Text.RegularExpressions;
using Helios.Application.Abstractions.Documents;

namespace Helios.Application.Features.Products.Documents;

public sealed record PayslipExtraction(
    ExtractedField<string>? EmployerName,
    ExtractedField<string>? EmployeeName,
    ExtractedField<string>? EmployeeNumber,
    ExtractedField<DateOnly>? PayDate,
    ExtractedField<DateOnly>? PeriodFrom,
    ExtractedField<DateOnly>? PeriodTo,
    ExtractedField<decimal>? GrossPay,
    ExtractedField<decimal>? TotalDeductions,
    ExtractedField<decimal>? NetPay,
    ExtractedField<decimal>? Paye,
    ExtractedField<decimal>? Uif,
    IReadOnlyList<InvoiceCheck> Checks,
    IReadOnlyList<string> MissingRequired)
{
    public bool ReviewRequired => MissingRequired.Count > 0 || Checks.Any(c => c.Outcome == "fail");
}

/// <summary>
/// Deterministic payslip extraction from a PDF text layer: employer and employee (only when labelled),
/// employee number, pay date and period, gross pay, total deductions, net pay, PAYE and UIF, each with
/// the line it came from. Checks: net equals gross minus deductions, and the pay date sits near the
/// pay period. The result does not verify employment or income, and identity numbers printed on a
/// payslip are deliberately not extracted.
/// <para>Known limits (v1): English labels; one employee per document; amounts at the end of their labelled line.</para>
/// </summary>
public static partial class PayslipExtractor
{
    /// <summary>A pay date may fall a little after the period it pays for.</summary>
    public const int PayDateGraceDays = 10;

    public static PayslipExtraction Extract(PdfText document)
    {
        var lines = DocumentText.Lines(document);

        var employer = DocumentText.Labelled(lines, EmployerPattern());
        var employee = DocumentText.Labelled(lines, EmployeePattern());
        var number = EmployeeNumber(lines);
        var payDate = DocumentText.LabelledDate(lines, PayDatePattern());
        var period = DocumentText.LabelledRange(lines, PeriodPattern());

        var gross = DocumentText.LabelledAmount(lines, GrossPattern());
        var deductions = DocumentText.LabelledAmount(lines, DeductionsPattern());
        var net = DocumentText.LabelledAmount(lines, NetPattern());
        var paye = DocumentText.LabelledAmount(lines, PayePattern(), last: false);
        var uif = DocumentText.LabelledAmount(lines, UifPattern(), last: false);

        var missing = new List<string>();
        if (employer is null) missing.Add("employerName");
        if (employee is null) missing.Add("employeeName");
        if (payDate is null) missing.Add("payDate");
        if (gross is null) missing.Add("grossPay");
        if (net is null) missing.Add("netPay");

        var checks = new List<InvoiceCheck>
        {
            gross is not null && deductions is not null && net is not null
                ? Math.Abs(gross.Value - deductions.Value - net.Value) <= 0.01m
                    ? new InvoiceCheck("net_equals_gross_minus_deductions", "pass")
                    : new InvoiceCheck("net_equals_gross_minus_deductions", "fail",
                        $"Gross {gross.Value} - deductions {deductions.Value} = {gross.Value - deductions.Value}, but net pay is {net.Value}.")
                : new InvoiceCheck("net_equals_gross_minus_deductions", "not_evaluated", "Gross, total deductions and net pay were not all found."),

            gross is not null && net is not null && net.Value > gross.Value
                ? new InvoiceCheck("net_not_above_gross", "fail", "Net pay is larger than gross pay.")
                : new InvoiceCheck("net_not_above_gross", gross is null || net is null ? "not_evaluated" : "pass"),

            payDate is not null && period is { } range
                ? payDate.Value >= range.From.Value && payDate.Value <= range.To.Value.AddDays(PayDateGraceDays)
                    ? new InvoiceCheck("pay_date_in_period", "pass")
                    : new InvoiceCheck("pay_date_in_period", "differs", "The pay date is outside the pay period (allowing for payment shortly after it).")
                : new InvoiceCheck("pay_date_in_period", "not_evaluated"),
        };

        return new PayslipExtraction(employer, employee, number, payDate, period?.From, period?.To,
            gross, deductions, net, paye, uif, checks, missing);
    }

    private static ExtractedField<string>? EmployeeNumber(IEnumerable<DocLine> lines)
    {
        foreach (var line in lines)
        {
            var match = EmployeeNumberPattern().Match(line.Value);
            if (match.Success)
            {
                return new ExtractedField<string>(match.Groups["v"].Value, line.Evidence);
            }
        }

        return null;
    }

    [GeneratedRegex(@"^\s*(?:employer|company(?:\s+name)?)\s*:\s*(?<v>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex EmployerPattern();

    [GeneratedRegex(@"^\s*(?:employee(?:\s+name)?|name)\s*:\s*(?<v>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex EmployeePattern();

    [GeneratedRegex(@"\b(?:employee\s+(?:no\.?|number|code)|emp\.?\s*no\.?)\s*:?\s*(?<v>[A-Z0-9][A-Z0-9\-/]{0,19})\b", RegexOptions.IgnoreCase)]
    private static partial Regex EmployeeNumberPattern();

    [GeneratedRegex(@"\b(?:pay(?:ment)?\s+date|date\s+paid|paid\s+on)\s*:?\s*(?<v>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex PayDatePattern();

    [GeneratedRegex(@"\b(?:pay\s+period|period)\s*:?\s*(?<v>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex PeriodPattern();

    [GeneratedRegex(@"\b(?:gross\s+(?:pay|earnings|salary|income|remuneration)|total\s+earnings)\b", RegexOptions.IgnoreCase)]
    private static partial Regex GrossPattern();

    [GeneratedRegex(@"\btotal\s+deductions\b", RegexOptions.IgnoreCase)]
    private static partial Regex DeductionsPattern();

    [GeneratedRegex(@"\b(?:nett?\s+(?:pay|salary|amount|income|remuneration)|take[\s-]?home\s+pay)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NetPattern();

    [GeneratedRegex(@"^\s*(?:paye|income\s+tax|tax\s*\(paye\))\b", RegexOptions.IgnoreCase)]
    private static partial Regex PayePattern();

    [GeneratedRegex(@"^\s*uif\b", RegexOptions.IgnoreCase)]
    private static partial Regex UifPattern();
}

/// <summary><c>documents.payslip</c> v1 over the PDF text layer.</summary>
public sealed class PayslipExecutor(IPdfTextReader reader) : TextLayerProduct(reader)
{
    public const string Slug = "documents.payslip";

    public override string ProductSlug => Slug;
    public override int MaxPages => 5;

    public override string Notice =>
        "Values are read from labelled lines of the PDF's text layer. Checks test the extracted arithmetic only. " +
        "This does not verify employment or income and does not establish that the payslip is authentic.";

    protected override DocumentExtraction Extract(PdfText text)
    {
        var x = PayslipExtractor.Extract(text);

        var body = new
        {
            fields = new
            {
                employerName = DocumentText.Field(x.EmployerName),
                employeeName = DocumentText.Field(x.EmployeeName),
                employeeNumber = DocumentText.Field(x.EmployeeNumber),
                payDate = DocumentText.DateField(x.PayDate),
                periodFrom = DocumentText.DateField(x.PeriodFrom),
                periodTo = DocumentText.DateField(x.PeriodTo),
                grossPay = DocumentText.Field(x.GrossPay),
                totalDeductions = DocumentText.Field(x.TotalDeductions),
                netPay = DocumentText.Field(x.NetPay),
                paye = DocumentText.Field(x.Paye),
                uif = DocumentText.Field(x.Uif)
            },
            checks = x.Checks.Select(DocumentText.CheckJson),
            missingRequired = x.MissingRequired
        };

        return new DocumentExtraction(body, StandardWarnings(x.MissingRequired, x.Checks), x.ReviewRequired);
    }
}
