using System.Text.RegularExpressions;
using Helios.Application.Abstractions.Documents;

namespace Helios.Application.Features.Products.Documents;

/// <param name="Amount">Positive for money in, negative for money out; null when the direction could not be established.</param>
/// <param name="Direction"><c>credit</c>, <c>debit</c> or null when neither a printed marker nor the running balance decides it.</param>
public sealed record StatementTransaction(
    DateOnly Date,
    string Description,
    decimal RawAmount,
    decimal? Amount,
    string? Direction,
    decimal? Balance,
    Evidence Evidence);

public sealed record BankStatementExtraction(
    ExtractedField<string>? Institution,
    ExtractedField<string>? AccountHolder,
    ExtractedField<string>? AccountNumberMasked,
    ExtractedField<DateOnly>? PeriodFrom,
    ExtractedField<DateOnly>? PeriodTo,
    ExtractedField<decimal>? OpeningBalance,
    ExtractedField<decimal>? ClosingBalance,
    IReadOnlyList<StatementTransaction> Transactions,
    IReadOnlyList<InvoiceCheck> Checks,
    IReadOnlyList<string> MissingRequired)
{
    public bool ReviewRequired =>
        MissingRequired.Count > 0 || Checks.Any(c => c.Outcome == "fail") || Transactions.Any(t => t.Direction is null);
}

/// <summary>
/// Deterministic bank statement extraction from a PDF text layer: institution (only when its name is
/// printed above the transactions), account holder, masked account number, period, opening and
/// closing balances, and dated transactions with amounts and running balances. The direction of each
/// transaction comes from a printed sign or Cr/Dr marker, or from the running balance; when neither
/// decides it, the direction is null and the statement is flagged, never guessed.
/// <para>
/// Checks reconcile the arithmetic: each running balance follows from the previous one, opening plus
/// movements equals closing, and every transaction falls inside the period. These are checks on the
/// extracted values — not affordability, credit or fraud decisions.
/// </para>
/// <para>
/// Known limits (v1): English labels; one account per document; a transaction is one line beginning
/// with its date; a description ending in a number directly before the amount can be misread (the
/// balance checks then fail and flag the statement).
/// </para>
/// </summary>
public static partial class BankStatementExtractor
{
    private static readonly (string Label, Regex Pattern)[] Institutions =
    [
        ("ABSA", new Regex(@"\bABSA\b", RegexOptions.IgnoreCase)),
        ("Standard Bank", new Regex(@"\bStandard\s+Bank\b", RegexOptions.IgnoreCase)),
        ("FNB", new Regex(@"\b(?:FNB|First\s+National\s+Bank)\b", RegexOptions.IgnoreCase)),
        ("Nedbank", new Regex(@"\bNedbank\b", RegexOptions.IgnoreCase)),
        ("Capitec", new Regex(@"\bCapitec\b", RegexOptions.IgnoreCase)),
        ("Investec", new Regex(@"\bInvestec\b", RegexOptions.IgnoreCase)),
        ("African Bank", new Regex(@"\bAfrican\s+Bank\b", RegexOptions.IgnoreCase)),
        ("TymeBank", new Regex(@"\bTyme\s?Bank\b", RegexOptions.IgnoreCase)),
        ("Discovery Bank", new Regex(@"\bDiscovery\s+Bank\b", RegexOptions.IgnoreCase)),
    ];

    public static BankStatementExtraction Extract(PdfText document)
    {
        var lines = DocumentText.Lines(document);

        var holder = DocumentText.Labelled(lines, HolderPattern());
        var account = AccountNumber(lines);
        var period = DocumentText.LabelledRange(lines, PeriodPattern()) ?? DocumentText.LabelledRange(lines, FromToPattern());
        var opening = DocumentText.LabelledAmount(lines, OpeningPattern(), last: false);
        var closing = DocumentText.LabelledAmount(lines, ClosingPattern());

        var transactions = Transactions(lines, opening?.Value, period?.From.Value, period?.To.Value, out var firstTransactionIndex);
        var institution = Institution(lines.Take(firstTransactionIndex < 0 ? lines.Count : firstTransactionIndex));

        var missing = new List<string>();
        if (period is null) missing.Add("statementPeriod");
        if (opening is null) missing.Add("openingBalance");
        if (closing is null) missing.Add("closingBalance");
        if (transactions.Count == 0) missing.Add("transactions");

        var checks = Checks(transactions, opening, closing, period);

        return new BankStatementExtraction(institution, holder, account, period?.From, period?.To, opening, closing,
            transactions, checks, missing);
    }

    private static List<StatementTransaction> Transactions(
        IReadOnlyList<DocLine> lines, decimal? opening, DateOnly? from, DateOnly? to, out int firstIndex)
    {
        var transactions = new List<StatementTransaction>();
        var previous = opening;
        firstIndex = -1;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var match = TransactionPattern().Match(line.Value);
            if (!match.Success || OpeningPattern().IsMatch(line.Value) || ClosingPattern().IsMatch(line.Value))
            {
                continue;
            }

            if (TransactionDate(match.Groups["date"].Value, from, to) is not { } date)
            {
                continue;
            }

            var (amounts, rest) = DocumentText.TrailingAmounts(match.Groups["rest"].Value, 2);
            if (amounts.Count == 0 || rest.Length == 0)
            {
                continue;
            }

            var raw = amounts[0];
            decimal? balance = amounts.Count == 2 ? DocumentText.Signed(amounts[1]) : null;

            string? direction = raw.Marker switch
            {
                "CR" => "credit",
                "DR" or "-" => "debit",
                _ => null
            };

            if (direction is null && balance is { } b && previous is { } p)
            {
                if (Math.Abs(p + raw.Value - b) <= 0.01m)
                {
                    direction = "credit";
                }
                else if (Math.Abs(p - raw.Value - b) <= 0.01m)
                {
                    direction = "debit";
                }
            }

            decimal? signed = direction switch
            {
                "credit" => raw.Value,
                "debit" => -raw.Value,
                _ => null
            };

            transactions.Add(new StatementTransaction(date, rest, raw.Value, signed, direction, balance, line.Evidence));
            if (firstIndex < 0)
            {
                firstIndex = i;
            }

            // The running balance continues from what the statement printed, not from our sum.
            previous = balance ?? (previous is { } prev && signed is { } s ? prev + s : null);
        }

        return transactions;
    }

    /// <summary>
    /// A full date, or "dd Mon" without a year when the period says which year it is (statements
    /// often omit it). Without a period a year-less date is not read.
    /// </summary>
    private static DateOnly? TransactionDate(string token, DateOnly? from, DateOnly? to)
    {
        if (InvoiceFieldExtractor.ParseDate(token) is { } full)
        {
            return full;
        }

        if (from is not { } start || to is not { } end)
        {
            return null;
        }

        foreach (var year in new[] { start.Year, end.Year }.Distinct())
        {
            if (InvoiceFieldExtractor.ParseDate($"{token} {year}") is { } candidate && candidate >= start && candidate <= end)
            {
                return candidate;
            }
        }

        return null;
    }

    private static IReadOnlyList<InvoiceCheck> Checks(
        IReadOnlyList<StatementTransaction> transactions,
        ExtractedField<decimal>? opening,
        ExtractedField<decimal>? closing,
        (ExtractedField<DateOnly> From, ExtractedField<DateOnly> To)? period)
    {
        var checks = new List<InvoiceCheck>();

        // Each printed running balance must follow from the one before it.
        var previous = opening?.Value;
        var evaluated = 0;
        InvoiceCheck? continuity = null;

        foreach (var t in transactions)
        {
            if (previous is { } p && t.Balance is { } balance)
            {
                evaluated++;
                if (t.Amount is { } amount)
                {
                    if (Math.Abs(p + amount - balance) > 0.01m && continuity is null)
                    {
                        continuity = new InvoiceCheck("balance_continuity", "fail",
                            $"On {t.Date:yyyy-MM-dd} ('{t.Description}'), {p} {(amount < 0 ? "-" : "+")} {Math.Abs(amount)} does not give the printed balance {balance}.");
                    }
                }
                else
                {
                    // Neither adding nor subtracting the amount gives the printed balance.
                    continuity ??= new InvoiceCheck("balance_continuity", "fail",
                        $"On {t.Date:yyyy-MM-dd} ('{t.Description}'), {t.RawAmount} from {p} does not give the printed balance {balance} either way. A line may be missing or misread.");
                }
            }

            previous = t.Balance ?? (previous is { } prev && t.Amount is { } a ? prev + a : null);
        }

        checks.Add(continuity ?? (evaluated > 0
            ? new InvoiceCheck("balance_continuity", "pass")
            : new InvoiceCheck("balance_continuity", "not_evaluated", "No running balances could be followed.")));

        checks.Add(opening is not null && closing is not null && transactions.Count > 0 && transactions.All(t => t.Amount is not null)
            ? Math.Abs(opening.Value + transactions.Sum(t => t.Amount!.Value) - closing.Value) <= 0.01m
                ? new InvoiceCheck("closing_balance_reconciles", "pass")
                : new InvoiceCheck("closing_balance_reconciles", "fail",
                    $"Opening {opening.Value} plus movements {transactions.Sum(t => t.Amount!.Value)} is not the closing balance {closing.Value}. A transaction may be missing or misread.")
            : new InvoiceCheck("closing_balance_reconciles", "not_evaluated"));

        if (period is { } range && transactions.Count > 0)
        {
            var outside = transactions.FirstOrDefault(t => t.Date < range.From.Value || t.Date > range.To.Value);
            checks.Add(outside is null
                ? new InvoiceCheck("dates_within_period", "pass")
                : new InvoiceCheck("dates_within_period", "fail", $"{outside.Date:yyyy-MM-dd} is outside the statement period."));
        }
        else
        {
            checks.Add(new InvoiceCheck("dates_within_period", "not_evaluated"));
        }

        checks.Add(period is { } p2 && p2.To.Value < p2.From.Value
            ? new InvoiceCheck("period_order", "fail", "The period ends before it starts.")
            : new InvoiceCheck("period_order", period is null ? "not_evaluated" : "pass"));

        return checks;
    }

    private static ExtractedField<string>? Institution(IEnumerable<DocLine> header)
    {
        foreach (var line in header)
        {
            foreach (var (label, pattern) in Institutions)
            {
                if (pattern.IsMatch(line.Value))
                {
                    return new ExtractedField<string>(label, line.Evidence);
                }
            }
        }

        return null;
    }

    private static ExtractedField<string>? AccountNumber(IEnumerable<DocLine> lines)
    {
        foreach (var line in lines)
        {
            var match = AccountNumberPattern().Match(line.Value);
            if (match.Success)
            {
                // Evidence keeps the position but not the full number: the source text is masked too.
                var masked = DocumentText.MaskAccountNumber(match.Groups["v"].Value);
                var evidence = line.Evidence with { Line = line.Value.Replace(match.Groups["v"].Value, masked, StringComparison.Ordinal) };
                return new ExtractedField<string>(masked, evidence);
            }
        }

        return null;
    }

    [GeneratedRegex(@"^\s*(?:account\s+holder|account\s+name|name\s+of\s+account\s+holder)\s*:\s*(?<v>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex HolderPattern();

    [GeneratedRegex(@"\b(?:account\s+(?:number|no\.?)|acc(?:ount)?\.?\s*no\.?)\s*:?\s*(?<v>\d[\d \-]{4,22}\d)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AccountNumberPattern();

    [GeneratedRegex(@"\b(?:statement\s+period|period)\s*:?\s*(?<v>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex PeriodPattern();

    [GeneratedRegex(@"^\s*(?:statement\s+)?from\s*:?\s*(?<v>.+\bto\b.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex FromToPattern();

    [GeneratedRegex(@"\b(?:opening\s+balance|balance\s+brought\s+forward|balance\s+b/?f)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OpeningPattern();

    [GeneratedRegex(@"\b(?:closing\s+balance|balance\s+carried\s+forward|balance\s+c/?f)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ClosingPattern();

    [GeneratedRegex(@"^\s*(?<date>\d{4}[-/.]\d{1,2}[-/.]\d{1,2}|\d{1,2}[-/.]\d{1,2}[-/.]\d{4}|\d{1,2}\s+[A-Za-z]{3,9}\.?(?:\s+\d{4})?)\s+(?<rest>.+)$")]
    private static partial Regex TransactionPattern();
}

/// <summary><c>documents.bank-statement</c> v1 over the PDF text layer.</summary>
public sealed class BankStatementExecutor(IPdfTextReader reader) : TextLayerProduct(reader)
{
    public const string Slug = "documents.bank-statement";

    public override string ProductSlug => Slug;
    public override int MaxPages => 20;

    public override string Notice =>
        "Values are read from the PDF's text layer. Account numbers are masked to their last four digits. Arithmetic " +
        "checks test the extracted values only; this is not an affordability, credit or fraud assessment and does not " +
        "establish that the statement is authentic.";

    protected override DocumentExtraction Extract(PdfText text)
    {
        var x = BankStatementExtractor.Extract(text);

        var warnings = StandardWarnings(x.MissingRequired, x.Checks);
        var undecided = x.Transactions.Count(t => t.Direction is null);
        if (undecided > 0)
        {
            warnings.Add($"{undecided} transaction(s) have no printed sign and no running balance to decide debit or credit.");
        }

        var body = new
        {
            fields = new
            {
                institution = DocumentText.Field(x.Institution),
                accountHolder = DocumentText.Field(x.AccountHolder),
                accountNumberMasked = DocumentText.Field(x.AccountNumberMasked),
                periodFrom = DocumentText.DateField(x.PeriodFrom),
                periodTo = DocumentText.DateField(x.PeriodTo),
                openingBalance = DocumentText.Field(x.OpeningBalance),
                closingBalance = DocumentText.Field(x.ClosingBalance)
            },
            transactions = x.Transactions.Select(t => new
            {
                date = t.Date.ToString("yyyy-MM-dd"),
                description = t.Description,
                amount = t.Amount,
                printedAmount = t.RawAmount,
                direction = t.Direction,
                balance = t.Balance,
                evidence = DocumentText.EvidenceJson(t.Evidence)
            }),
            summary = new
            {
                transactionCount = x.Transactions.Count,
                totalCredits = x.Transactions.Where(t => t.Direction == "credit").Sum(t => t.RawAmount),
                totalDebits = x.Transactions.Where(t => t.Direction == "debit").Sum(t => t.RawAmount)
            },
            checks = x.Checks.Select(DocumentText.CheckJson),
            missingRequired = x.MissingRequired
        };

        return new DocumentExtraction(body, warnings, x.ReviewRequired);
    }
}
