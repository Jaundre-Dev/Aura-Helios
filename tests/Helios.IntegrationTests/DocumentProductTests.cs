using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Helios.Contracts.Catalogue;
using Helios.Contracts.Requests;
using Helios.Contracts.Uploads;
using Helios.Infrastructure.Persistence.MySql;
using Helios.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.IntegrationTests;

/// <summary>
/// ocr.general and documents.invoice v1 end to end: upload → accepted (202) → worker → result with
/// evidence. Uncertain data is never invented; unreadable pages are flagged and not charged; uploads
/// cannot be used across tenants, types or page limits.
/// </summary>
[Collection(HeliosApiCollection.Name)]
public sealed class DocumentProductTests(HeliosApiFactory factory)
{
    private readonly HeliosApiFactory _factory = factory;
    private const string Ocr = "ocr.general";
    private const string Invoice = "documents.invoice";

    private static readonly string[] CleanInvoice =
    [
        "TAX INVOICE",
        "Supplier: Example Office Supplies (Pty) Ltd",
        "VAT No: 4123456789",
        "Invoice No: INV-2026-0042",
        "Invoice Date: 15/09/2026",
        "Due Date: 15/10/2026",
        "Bill To:",
        "Sample Trading CC",
        "Customer VAT No: 4987654321",
        "Description Qty Unit Price Amount",
        "A4 paper boxes 10 250.00 2 500.00",
        "Toner cartridges 4 1 875.00 7 500.00",
        "Subtotal R 10 000.00",
        "VAT 15% R 1 500.00",
        "Total Due R 11 500.00",
    ];

    private async Task<(TestCompany Company, HttpClient Client)> CompanyAsync(params string[] products)
    {
        var company = await TestCompany.OnboardAsync(_factory);
        foreach (var product in products)
        {
            await company.EnableAsync(product, ApiEnvironment.Sandbox);
        }

        var key = await company.CreateKeyAsync(products);
        return (company, company.KeyClient(key.Secret));
    }

    private static async Task<UploadResponse> UploadAsync(HttpClient client, byte[] content, string type = "application/pdf")
    {
        var response = await client.PostAsync("/api/v1/uploads", TestDocuments.Form(content, "doc", type));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UploadResponse>())!;
    }

    private static Task<HttpResponseMessage> RunAsync(HttpClient client, string product, Guid uploadId) =>
        client.PostAsync($"/api/v1/products/{product}/requests",
            new StringContent(JsonSerializer.Serialize(new { uploadId }), System.Text.Encoding.UTF8, "application/json"));

    private async Task<ApiRequestEnvelope> RunToCompletionAsync(HttpClient client, string product, Guid uploadId)
    {
        var accepted = await RunAsync(client, product, uploadId);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var requestId = (await accepted.Content.ReadFromJsonAsync<ApiRequestEnvelope>())!.RequestId;

        await _factory.DrainJobsAsync();

        return (await client.GetFromJsonAsync<ApiRequestEnvelope>($"/api/v1/requests/{requestId}/result"))!;
    }

    [Fact]
    public async Task Ocr_returns_each_pages_text_with_line_positions()
    {
        var (_, client) = await CompanyAsync(Ocr);
        var upload = await UploadAsync(client, TestDocuments.TextPdf(
            ["Delivery note 7781", "Received in good order"],
            ["Signed: J. Example"]));

        var envelope = await RunToCompletionAsync(client, Ocr, upload.Id);

        Assert.Equal(ApiRequestStatus.Succeeded, envelope.Status);
        Assert.False(envelope.ReviewRequired);
        Assert.Equal(new UsageInfo("page", 2m), envelope.Usage);

        var result = envelope.Result!.Value;
        Assert.Equal("pdf_text_layer", result.GetProperty("method").GetString());
        var pages = result.GetProperty("pages").EnumerateArray().ToList();
        Assert.Equal("Delivery note 7781\nReceived in good order", pages[0].GetProperty("text").GetString());
        Assert.Equal("Signed: J. Example", pages[1].GetProperty("text").GetString());

        var box = pages[0].GetProperty("lines")[0].GetProperty("box").EnumerateArray().Select(b => b.GetDouble()).ToArray();
        Assert.Equal(4, box.Length);
        Assert.All(box, v => Assert.InRange(v, 0, 1));
        Assert.True(box[0] < box[2] && box[1] < box[3]);
    }

    [Fact]
    public async Task Pages_without_a_text_layer_are_flagged_and_not_charged()
    {
        var (_, client) = await CompanyAsync(Ocr);

        var scanned = await UploadAsync(client, TestDocuments.ImageOnlyPdf(2));
        var envelope = await RunToCompletionAsync(client, Ocr, scanned.Id);

        Assert.Equal(ApiRequestStatus.NeedsReview, envelope.Status);
        Assert.True(envelope.ReviewRequired);
        Assert.Equal(0m, envelope.Usage.Quantity);
        Assert.Equal(2, envelope.Warnings.Count);
        Assert.All(envelope.Result!.Value.GetProperty("pages").EnumerateArray(),
            p => Assert.Equal(string.Empty, p.GetProperty("text").GetString()));
    }

    [Fact]
    public async Task A_clean_invoice_is_extracted_with_evidence_and_passing_checks()
    {
        var (_, client) = await CompanyAsync(Invoice);
        var upload = await UploadAsync(client, TestDocuments.TextPdf(CleanInvoice));

        var envelope = await RunToCompletionAsync(client, Invoice, upload.Id);

        Assert.Equal(ApiRequestStatus.Succeeded, envelope.Status);
        Assert.False(envelope.ReviewRequired);
        Assert.Equal(new UsageInfo("document", 1m), envelope.Usage);

        var fields = envelope.Result!.Value.GetProperty("fields");
        string Text(string name) => fields.GetProperty(name).GetProperty("value").ToString();

        Assert.Equal("INV-2026-0042", Text("invoiceNumber"));
        Assert.Equal("2026-09-15", Text("invoiceDate"));
        Assert.Equal("2026-10-15", Text("dueDate"));
        Assert.Equal("Example Office Supplies (Pty) Ltd", Text("supplierName"));
        Assert.Equal("4123456789", Text("supplierVatNumber"));
        Assert.Equal("Sample Trading CC", Text("customerName"));
        Assert.Equal("4987654321", Text("customerVatNumber"));
        Assert.Equal("ZAR", Text("currency"));
        Assert.Equal(10000m, fields.GetProperty("subtotal").GetProperty("value").GetDecimal());
        Assert.Equal(1500m, fields.GetProperty("vat").GetProperty("value").GetDecimal());
        Assert.Equal(11500m, fields.GetProperty("total").GetProperty("value").GetDecimal());

        var totalEvidence = fields.GetProperty("total").GetProperty("evidence");
        Assert.Equal(1, totalEvidence.GetProperty("page").GetInt32());
        Assert.Equal("Total Due R 11 500.00", totalEvidence.GetProperty("source").GetString());

        var items = envelope.Result.Value.GetProperty("lineItems").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal(7500m, items[1].GetProperty("amount").GetDecimal());

        Assert.All(envelope.Result.Value.GetProperty("checks").EnumerateArray(),
            c => Assert.Contains(c.GetProperty("outcome").GetString(), new[] { "pass" }));
    }

    [Fact]
    public async Task Inconsistent_totals_are_flagged_not_corrected()
    {
        var (_, client) = await CompanyAsync(Invoice);
        var lines = CleanInvoice.Select(l => l == "Total Due R 11 500.00" ? "Total Due R 11 900.00" : l).ToArray();
        var upload = await UploadAsync(client, TestDocuments.TextPdf(lines));

        var envelope = await RunToCompletionAsync(client, Invoice, upload.Id);

        Assert.True(envelope.ReviewRequired);
        Assert.Equal(11900m, envelope.Result!.Value.GetProperty("fields").GetProperty("total").GetProperty("value").GetDecimal());
        var totals = envelope.Result.Value.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "totals_add_up");
        Assert.Equal("fail", totals.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Missing_fields_are_null_and_listed_never_invented()
    {
        var (_, client) = await CompanyAsync(Invoice);
        var upload = await UploadAsync(client, TestDocuments.TextPdf(
            ["Example Office Supplies", "Thank you for your business", "Total Due R 230.00"]));

        var envelope = await RunToCompletionAsync(client, Invoice, upload.Id);

        Assert.True(envelope.ReviewRequired);
        var result = envelope.Result!.Value;
        var fields = result.GetProperty("fields");
        Assert.Equal(JsonValueKind.Null, fields.GetProperty("invoiceNumber").ValueKind);
        Assert.Equal(JsonValueKind.Null, fields.GetProperty("invoiceDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, fields.GetProperty("supplierName").ValueKind);   // unlabelled: not guessed
        Assert.Equal(230m, fields.GetProperty("total").GetProperty("value").GetDecimal());
        Assert.Equal(["invoiceNumber", "invoiceDate"], result.GetProperty("missingRequired").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Another_companys_upload_cannot_be_processed()
    {
        var (_, ownerClient) = await CompanyAsync(Ocr);
        var (other, otherClient) = await CompanyAsync(Ocr);
        var upload = await UploadAsync(ownerClient, TestDocuments.TextPdf(["private"]));

        var response = await RunAsync(otherClient, Ocr, upload.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = _factory.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
        Assert.False(await db.ApiRequests.AnyAsync(r => r.OrganizationId == other.Organization.Id));
    }

    [Fact]
    public async Task Unsupported_types_and_page_limits_are_refused_at_acceptance()
    {
        var (_, client) = await CompanyAsync(Ocr, Invoice);
        var image = await UploadAsync(client, TestDocuments.PngHeader(800, 600), "image/png");
        var longPdf = await UploadAsync(client, TestDocuments.ManyPagesPdf(25));

        var imageResponse = await RunAsync(client, Ocr, image.Id);
        var longResponse = await RunAsync(client, Invoice, longPdf.Id);

        Assert.Equal(HttpStatusCode.BadRequest, imageResponse.StatusCode);
        Assert.Contains("unsupported_media_type", await imageResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, longResponse.StatusCode);
        Assert.Contains("too_many_pages", await longResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_document_deleted_after_acceptance_fails_once_without_retries()
    {
        var (_, client) = await CompanyAsync(Ocr);
        var upload = await UploadAsync(client, TestDocuments.TextPdf(["soon gone"]));

        var accepted = await RunAsync(client, Ocr, upload.Id);
        var requestId = (await accepted.Content.ReadFromJsonAsync<ApiRequestEnvelope>())!.RequestId;
        (await client.DeleteAsync($"/api/v1/uploads/{upload.Id}")).EnsureSuccessStatusCode();

        await _factory.DrainJobsAsync();

        var summary = await client.GetFromJsonAsync<ApiRequestSummary>($"/api/v1/requests/{requestId}");
        Assert.Equal(ApiRequestStatus.Failed, summary!.Status);
        Assert.Equal("upload_unavailable", summary.Error);

        using var scope = _factory.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
        Assert.Equal(1, (await db.Jobs.SingleAsync(j => j.ApiRequestId == requestId)).Attempts);
    }

    [Fact]
    public async Task Document_products_are_not_offered_for_live_use_yet()
    {
        var company = await TestCompany.OnboardAsync(_factory);

        var response = await company.Owner.Client.PostAsJsonAsync(
            $"/api/v1/organizations/{company.Organization.Id}/entitlements",
            new EnableEntitlementRequest(Invoice, ApiEnvironment.Live));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
