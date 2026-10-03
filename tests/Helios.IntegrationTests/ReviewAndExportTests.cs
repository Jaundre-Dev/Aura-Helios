using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Helios.Application.Features.Retention;
using Helios.Contracts.Catalogue;
using Helios.Contracts.Organizations;
using Helios.Contracts.Requests;
using Helios.Contracts.Uploads;
using Helios.Infrastructure.Persistence.MySql;
using Helios.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.IntegrationTests;

/// <summary>
/// Manual review (plan section 4): corrections never overwrite the original, every decision keeps
/// its actor and reason, only permitted roles review or export, exports apply the corrections in
/// force, and retention removes corrected values with the result.
/// </summary>
[Collection(HeliosApiCollection.Name)]
public sealed class ReviewAndExportTests(HeliosApiFactory factory)
{
    private readonly HeliosApiFactory _factory = factory;
    private const string Invoice = "documents.invoice";

    // No due date: the reviewer fills it in.
    private static readonly string[] InvoiceLines =
    [
        "TAX INVOICE",
        "Supplier: Example Office Supplies (Pty) Ltd",
        "Invoice No: INV-77",
        "Invoice Date: 15/09/2026",
        "Subtotal R 100.00",
        "VAT R 15.00",
        "Total Due R 115.00",
    ];

    private async Task<(TestCompany Company, HttpClient Key, Guid RequestId)> ProcessedInvoiceAsync()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        await company.EnableAsync(Invoice, ApiEnvironment.Sandbox);
        var key = company.KeyClient((await company.CreateKeyAsync(Invoice)).Secret);

        var upload = await key.PostAsync("/api/v1/uploads", TestDocuments.Form(TestDocuments.TextPdf(InvoiceLines), "inv.pdf", "application/pdf"));
        var uploadId = (await upload.Content.ReadFromJsonAsync<UploadResponse>())!.Id;

        var accepted = await key.PostAsync($"/api/v1/products/{Invoice}/requests",
            new StringContent(JsonSerializer.Serialize(new { uploadId }), System.Text.Encoding.UTF8, "application/json"));
        var requestId = (await accepted.Content.ReadFromJsonAsync<ApiRequestEnvelope>())!.RequestId;

        await _factory.DrainJobsAsync();
        return (company, key, requestId);
    }

    private static Task<HttpResponseMessage> ReviewAsync(HttpClient client, Guid id, ReviewDecisionType decision, string? reason = null, params (string Path, object? Value)[] corrections) =>
        client.PostAsJsonAsync($"/api/v1/requests/{id}/review", new SubmitReviewRequest(decision,
            corrections.Length == 0 ? null : corrections.ToDictionary(c => c.Path, c => JsonSerializer.SerializeToElement(c.Value)),
            reason));

    [Fact]
    public async Task A_correction_keeps_the_original_and_records_who_made_it()
    {
        var (company, _, id) = await ProcessedInvoiceAsync();
        var operatorAccount = await company.AddMemberAsync(OrganizationRole.Operator);

        var before = await operatorAccount.Client.GetFromJsonAsync<RequestReviewResponse>($"/api/v1/requests/{id}/review");
        Assert.Equal("not_required", before!.State);

        var response = await ReviewAsync(operatorAccount.Client, id, ReviewDecisionType.Correct, "Due date from the supplier's email",
            ("fields.dueDate", "2026-10-15"), ("fields.total", 115.00m));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var review = (await response.Content.ReadFromJsonAsync<RequestReviewResponse>())!;
        Assert.Equal("corrected", review.State);
        Assert.Equal("2026-10-15", review.Corrections["fields.dueDate"].GetString());
        var decision = Assert.Single(review.History);
        Assert.Equal(operatorAccount.UserId, decision.ActorUserId);
        Assert.Equal("Due date from the supplier's email", decision.Reason);

        // The stored result is untouched.
        var result = await operatorAccount.Client.GetFromJsonAsync<ApiRequestEnvelope>($"/api/v1/requests/{id}/result");
        Assert.Equal(JsonValueKind.Null, result!.Result!.Value.GetProperty("fields").GetProperty("dueDate").ValueKind);

        // A later approval keeps the corrections in force.
        (await ReviewAsync(operatorAccount.Client, id, ReviewDecisionType.Approve)).EnsureSuccessStatusCode();
        var after = await operatorAccount.Client.GetFromJsonAsync<RequestReviewResponse>($"/api/v1/requests/{id}/review");
        Assert.Equal("approved", after!.State);
        Assert.Equal(2, after.History.Count);
        Assert.True(after.Corrections.ContainsKey("fields.dueDate"));

        // The audit trail names the paths, never the values.
        using var scope = _factory.CreateSystemScope();
        var audit = await scope.ServiceProvider.GetRequiredService<HeliosDbContext>().AuditLogs
            .Where(a => a.Action == "request.review" && a.ResourceId == id.ToString()).ToListAsync();
        Assert.Equal(2, audit.Count);
        Assert.Contains(audit, a => a.Metadata!.Contains("fields.dueDate") && !a.Metadata.Contains("2026-10-15"));
    }

    [Fact]
    public async Task Each_decision_is_announced_by_webhook_with_paths_but_no_values()
    {
        var (company, key, id) = await ProcessedInvoiceAsync();
        var host = $"hooks-{Guid.NewGuid():N}.example.com";
        (await company.Owner.Client.PostAsJsonAsync("/api/v1/webhooks",
            new Contracts.Webhooks.CreateWebhookRequest($"https://{host}/helios", [Contracts.Webhooks.WebhookEventTypes.RequestReviewed])))
            .EnsureSuccessStatusCode();

        (await ReviewAsync(key, id, ReviewDecisionType.Correct, "From email", ("fields.dueDate", "2026-10-15"))).EnsureSuccessStatusCode();
        (await ReviewAsync(key, id, ReviewDecisionType.Approve)).EnsureSuccessStatusCode();
        await _factory.DrainWebhooksAsync();

        var received = _factory.WebhookReceiver.For(host);
        Assert.Equal(2, received.Count);
        Assert.Equal(2, received.Select(r => r.Headers["Helios-Event-Id"]).Distinct().Count());

        using var first = JsonDocument.Parse(received[0].Body);
        var data = first.RootElement.GetProperty("data");
        Assert.Equal("request.reviewed", first.RootElement.GetProperty("type").GetString());
        Assert.Equal(id, data.GetProperty("requestId").GetGuid());
        Assert.Equal("corrected", data.GetProperty("state").GetString());
        Assert.Equal(["fields.dueDate"], data.GetProperty("correctedPaths").EnumerateArray().Select(p => p.GetString()));
        Assert.DoesNotContain("2026-10-15", received[0].Body);
    }

    [Fact]
    public async Task Exports_apply_corrections_and_defuse_spreadsheet_formulas()
    {
        var (company, key, id) = await ProcessedInvoiceAsync();

        (await ReviewAsync(key, id, ReviewDecisionType.Correct, null,
            ("fields.dueDate", "2026-10-15"), ("fields.customerName", "=HYPERLINK(\"http://evil\")"))).EnsureSuccessStatusCode();

        var json = await key.GetAsync($"/api/v1/requests/{id}/export");
        Assert.Equal("application/json", json.Content.Headers.ContentType!.MediaType);
        using var document = JsonDocument.Parse(await json.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("corrected", root.GetProperty("review").GetProperty("state").GetString());
        var dueDate = root.GetProperty("result").GetProperty("fields").GetProperty("dueDate");
        Assert.Equal("2026-10-15", dueDate.GetProperty("value").GetString());
        Assert.True(dueDate.GetProperty("corrected").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("original").GetProperty("fields").GetProperty("dueDate").ValueKind);

        var csvResponse = await key.GetAsync($"/api/v1/requests/{id}/export?format=csv");
        Assert.Equal("text/csv", csvResponse.Content.Headers.ContentType!.MediaType);
        var csv = await csvResponse.Content.ReadAsStringAsync();
        Assert.Contains("fields,dueDate,,2026-10-15,2026-10-15,,", csv);
        Assert.Contains("fields,invoiceNumber,INV-77,,INV-77,1,Invoice No: INV-77", csv);
        Assert.Contains("'=HYPERLINK", csv);
        Assert.DoesNotContain(",=HYPERLINK", csv);

        Assert.Equal(HttpStatusCode.BadRequest, (await key.GetAsync($"/api/v1/requests/{id}/export?format=xlsx")).StatusCode);

        using var scope = _factory.CreateSystemScope();
        var exports = await scope.ServiceProvider.GetRequiredService<HeliosDbContext>().AuditLogs
            .CountAsync(a => a.Action == "request.export" && a.ResourceId == id.ToString());
        Assert.Equal(2, exports);
    }

    [Fact]
    public async Task Only_reviewing_roles_review_and_only_result_readers_export()
    {
        var (company, _, id) = await ProcessedInvoiceAsync();
        var finance = await company.AddMemberAsync(OrganizationRole.Finance);
        var developer = await company.AddMemberAsync(OrganizationRole.Developer);
        var admin = await company.AddMemberAsync(OrganizationRole.Admin);

        Assert.Equal(HttpStatusCode.Forbidden, (await ReviewAsync(finance.Client, id, ReviewDecisionType.Approve)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ReviewAsync(developer.Client, id, ReviewDecisionType.Approve)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await developer.Client.GetAsync($"/api/v1/requests/{id}/export")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.Client.GetAsync($"/api/v1/requests/{id}/review")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await ReviewAsync(admin.Client, id, ReviewDecisionType.Approve)).StatusCode);

        // Another company cannot see that the request exists.
        var stranger = await TestCompany.OnboardAsync(_factory);
        Assert.Equal(HttpStatusCode.NotFound, (await ReviewAsync(stranger.Owner.Client, id, ReviewDecisionType.Approve)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Owner.Client.GetAsync($"/api/v1/requests/{id}/export")).StatusCode);

        // A key scoped to another product cannot either.
        var otherKey = company.KeyClient((await company.CreateKeyAsync(TestCompany.SaIdProduct)).Secret);
        Assert.Equal(HttpStatusCode.NotFound, (await otherKey.GetAsync($"/api/v1/requests/{id}/review")).StatusCode);
    }

    [Theory]
    [InlineData("Correct", null, false, "corrections")]
    [InlineData("Approve", "fields.total", false, "corrections")]
    [InlineData("Reject", null, false, "reason")]
    [InlineData("Correct", "fields.bankAccount", false, "corrections.fields.bankAccount")]
    [InlineData("Correct", "lineItems[0].evidence", false, "corrections.lineItems[0].evidence")]
    public async Task Invalid_decisions_are_refused_and_record_nothing(string decision, string? path, bool withReason, string errorKey)
    {
        var (_, key, id) = await ProcessedInvoiceAsync();
        var corrections = path is null ? Array.Empty<(string, object?)>() : [(path, (object?)1m)];

        var response = await ReviewAsync(key, id, Enum.Parse<ReviewDecisionType>(decision), withReason ? "why" : null, corrections);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains($"\"{errorKey}\"", await response.Content.ReadAsStringAsync());

        using var scope = _factory.CreateSystemScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<HeliosDbContext>().ReviewDecisions.AnyAsync(d => d.ApiRequestId == id));
    }

    [Fact]
    public async Task Results_without_fields_can_be_approved_or_rejected_but_not_corrected()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var key = company.KeyClient((await company.CreateKeyAsync()).Secret);
        var run = await key.PostAsync($"/api/v1/products/{TestCompany.SaIdProduct}/requests",
            new StringContent("""{"idNumber":"8001015009087"}""", System.Text.Encoding.UTF8, "application/json"));
        var id = (await run.Content.ReadFromJsonAsync<ApiRequestEnvelope>())!.RequestId;

        Assert.Equal(HttpStatusCode.BadRequest, (await ReviewAsync(key, id, ReviewDecisionType.Correct, null, ("fields.valid", true))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ReviewAsync(key, id, ReviewDecisionType.Reject, "Wrong applicant's number")).StatusCode);
    }

    [Fact]
    public async Task Retention_removes_corrected_values_with_the_result_but_keeps_the_decision()
    {
        var (_, key, id) = await ProcessedInvoiceAsync();
        (await ReviewAsync(key, id, ReviewDecisionType.Correct, "Filled from email", ("fields.dueDate", "2026-10-15"))).EnsureSuccessStatusCode();

        using (var scope = _factory.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
            await db.ApiRequests.Where(r => r.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ResultExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        await _factory.Services.GetRequiredService<RetentionSweeper>().SweepAsync(1000, CancellationToken.None);

        using (var scope = _factory.CreateSystemScope())
        {
            var decision = await scope.ServiceProvider.GetRequiredService<HeliosDbContext>().ReviewDecisions.SingleAsync(d => d.ApiRequestId == id);
            Assert.Null(decision.CorrectionsJson);
            Assert.NotNull(decision.PurgedAt);
            Assert.Equal("Filled from email", decision.Reason);
        }

        Assert.Equal(HttpStatusCode.Gone, (await key.GetAsync($"/api/v1/requests/{id}/export")).StatusCode);
    }
}
