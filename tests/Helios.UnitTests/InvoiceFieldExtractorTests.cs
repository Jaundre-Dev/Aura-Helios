using Helios.Application.Abstractions.Documents;
using Helios.Application.Features.Products.Documents;

namespace Helios.UnitTests;

public class InvoiceFieldExtractorTests
{
    private static PdfText Doc(params string[] lines) =>
        new([new PdfTextPage(1, 595, 842, lines.Select((text, i) =>
            new PdfTextLine(text, new TextBox(0.1, 0.05 + (i * 0.02), 0.9, 0.065 + (i * 0.02)), [])).ToList())]);

    [Theory]
    [InlineData("R 1 234,56", 1234.56)]
    [InlineData("R1,234.56", 1234.56)]
    [InlineData("1 234.56", 1234.56)]
    [InlineData("1234,56", 1234.56)]
    [InlineData("1,234", 1234)]
    [InlineData("ZAR 11 500.00", 11500)]
    [InlineData("1.234,56", 1234.56)]
    public void Money_in_south_african_and_international_formats(string raw, decimal expected)
    {
        Assert.Equal(expected, InvoiceFieldExtractor.ParseMoney(raw));
    }

    [Theory]
    [InlineData("15/09/2026", 2026, 9, 15)]
    [InlineData("2026-09-15", 2026, 9, 15)]
    [InlineData("15 September 2026", 2026, 9, 15)]
    [InlineData("15 Sep 2026", 2026, 9, 15)]
    [InlineData("September 15, 2026", 2026, 9, 15)]
    [InlineData("15.09.2026", 2026, 9, 15)]
    public void Dates_in_common_formats_day_first_for_numeric(string raw, int y, int m, int d)
    {
        Assert.Equal(new DateOnly(y, m, d), InvoiceFieldExtractor.ParseDate(raw));
    }

    [Theory]
    [InlineData("31/02/2026")]
    [InlineData("not a date")]
    public void Impossible_or_absent_dates_are_null(string raw)
    {
        Assert.Null(InvoiceFieldExtractor.ParseDate(raw));
    }

    [Fact]
    public void Subtotal_is_not_mistaken_for_the_total_and_vat_numbers_are_not_amounts()
    {
        var result = InvoiceFieldExtractor.Extract(Doc(
            "Invoice Number: A-17",
            "Date: 01/03/2026",
            "VAT Reg No: 4111111111",
            "Sub-total 200.00",
            "VAT 30.00",
            "Total 230.00"));

        Assert.Equal(200m, result.Subtotal!.Value);
        Assert.Equal(30m, result.Vat!.Value);
        Assert.Equal(230m, result.Total!.Value);
        Assert.Equal("4111111111", result.SupplierVatNumber!.Value);
        Assert.Empty(result.MissingRequired);
        Assert.False(result.ReviewRequired);
    }

    [Fact]
    public void An_explicit_total_due_wins_over_an_earlier_bare_total()
    {
        var result = InvoiceFieldExtractor.Extract(Doc(
            "Invoice No: 9",
            "Date: 01/03/2026",
            "Total 100.00",
            "Balance Due 60.00"));

        Assert.Equal(60m, result.Total!.Value);
        Assert.Equal("Balance Due 60.00", result.Total.Evidence.Line);
    }

    [Fact]
    public void An_unlabelled_company_name_is_not_guessed_as_supplier()
    {
        var result = InvoiceFieldExtractor.Extract(Doc("ACME HOLDINGS", "Invoice No: 5", "Total R 10.00"));

        Assert.Null(result.SupplierName);
        Assert.Contains("invoiceDate", result.MissingRequired);
        Assert.True(result.ReviewRequired);
    }

    [Fact]
    public void A_non_standard_vat_rate_is_reported_as_differs_not_failure()
    {
        var result = InvoiceFieldExtractor.Extract(Doc(
            "Invoice No: Z-1", "Date: 01/03/2026", "Subtotal 100.00", "VAT 0.00", "Total 100.00"));

        Assert.Equal("differs", result.Checks.Single(c => c.Name == "vat_rate").Outcome);
        Assert.Equal("pass", result.Checks.Single(c => c.Name == "totals_add_up").Outcome);
        Assert.False(result.ReviewRequired);
    }

    [Fact]
    public void A_due_date_before_the_invoice_date_fails()
    {
        var result = InvoiceFieldExtractor.Extract(Doc(
            "Invoice No: D-1", "Invoice Date: 10/03/2026", "Due Date: 01/03/2026", "Total 50.00"));

        Assert.Equal("fail", result.Checks.Single(c => c.Name == "due_after_issue").Outcome);
        Assert.True(result.ReviewRequired);
    }

    [Fact]
    public void Line_items_are_taken_only_when_quantity_times_price_equals_amount()
    {
        var result = InvoiceFieldExtractor.Extract(Doc(
            "Invoice No: L-1", "Date: 01/03/2026",
            "Widgets 3 10.00 30.00",
            "Gadgets 2 5.00 11.00",
            "Total 30.00"));

        var item = Assert.Single(result.LineItems);
        Assert.Equal("Widgets", item.Description);
        Assert.Equal("pass", result.Checks.Single(c => c.Name == "line_items_sum").Outcome);
    }
}
