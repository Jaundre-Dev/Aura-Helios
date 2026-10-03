using Helios.Application.Abstractions.Documents;
using Helios.Application.Features.Products.Documents;

namespace Helios.UnitTests;

/// <summary>
/// The v1 statement, payslip, proof-of-address and classification extractors on synthetic text
/// layers: values come only from labelled lines, arithmetic is checked, undecidable values stay
/// null and flagged, and account numbers never leave unmasked.
/// </summary>
public class DocumentExtractorsTests
{
    private static PdfText Doc(params string[] lines) =>
        new([new PdfTextPage(1, 595, 842, lines.Select((text, i) =>
            new PdfTextLine(text, new TextBox(0.1, 0.05 + (i * 0.02), 0.9, 0.065 + (i * 0.02)), [])).ToList())]);

    // Shared parsing

    [Theory]
    [InlineData("Salary ACME 25 000.00 30 000.00", 2, "Salary ACME", 25000, null, 30000d)]
    [InlineData("Card purchase 120.50- 4 879.50", 2, "Card purchase", 120.50, "-", 4879.50)]
    [InlineData("Debit order 300.00 Dr", 1, "Debit order", 300, "DR", null)]
    [InlineData("Transfer in R 1 500,00 Cr", 1, "Transfer in", 1500, "CR", null)]
    public void Trailing_amounts_keep_their_markers(string line, int max, string before, double first, string? marker, double? second)
    {
        var (amounts, rest) = DocumentText.TrailingAmounts(line, max);

        Assert.Equal(before, rest);
        Assert.Equal((decimal)first, amounts[0].Value);
        Assert.Equal(marker, amounts[0].Marker);
        if (second is { } s)
        {
            Assert.Equal((decimal)s, amounts[1].Value);
        }
    }

    [Theory]
    [InlineData("62123456789", "*******6789")]
    [InlineData("6212 3456 789", "*******6789")]
    [InlineData("123", "***")]
    public void Account_numbers_keep_only_the_last_four_digits(string raw, string masked)
    {
        Assert.Equal(masked, DocumentText.MaskAccountNumber(raw));
    }

    // Bank statements

    private static readonly string[] Statement =
    [
        "Example Bank of Tests — not a real bank",
        "FNB Cheque Account Statement",
        "Account Holder: Sample Trading CC",
        "Account Number: 62123456789",
        "Statement Period: 01/08/2026 to 31/08/2026",
        "Opening Balance 5 000.00",
        "01/08/2026 Salary ACME 25 000.00 30 000.00",
        "03/08/2026 Card purchase 120.50 29 879.50",
        "15/08/2026 Debit order insurer 879.50 29 000.00",
        "Closing Balance 29 000.00",
    ];

    [Fact]
    public void A_statement_reconciles_and_directions_come_from_the_running_balance()
    {
        var x = BankStatementExtractor.Extract(Doc(Statement));

        Assert.Equal("FNB", x.Institution!.Value);
        Assert.Equal("Sample Trading CC", x.AccountHolder!.Value);
        Assert.Equal("*******6789", x.AccountNumberMasked!.Value);
        Assert.DoesNotContain("62123456789", x.AccountNumberMasked.Evidence.Line);
        Assert.Equal(new DateOnly(2026, 8, 1), x.PeriodFrom!.Value);
        Assert.Equal(new DateOnly(2026, 8, 31), x.PeriodTo!.Value);
        Assert.Equal(5000m, x.OpeningBalance!.Value);
        Assert.Equal(29000m, x.ClosingBalance!.Value);

        Assert.Equal(3, x.Transactions.Count);
        Assert.Equal(("credit", 25000m), (x.Transactions[0].Direction, x.Transactions[0].Amount!.Value));
        Assert.Equal(("debit", -120.50m), (x.Transactions[1].Direction, x.Transactions[1].Amount!.Value));
        Assert.Equal("Debit order insurer", x.Transactions[2].Description);

        Assert.All(x.Checks, c => Assert.Equal("pass", c.Outcome));
        Assert.Empty(x.MissingRequired);
        Assert.False(x.ReviewRequired);
    }

    [Fact]
    public void A_missing_transaction_fails_reconciliation()
    {
        var lines = Statement.Where(l => !l.Contains("Card purchase")).ToArray();
        var x = BankStatementExtractor.Extract(Doc(lines));

        Assert.Equal("fail", x.Checks.Single(c => c.Name == "balance_continuity").Outcome);
        Assert.True(x.ReviewRequired);
    }

    [Fact]
    public void Without_a_marker_or_running_balance_the_direction_is_not_guessed()
    {
        var x = BankStatementExtractor.Extract(Doc(
            "Statement Period: 01/08/2026 to 31/08/2026",
            "Opening Balance 100.00",
            "02/08/2026 Mystery movement 40.00",
            "05/08/2026 Refund 10.00 Cr",
            "Closing Balance 70.00"));

        Assert.Null(x.Transactions[0].Direction);
        Assert.Null(x.Transactions[0].Amount);
        Assert.Equal("credit", x.Transactions[1].Direction);
        Assert.Equal("not_evaluated", x.Checks.Single(c => c.Name == "closing_balance_reconciles").Outcome);
        Assert.True(x.ReviewRequired);
    }

    [Fact]
    public void Year_less_dates_are_read_only_within_the_stated_period()
    {
        var x = BankStatementExtractor.Extract(Doc(
            "Statement Period: 20/12/2026 to 19/01/2027",
            "Opening Balance 0.00",
            "28 Dec Deposit 50.00 50.00",
            "05 Jan Fee 5.00 45.00",
            "Closing Balance 45.00"));

        Assert.Equal(new DateOnly(2026, 12, 28), x.Transactions[0].Date);
        Assert.Equal(new DateOnly(2027, 1, 5), x.Transactions[1].Date);
        Assert.False(x.ReviewRequired);
    }

    [Fact]
    public void A_bank_named_only_in_a_transaction_is_not_the_institution()
    {
        var x = BankStatementExtractor.Extract(Doc(
            "Statement Period: 01/08/2026 to 31/08/2026",
            "Opening Balance 100.00",
            "02/08/2026 Transfer to Capitec 40.00 60.00",
            "Closing Balance 60.00"));

        Assert.Null(x.Institution);
    }

    // Payslips

    [Fact]
    public void A_payslip_reads_labelled_values_and_checks_net_pay()
    {
        var x = PayslipExtractor.Extract(Doc(
            "PAYSLIP",
            "Employer: Example Manufacturing (Pty) Ltd",
            "Employee: J. Sample",
            "Employee No: E1042",
            "Pay Period: 01/08/2026 - 31/08/2026",
            "Pay Date: 25/08/2026",
            "Basic salary 20 000.00",
            "Gross Pay 22 000.00",
            "PAYE 3 100.00",
            "UIF 177.12",
            "Total Deductions 3 277.12",
            "Net Pay 18 722.88"));

        Assert.Equal("Example Manufacturing (Pty) Ltd", x.EmployerName!.Value);
        Assert.Equal("J. Sample", x.EmployeeName!.Value);
        Assert.Equal("E1042", x.EmployeeNumber!.Value);
        Assert.Equal(new DateOnly(2026, 8, 25), x.PayDate!.Value);
        Assert.Equal(22000m, x.GrossPay!.Value);
        Assert.Equal(3100m, x.Paye!.Value);
        Assert.Equal(177.12m, x.Uif!.Value);
        Assert.Equal(18722.88m, x.NetPay!.Value);
        Assert.All(x.Checks, c => Assert.Equal("pass", c.Outcome));
        Assert.False(x.ReviewRequired);
    }

    [Fact]
    public void Inconsistent_net_pay_and_missing_employer_are_flagged()
    {
        var x = PayslipExtractor.Extract(Doc(
            "Employee: J. Sample",
            "Pay Date: 25/08/2026",
            "Gross Pay 10 000.00",
            "Total Deductions 1 000.00",
            "Net Pay 9 500.00"));

        Assert.Null(x.EmployerName);
        Assert.Contains("employerName", x.MissingRequired);
        Assert.Equal("fail", x.Checks.Single(c => c.Name == "net_equals_gross_minus_deductions").Outcome);
        Assert.True(x.ReviewRequired);
    }

    // Proof of address

    private static readonly DateOnly Today = new(2026, 10, 3);

    [Fact]
    public void An_address_block_ends_at_its_postal_code()
    {
        var x = ProofOfAddressExtractor.Extract(Doc(
            "Issued by: Example Municipality",
            "Account Holder: J. Sample",
            "Account No: 4400123456",
            "Statement Date: 15/09/2026",
            "Service Address:",
            "12 Example Street",
            "Sampleton",
            "Testville 2196",
            "Amount due 845.00"), Today);

        Assert.Equal("Example Municipality", x.Issuer!.Value);
        Assert.Equal("J. Sample", x.AccountHolder!.Value);
        Assert.Equal(["12 Example Street", "Sampleton", "Testville 2196"], x.Address!.Value);
        Assert.Equal("2196", x.PostalCode!.Value);
        Assert.Equal(new DateOnly(2026, 9, 15), x.DocumentDate!.Value);
        Assert.Equal("******3456", x.AccountNumberMasked!.Value);
        Assert.All(x.Checks, c => Assert.Equal("pass", c.Outcome));
        Assert.False(x.ReviewRequired);
    }

    [Fact]
    public void An_old_document_differs_a_future_one_fails_and_a_birth_date_is_not_the_document_date()
    {
        var old = ProofOfAddressExtractor.Extract(Doc(
            "Name: J. Sample", "Date of birth: 01/01/1990", "Date: 01/01/2026", "Address: 1 Road, Town 2000"), Today);
        Assert.Equal(new DateOnly(2026, 1, 1), old.DocumentDate!.Value);
        Assert.Equal("differs", old.Checks.Single(c => c.Name == "recent").Outcome);
        Assert.True(old.ReviewRequired);

        var future = ProofOfAddressExtractor.Extract(Doc(
            "Name: J. Sample", "Date: 01/01/2027", "Address: 1 Road, Town 2000"), Today);
        Assert.Equal("fail", future.Checks.Single(c => c.Name == "not_future_dated").Outcome);
    }

    [Fact]
    public void An_unlabelled_issuer_is_not_guessed()
    {
        var x = ProofOfAddressExtractor.Extract(Doc(
            "CITY OF EXAMPLE", "Name: J. Sample", "Date: 01/09/2026", "Address: 1 Road, Town 2000"), Today);

        Assert.Null(x.Issuer);
        Assert.Empty(x.MissingRequired);
    }

    // Classification

    [Fact]
    public void Clear_documents_are_classified_with_their_indicators_as_evidence()
    {
        Assert.Equal("bank_statement", DocumentClassifier.Classify(Doc(Statement)).Type);

        var payslip = DocumentClassifier.Classify(Doc("PAYSLIP", "Employee No: 1", "Gross Pay 1 000.00", "PAYE 100.00", "UIF 10.00", "Net Pay 890.00"));
        Assert.Equal("payslip", payslip.Type);
        Assert.Equal("matched", payslip.Decision);
        Assert.Contains(payslip.Candidates[0].Indicators, i => i.Name == "paye" && i.Evidence.Line == "PAYE 100.00");

        var invoice = DocumentClassifier.Classify(Doc("TAX INVOICE", "Invoice No: 1", "Invoice Date: 01/09/2026", "Subtotal 1.00", "Total Due 1.15"));
        Assert.Equal("invoice", invoice.Type);
    }

    [Fact]
    public void Weak_or_mixed_evidence_is_unknown_and_flagged()
    {
        var weak = DocumentClassifier.Classify(Doc("Meeting notes", "Agenda for Tuesday"));
        Assert.Equal("unknown", weak.Type);
        Assert.True(weak.ReviewRequired);
        Assert.Empty(weak.Candidates);

        var mixed = DocumentClassifier.Classify(Doc(
            "TAX INVOICE", "Invoice No: 1", "Subtotal 1.00",
            "Statement Period: 01/08/2026 to 31/08/2026", "Opening Balance 1.00", "Closing Balance 1.00"));
        Assert.Equal("unknown", mixed.Type);
        Assert.Equal("uncertain", mixed.Decision);
    }
}
