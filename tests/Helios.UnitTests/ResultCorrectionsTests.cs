using System.Text.Json;
using Helios.Application.Features.Requests;

namespace Helios.UnitTests;

public class ResultCorrectionsTests
{
    private static readonly JsonElement Result = JsonDocument.Parse("""
        {
          "readable": true,
          "fields": {
            "invoiceNumber": {"value": "INV-1", "evidence": {"page": 1, "box": [0,0,1,1], "source": "Invoice No: INV-1"}},
            "dueDate": null,
            "total": {"value": 115.0, "evidence": {"page": 2, "box": [0,0,1,1], "source": "Total 115.00"}}
          },
          "lineItems": [
            {"description": "Paper", "amount": 100.0, "evidence": {"page": 1, "box": [0,0,1,1], "source": "Paper 1 100.00 100.00"}}
          ],
          "checks": [{"name": "totals_add_up", "outcome": "pass", "detail": null}],
          "notice": "n"
        }
        """).RootElement.Clone();

    private static Dictionary<string, JsonElement> C(params (string Path, string Json)[] items) =>
        items.ToDictionary(i => i.Path, i => JsonDocument.Parse(i.Json).RootElement.Clone());

    [Fact]
    public void Fields_present_or_missing_and_plain_list_values_can_be_corrected()
    {
        var errors = ResultCorrections.Validate(Result, C(
            ("fields.total", "120.0"), ("fields.dueDate", "\"2026-10-15\""), ("lineItems[0].amount", "105.0")));

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("fields.supplierBankAccount", "This result has no such field.")]
    [InlineData("lineItems[3].amount", "No such plain value at this position.")]
    [InlineData("lineItems[0].evidence", "Evidence and correction markers cannot be edited.")]
    [InlineData("notice", "Use 'fields.<name>' or '<list>[<index>].<property>'.")]
    [InlineData("fields.total.value", "Use 'fields.<name>' or '<list>[<index>].<property>'.")]
    public void Unknown_paths_and_evidence_are_refused(string path, string message)
    {
        var errors = ResultCorrections.Validate(Result, C((path, "1")));
        Assert.Equal(message, Assert.Single(errors[$"corrections.{path}"]));
    }

    [Fact]
    public void Objects_are_not_accepted_as_values()
    {
        var errors = ResultCorrections.Validate(Result, C(("fields.total", """{"value": 1}""")));
        Assert.Single(errors);
    }

    [Fact]
    public void Applying_keeps_evidence_marks_the_change_and_leaves_the_original_alone()
    {
        var corrected = ResultCorrections.Apply(Result, C(("fields.total", "120.0"), ("fields.dueDate", "\"2026-10-15\""), ("lineItems[0].amount", "105.0")));

        Assert.Equal(120.0m, corrected["fields"]!["total"]!["value"]!.GetValue<decimal>());
        Assert.True(corrected["fields"]!["total"]!["corrected"]!.GetValue<bool>());
        Assert.Equal("Total 115.00", corrected["fields"]!["total"]!["evidence"]!["source"]!.GetValue<string>());
        Assert.Null(corrected["fields"]!["dueDate"]!["evidence"]);
        Assert.Equal(105.0m, corrected["lineItems"]![0]!["amount"]!.GetValue<decimal>());

        Assert.Equal(115.0m, Result.GetProperty("fields").GetProperty("total").GetProperty("value").GetDecimal());
    }

    [Fact]
    public void Export_rows_show_original_correction_and_final_with_page()
    {
        var rows = ResultCorrections.Rows(Result, C(("fields.total", "120.0")));

        var total = rows.Single(r => r.Section == "fields" && r.Name == "total");
        Assert.Equal(("115.0", "120.0", "120.0", 2), (total.Original, total.Corrected, total.Final, total.Page));

        var number = rows.Single(r => r.Name == "invoiceNumber");
        Assert.Equal(("INV-1", null, "INV-1"), (number.Original, number.Corrected, number.Final));

        Assert.Contains(rows, r => r.Section == "lineItems[0]" && r.Name == "amount" && r.Page == 1);
        Assert.Contains(rows, r => r.Section == "checks[0]" && r.Name == "outcome" && r.Final == "pass");
        Assert.Contains(rows, r => r.Section == "result" && r.Name == "readable" && r.Final == "true");
    }

    [Fact]
    public void Csv_quotes_where_needed_and_defuses_formulas_but_not_negative_numbers()
    {
        var csv = ResultCorrections.ToCsv(
        [
            new ExportRow("fields", "supplierName", "=HYPERLINK(\"http://x\")", null, "=HYPERLINK(\"http://x\")", 1, "Supplier: A, B"),
            new ExportRow("transactions[0]", "amount", "-3500", null, "-3500", 1, null),
        ]);

        var lines = csv.Split("\r\n");
        Assert.Equal("section,name,original,corrected,final,page,source", lines[0]);
        Assert.Equal("fields,supplierName,\"'=HYPERLINK(\"\"http://x\"\")\",,\"'=HYPERLINK(\"\"http://x\"\")\",1,\"Supplier: A, B\"", lines[1]);
        Assert.Equal("transactions[0],amount,-3500,,-3500,1,", lines[2]);
    }
}
