using System.Text.RegularExpressions;
using Helios.Application.Abstractions.Documents;

namespace Helios.Application.Features.Products.Documents;

public sealed record ClassIndicator(string Name, Evidence Evidence);

public sealed record ClassCandidate(string Type, int Score, IReadOnlyList<ClassIndicator> Indicators);

/// <param name="Type">The matched type, or <c>unknown</c> when the evidence does not clearly favour one.</param>
/// <param name="Decision"><c>matched</c> or <c>uncertain</c>.</param>
public sealed record Classification(string Type, string Decision, IReadOnlyList<ClassCandidate> Candidates)
{
    public bool ReviewRequired => Decision != "matched";
}

/// <summary>
/// Rule-based document type detection over a PDF text layer, for routing documents to the right
/// extractor. Each type has named indicators — distinctive labels and phrases — and scores one point
/// per distinct indicator found, with the line that matched as evidence. A type is chosen only when it
/// has at least <see cref="MinimumScore"/> indicators and leads the next type by
/// <see cref="MinimumLead"/>; otherwise the answer is <c>unknown</c> and flagged for review. Scores
/// are counts of evidence, not probabilities.
/// </summary>
public static class DocumentClassifier
{
    public const int MinimumScore = 3;
    public const int MinimumLead = 2;

    private static Regex R(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly (string Type, (string Name, Regex Pattern)[] Indicators)[] Types =
    [
        ("invoice",
        [
            ("tax_invoice_heading", R(@"\btax\s+invoice\b")),
            ("invoice_number", R(@"\binvoice\s*(?:no\.?|number|#)")),
            ("invoice_date", R(@"\binvoice\s+date\b")),
            ("bill_to", R(@"\bbill(?:ed)?\s+to\b")),
            ("subtotal", R(@"\bsub\s*-?\s*total\b")),
            ("amount_due", R(@"\b(?:total|amount|balance)\s+due\b")),
            ("supplier_vat_number", R(@"\bvat\s*(?:reg(?:istration)?\.?\s*)?(?:no\.?|number)\b")),
        ]),
        ("bank_statement",
        [
            ("statement_period", R(@"\bstatement\s+period\b")),
            ("opening_balance", R(@"\b(?:opening\s+balance|balance\s+brought\s+forward)\b")),
            ("closing_balance", R(@"\b(?:closing\s+balance|balance\s+carried\s+forward)\b")),
            ("bank_statement_heading", R(@"\bbank\s+statement\b")),
            ("branch_code", R(@"\bbranch\s+code\b")),
            ("transaction_columns", R(@"\b(?:debit|withdrawals?)\b.*\b(?:credit|deposits?)\b")),
            ("available_balance", R(@"\bavailable\s+balance\b")),
        ]),
        ("payslip",
        [
            ("payslip_heading", R(@"\b(?:pay\s*slip|salary\s+advice|remuneration\s+advice)\b")),
            ("gross_pay", R(@"\bgross\s+(?:pay|earnings|salary|remuneration)\b")),
            ("net_pay", R(@"\bnett?\s+(?:pay|salary)\b")),
            ("paye", R(@"\bpaye\b")),
            ("uif", R(@"\buif\b")),
            ("employee_number", R(@"\bemployee\s+(?:no\.?|number|code)\b")),
            ("total_deductions", R(@"\btotal\s+deductions\b")),
        ]),
        ("proof_of_address",
        [
            ("municipal_account", R(@"\bmunicipal(?:ity)?\b")),
            ("utility_service", R(@"\b(?:electricity|water\s+and\s+sanitation|refuse|property\s+rates)\b")),
            ("meter_reading", R(@"\bmeter\b")),
            ("service_or_physical_address", R(@"\b(?:service|physical|residential|postal)\s+address\b")),
            ("account_holder", R(@"\baccount\s+holder\b")),
            ("confirmation_of_residence", R(@"\bconfirmation\s+of\s+(?:residence|address)\b")),
        ]),
        ("identity_document",
        [
            ("republic_heading", R(@"\brepublic\s+of\s+south\s+africa\b")),
            ("identity_heading", R(@"\bidentity\s+(?:document|card|number)\b")),
            ("date_of_birth", R(@"\bdate\s+of\s+birth\b")),
            ("country_of_birth", R(@"\bcountry\s+of\s+birth\b")),
            ("nationality", R(@"\bnationality\b")),
            ("surname_field", R(@"^\s*surname\b")),
        ]),
    ];

    public static IReadOnlyList<string> KnownTypes => Types.Select(t => t.Type).ToList();

    public static Classification Classify(PdfText document)
    {
        var lines = DocumentText.Lines(document);

        var candidates = Types
            .Select(type =>
            {
                var found = new List<ClassIndicator>();
                foreach (var (name, pattern) in type.Indicators)
                {
                    var line = lines.FirstOrDefault(l => pattern.IsMatch(l.Value));
                    if (line is not null)
                    {
                        found.Add(new ClassIndicator(name, line.Evidence));
                    }
                }

                return new ClassCandidate(type.Type, found.Count, found);
            })
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Type, StringComparer.Ordinal)
            .ToList();

        var top = candidates[0];
        var second = candidates.Count > 1 ? candidates[1].Score : 0;
        var matched = top.Score >= MinimumScore && top.Score - second >= MinimumLead;

        return new Classification(matched ? top.Type : "unknown", matched ? "matched" : "uncertain",
            candidates.Where(c => c.Score > 0).ToList());
    }
}

/// <summary><c>documents.classify</c> v1: rules over the PDF text layer.</summary>
public sealed class DocumentClassifyExecutor(IPdfTextReader reader) : TextLayerProduct(reader)
{
    public const string Slug = "documents.classify";

    public override string ProductSlug => Slug;
    public override int MaxPages => 50;

    public override string Notice =>
        "The type is chosen by counting distinctive labels found in the PDF's text layer; scores are counts of evidence, " +
        "not probabilities. Unclear documents are returned as 'unknown'. This does not establish that a document is authentic.";

    protected override DocumentExtraction Extract(PdfText text)
    {
        var x = DocumentClassifier.Classify(text);

        var warnings = x.ReviewRequired
            ? new List<string> { "The document does not clearly match one supported type." }
            : [];

        var body = new
        {
            type = x.Type,
            decision = x.Decision,
            supportedTypes = DocumentClassifier.KnownTypes,
            candidates = x.Candidates.Select(c => new
            {
                type = c.Type,
                score = c.Score,
                indicators = c.Indicators.Select(i => new { name = i.Name, evidence = DocumentText.EvidenceJson(i.Evidence) })
            })
        };

        return new DocumentExtraction(body, warnings, x.ReviewRequired);
    }
}
