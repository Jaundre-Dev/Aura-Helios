using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Helios.Contracts.Catalogue;
using Helios.Contracts.Requests;
using Helios.Contracts.Uploads;
using Helios.IntegrationTests.Fixtures;

namespace Helios.IntegrationTests;

/// <summary>
/// Bank statement, payslip, proof-of-address and classification v1 end to end on synthetic PDFs:
/// upload → 202 → worker → result with evidence and checks. Account numbers come back masked;
/// scans are unreadable and free; page limits apply at acceptance; the catalogue says sandbox only.
/// </summary>
[Collection(HeliosApiCollection.Name)]
public sealed class DocumentProductsV2Tests(HeliosApiFactory factory)
{
    private readonly HeliosApiFactory _factory = factory;

    private const string Statement = "documents.bank-statement";
    private const string Payslip = "documents.payslip";
    private const string Address = "documents.proof-of-address";
    private const string Classify = "documents.classify";

    private static readonly string[] StatementLines =
    [
        "Standard Bank Current Account Statement",
        "Account Holder: Sample Trading CC",
        "Account Number: 001234567890",
        "Statement Period: 01/08/2026 to 31/08/2026",
        "Opening Balance 1 000.00",
        "02/08/2026 Customer payment 4 000.00 5 000.00",
        "10/08/2026 Rent 3 500.00 1 500.00",
        "Closing Balance 1 500.00",
    ];

    private async Task<HttpClient> ClientAsync(params string[] products)
    {
        var company = await TestCompany.OnboardAsync(_factory);
        foreach (var product in products)
        {
            await company.EnableAsync(product, ApiEnvironment.Sandbox);
        }

        return company.KeyClient((await company.CreateKeyAsync(products)).Secret);
    }

    private static async Task<UploadResponse> UploadAsync(HttpClient client, byte[] content)
    {
        var response = await client.PostAsync("/api/v1/uploads", TestDocuments.Form(content, "doc.pdf", "application/pdf"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UploadResponse>())!;
    }

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient client, string product, Guid uploadId) =>
        client.PostAsync($"/api/v1/products/{product}/requests",
            new StringContent(JsonSerializer.Serialize(new { uploadId }), System.Text.Encoding.UTF8, "application/json"));

    private async Task<ApiRequestEnvelope> RunAsync(HttpClient client, string product, byte[] pdf)
    {
        var upload = await UploadAsync(client, pdf);
        var accepted = await SubmitAsync(client, product, upload.Id);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var id = (await accepted.Content.ReadFromJsonAsync<ApiRequestEnvelope>())!.RequestId;

        await _factory.DrainJobsAsync();
        return (await client.GetFromJsonAsync<ApiRequestEnvelope>($"/api/v1/requests/{id}/result"))!;
    }

    private static JsonElement Field(ApiRequestEnvelope envelope, string name) =>
        envelope.Result!.Value.GetProperty("fields").GetProperty(name);

    [Fact]
    public async Task A_statement_is_extracted_reconciled_and_masked()
    {
        var client = await ClientAsync(Statement);
        var envelope = await RunAsync(client, Statement, TestDocuments.TextPdf(StatementLines));

        Assert.Equal(ApiRequestStatus.Succeeded, envelope.Status);
        Assert.False(envelope.ReviewRequired);
        Assert.Equal(new UsageInfo("document", 1m), envelope.Usage);

        Assert.Equal("Standard Bank", Field(envelope, "institution").GetProperty("value").GetString());
        Assert.Equal("********7890", Field(envelope, "accountNumberMasked").GetProperty("value").GetString());
        Assert.Equal(1500m, Field(envelope, "closingBalance").GetProperty("value").GetDecimal());

        var raw = envelope.Result!.Value.GetRawText();
        Assert.DoesNotContain("001234567890", raw);

        var transactions = envelope.Result.Value.GetProperty("transactions").EnumerateArray().ToList();
        Assert.Equal(2, transactions.Count);
        Assert.Equal("credit", transactions[0].GetProperty("direction").GetString());
        Assert.Equal(-3500m, transactions[1].GetProperty("amount").GetDecimal());
        Assert.Equal(1, transactions[1].GetProperty("evidence").GetProperty("page").GetInt32());

        Assert.All(envelope.Result.Value.GetProperty("checks").EnumerateArray(),
            c => Assert.Equal("pass", c.GetProperty("outcome").GetString()));
    }

    [Fact]
    public async Task A_payslip_with_wrong_arithmetic_is_returned_for_review()
    {
        var client = await ClientAsync(Payslip);
        var envelope = await RunAsync(client, Payslip, TestDocuments.TextPdf(
        [
            "PAYSLIP",
            "Employer: Example Manufacturing (Pty) Ltd",
            "Employee: J. Sample",
            "Pay Date: 25/08/2026",
            "Gross Pay 10 000.00",
            "Total Deductions 2 000.00",
            "Net Pay 8 500.00",
        ]));

        Assert.Equal(ApiRequestStatus.NeedsReview, envelope.Status);
        Assert.Equal(8500m, Field(envelope, "netPay").GetProperty("value").GetDecimal());
        Assert.Contains(envelope.Warnings, w => w.Contains("net_equals_gross_minus_deductions"));
    }

    [Fact]
    public async Task Proof_of_address_returns_the_address_block_with_one_evidence_box()
    {
        var client = await ClientAsync(Address);
        var date = DateTimeOffset.UtcNow.AddDays(-10).ToString("dd/MM/yyyy");

        var envelope = await RunAsync(client, Address, TestDocuments.TextPdf(
        [
            "Issued by: Example Municipality",
            "Account Holder: J. Sample",
            $"Statement Date: {date}",
            "Physical Address:",
            "12 Example Street",
            "Testville 2196",
        ]));

        Assert.Equal(ApiRequestStatus.Succeeded, envelope.Status);
        var address = Field(envelope, "address");
        Assert.Equal(["12 Example Street", "Testville 2196"], address.GetProperty("value").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal(4, address.GetProperty("evidence").GetProperty("box").GetArrayLength());
        Assert.Equal("2196", Field(envelope, "postalCode").GetProperty("value").GetString());
    }

    [Fact]
    public async Task Classification_routes_a_statement_and_admits_uncertainty()
    {
        var client = await ClientAsync(Classify);

        var statement = await RunAsync(client, Classify, TestDocuments.TextPdf(StatementLines));
        Assert.Equal("bank_statement", statement.Result!.Value.GetProperty("type").GetString());
        Assert.Equal(ApiRequestStatus.Succeeded, statement.Status);

        var unclear = await RunAsync(client, Classify, TestDocuments.TextPdf(["Minutes of the meeting", "Present: everyone"]));
        Assert.Equal("unknown", unclear.Result!.Value.GetProperty("type").GetString());
        Assert.Equal(ApiRequestStatus.NeedsReview, unclear.Status);
    }

    [Fact]
    public async Task Scans_are_unreadable_and_not_charged_for_every_new_product()
    {
        var client = await ClientAsync(Statement, Payslip, Address, Classify);

        foreach (var product in new[] { Statement, Payslip, Address, Classify })
        {
            var envelope = await RunAsync(client, product, TestDocuments.ImageOnlyPdf());

            Assert.Equal(ApiRequestStatus.NeedsReview, envelope.Status);
            Assert.False(envelope.Result!.Value.GetProperty("readable").GetBoolean());
            Assert.Equal(0m, envelope.Usage.Quantity);
        }
    }

    [Fact]
    public async Task Page_limits_apply_before_anything_runs()
    {
        var client = await ClientAsync(Payslip);
        var upload = await UploadAsync(client, TestDocuments.ManyPagesPdf(6));

        var response = await SubmitAsync(client, Payslip, upload.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("too_many_pages", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_catalogue_publishes_each_as_sandbox_only_with_its_schema_and_limits()
    {
        var anonymous = _factory.CreateClient();

        foreach (var slug in new[] { Statement, Payslip, Address, Classify })
        {
            var detail = (await anonymous.GetFromJsonAsync<ProductDetailResponse>($"/api/v1/catalogue/{slug}"))!;

            Assert.Equal(ProductReleaseState.Sandbox, detail.Product.ReleaseState);
            Assert.True(detail.Product.CallableInSandbox);
            Assert.False(detail.Product.CallableInLive);
            Assert.Contains("not been measured", detail.Product.Limitations);

            var version = Assert.Single(detail.Versions);
            using var schema = JsonDocument.Parse(version.ResponseSchemaJson!);
            Assert.True(schema.RootElement.GetProperty("properties").TryGetProperty("readable", out _));
        }

        var classify = (await anonymous.GetFromJsonAsync<ProductDetailResponse>($"/api/v1/catalogue/{Classify}"))!;
        Assert.Equal(ProductDelivery.Build, classify.Product.Delivery);
    }
}
