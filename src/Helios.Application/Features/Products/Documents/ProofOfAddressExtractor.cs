using System.Text.RegularExpressions;
using Helios.Application.Abstractions.Documents;

namespace Helios.Application.Features.Products.Documents;

public sealed record ProofOfAddressExtraction(
    ExtractedField<string>? Issuer,
    ExtractedField<string>? AccountHolder,
    ExtractedField<IReadOnlyList<string>>? Address,
    ExtractedField<string>? PostalCode,
    ExtractedField<DateOnly>? DocumentDate,
    ExtractedField<string>? AccountNumberMasked,
    IReadOnlyList<InvoiceCheck> Checks,
    IReadOnlyList<string> MissingRequired)
{
    public bool ReviewRequired => MissingRequired.Count > 0 || Checks.Any(c => c.Outcome is "fail" or "differs");
}

/// <summary>
/// Deterministic proof-of-address extraction from a PDF text layer (utility, municipal, telecom or
/// bank correspondence): issuer, account holder and address only where labelled, the address block
/// that follows an "Address:" label, its postal code, the document date and a masked account number.
/// Checks: the document date is not in the future and is recent (within
/// <see cref="RecentDays"/> days of processing; older is "differs", because acceptable age is the
/// customer's own policy). Extraction is not authoritative address verification.
/// <para>Known limits (v1): English labels; South African four-digit postal codes; one addressee.</para>
/// </summary>
public static partial class ProofOfAddressExtractor
{
    public const int RecentDays = 92;
    public const int MaxAddressLines = 6;

    public static ProofOfAddressExtraction Extract(PdfText document, DateOnly today)
    {
        var lines = DocumentText.Lines(document);

        var issuer = DocumentText.Labelled(lines, IssuerPattern());
        var holder = DocumentText.Labelled(lines, HolderPattern());
        var address = AddressBlock(lines);
        var postal = address is null ? null : PostalCode(address);
        var date = DocumentText.LabelledDate(lines, DocumentDatePattern()) ?? DocumentText.LabelledDate(lines, BareDatePattern());
        var account = AccountNumber(lines);

        var missing = new List<string>();
        if (holder is null) missing.Add("accountHolder");
        if (address is null) missing.Add("address");
        if (date is null) missing.Add("documentDate");

        var checks = new List<InvoiceCheck>();

        if (date is null)
        {
            checks.Add(new InvoiceCheck("not_future_dated", "not_evaluated"));
            checks.Add(new InvoiceCheck("recent", "not_evaluated"));
        }
        else
        {
            var age = today.DayNumber - date.Value.DayNumber;
            checks.Add(age < -1
                ? new InvoiceCheck("not_future_dated", "fail", "The document date is in the future.")
                : new InvoiceCheck("not_future_dated", "pass"));
            checks.Add(age <= RecentDays
                ? new InvoiceCheck("recent", "pass", $"Dated {age} day(s) before processing.")
                : new InvoiceCheck("recent", "differs", $"Dated {age} days before processing; check against your own acceptance policy."));
        }

        checks.Add(postal is null
            ? new InvoiceCheck("postal_code_present", address is null ? "not_evaluated" : "differs", "No four-digit postal code ends the address.")
            : new InvoiceCheck("postal_code_present", "pass"));

        return new ProofOfAddressExtraction(issuer, holder, address, postal, date, account, checks, missing);
    }

    /// <summary>
    /// The text after an address label, plus the unlabelled lines directly below it on the same page,
    /// up to a line ending in a postal code or <see cref="MaxAddressLines"/> lines.
    /// </summary>
    private static ExtractedField<IReadOnlyList<string>>? AddressBlock(IReadOnlyList<DocLine> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var match = AddressLabelPattern().Match(lines[i].Value);
            if (!match.Success)
            {
                continue;
            }

            var block = new List<DocLine>();
            var first = match.Groups["v"].Value.Trim();
            if (first.Length > 0)
            {
                block.Add(lines[i]);
            }

            for (var j = i + 1; j < lines.Count && block.Count < MaxAddressLines; j++)
            {
                if (lines[j].Page != lines[i].Page || DocumentText.LooksLikeLabel(lines[j].Value) ||
                    (block.Count > 0 && EndsInPostalCode(block[^1].Value)))
                {
                    break;
                }

                block.Add(lines[j]);
            }

            if (block.Count == 0)
            {
                continue;
            }

            var texts = block.Select((l, k) => k == 0 && first.Length > 0 ? first : l.Value.Trim()).ToList();
            var box = new[]
            {
                block.Min(l => l.Text.Box.Left), block.Min(l => l.Text.Box.Top),
                block.Max(l => l.Text.Box.Right), block.Max(l => l.Text.Box.Bottom)
            }.Select(v => Math.Round(v, 4)).ToArray();

            return new ExtractedField<IReadOnlyList<string>>(texts, new Evidence(lines[i].Page, box, string.Join(" | ", block.Select(l => l.Value))));
        }

        return null;
    }

    private static ExtractedField<string>? PostalCode(ExtractedField<IReadOnlyList<string>> address)
    {
        var match = PostalCodePattern().Match(address.Value[^1]);
        return match.Success ? new ExtractedField<string>(match.Groups["code"].Value, address.Evidence) : null;
    }

    private static bool EndsInPostalCode(string line) => PostalCodePattern().IsMatch(line);

    private static ExtractedField<string>? AccountNumber(IEnumerable<DocLine> lines)
    {
        foreach (var line in lines)
        {
            var match = AccountNumberPattern().Match(line.Value);
            if (match.Success)
            {
                var masked = DocumentText.MaskAccountNumber(match.Groups["v"].Value);
                return new ExtractedField<string>(masked,
                    line.Evidence with { Line = line.Value.Replace(match.Groups["v"].Value, masked, StringComparison.Ordinal) });
            }
        }

        return null;
    }

    [GeneratedRegex(@"^\s*(?:issued\s+by|issuer|service\s+provider|supplier|from)\s*:\s*(?<v>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex IssuerPattern();

    [GeneratedRegex(@"^\s*(?:account\s+holder|account\s+name|customer(?:\s+name)?|consumer(?:\s+name)?|resident|name)\s*:\s*(?<v>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex HolderPattern();

    [GeneratedRegex(@"^\s*(?:(?:postal|physical|residential|service|street|billing|delivery|property)\s+)?address\s*:\s*(?<v>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex AddressLabelPattern();

    [GeneratedRegex(@"(?:^|[\s,])(?<code>\d{4})\s*$")]
    private static partial Regex PostalCodePattern();

    [GeneratedRegex(@"\b(?:statement\s+date|invoice\s+date|bill(?:ing)?\s+date|issue\s+date|date\s+of\s+issue|document\s+date|letter\s+date)\s*:?\s*(?<v>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex DocumentDatePattern();

    // "Date: …" or "Date 01/…", but not "Date of birth" or "Date due".
    [GeneratedRegex(@"^\s*date(?:\s*:\s*|\s+(?=\d))(?<v>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex BareDatePattern();

    [GeneratedRegex(@"\b(?:account\s+(?:number|no\.?)|acc(?:ount)?\.?\s*no\.?)\s*:?\s*(?<v>\d[\d \-]{3,22}\d)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AccountNumberPattern();
}

/// <summary><c>documents.proof-of-address</c> v1 over the PDF text layer.</summary>
public sealed class ProofOfAddressExecutor(IPdfTextReader reader, TimeProvider clock) : TextLayerProduct(reader)
{
    public const string Slug = "documents.proof-of-address";

    public override string ProductSlug => Slug;
    public override int MaxPages => 5;

    public override string Notice =>
        "Name, address and date are read from labelled lines of the PDF's text layer. This is not authoritative address " +
        "verification, does not establish that the document is authentic, and does not decide whether it satisfies any " +
        "regulatory requirement; recency is reported against the processing date for your own policy.";

    protected override DocumentExtraction Extract(PdfText text)
    {
        var x = ProofOfAddressExtractor.Extract(text, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));

        var body = new
        {
            fields = new
            {
                issuer = DocumentText.Field(x.Issuer),
                accountHolder = DocumentText.Field(x.AccountHolder),
                address = DocumentText.Field(x.Address),
                postalCode = DocumentText.Field(x.PostalCode),
                documentDate = DocumentText.DateField(x.DocumentDate),
                accountNumberMasked = DocumentText.Field(x.AccountNumberMasked)
            },
            checks = x.Checks.Select(DocumentText.CheckJson),
            missingRequired = x.MissingRequired
        };

        return new DocumentExtraction(body, StandardWarnings(x.MissingRequired, x.Checks), x.ReviewRequired);
    }
}
