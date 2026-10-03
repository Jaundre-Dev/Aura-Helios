using System.Net;
using System.Net.Http.Json;
using System.Text;
using Helios.Application.Features.Retention;
using Helios.Contracts.Catalogue;
using Helios.Contracts.Organizations;
using Helios.Contracts.Uploads;
using Helios.Infrastructure.Persistence.MySql;
using Helios.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.IntegrationTests;

/// <summary>
/// P3 upload gate: malicious and out-of-bounds files are refused before storage; uploads are
/// isolated by company, workspace and environment; documents are readable only by result readers;
/// deletion and retention remove content.
/// </summary>
[Collection(HeliosApiCollection.Name)]
public sealed class UploadTests(HeliosApiFactory factory)
{
    private readonly HeliosApiFactory _factory = factory;

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] content, string name = "doc.pdf",
        string type = "application/pdf", string query = "") =>
        await client.PostAsync($"/api/v1/uploads{query}", TestDocuments.Form(content, name, type));

    private static async Task<UploadResponse> UploadOkAsync(HttpClient client, byte[] content, string name = "doc.pdf", string type = "application/pdf")
    {
        var response = await UploadAsync(client, content, name, type);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UploadResponse>())!;
    }

    [Fact]
    public async Task A_text_pdf_is_accepted_with_detected_type_pages_and_text_layer()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var pdf = TestDocuments.TextPdf(["Page one"], ["Page two"]);

        var upload = await UploadOkAsync(company.Owner.Client, pdf, "../../etc/passwd invoice.pdf", "application/octet-stream");

        Assert.Equal("application/pdf", upload.MediaType);
        Assert.Equal(2, upload.PageCount);
        Assert.True(upload.HasTextLayer);
        Assert.Equal(ScanState.NotScanned, upload.ScanState);   // no scanner in the test host
        Assert.Equal(ApiEnvironment.Sandbox, upload.Environment);
        Assert.Equal("passwd invoice.pdf", upload.FileName);   // a display name, never a path

        var content = await company.Owner.Client.GetByteArrayAsync($"/api/v1/uploads/{upload.Id}/content");
        Assert.Equal(pdf, content);
    }

    [Fact]
    public async Task A_scanned_pdf_is_accepted_but_reported_as_having_no_text_layer()
    {
        var company = await TestCompany.OnboardAsync(_factory);

        var upload = await UploadOkAsync(company.Owner.Client, TestDocuments.ImageOnlyPdf());

        Assert.False(upload.HasTextLayer);
    }

    [Theory]
    [InlineData("active_content")]
    [InlineData("too_many_pages")]
    [InlineData("type_mismatch")]
    [InlineData("unsupported_type")]
    [InlineData("image_too_large")]
    [InlineData("malformed")]
    [InlineData("empty_file")]
    public async Task Unsafe_or_out_of_bounds_files_are_refused_and_never_stored(string expected)
    {
        var company = await TestCompany.OnboardAsync(_factory);

        var (bytes, type) = expected switch
        {
            "active_content" => (TestDocuments.JavaScriptPdf(), "application/pdf"),
            "too_many_pages" => (TestDocuments.ManyPagesPdf(51), "application/pdf"),
            "type_mismatch" => (TestDocuments.TextPdf(["x"]), "image/png"),
            "unsupported_type" => (Encoding.UTF8.GetBytes("#!/bin/sh\nrm -rf /\n"), "text/plain"),
            "image_too_large" => (TestDocuments.PngHeader(50_000, 50_000), "image/png"),
            "malformed" => ("%PDF-1.7\nthis is not a pdf"u8.ToArray(), "application/pdf"),
            _ => ([], "application/pdf")
        };

        var response = await UploadAsync(company.Owner.Client, bytes, type: type);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expected, await response.Content.ReadAsStringAsync());

        using var scope = _factory.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
        Assert.False(await db.Uploads.AnyAsync(u => u.OrganizationId == company.Organization.Id));
        Assert.True(await db.AuditLogs.AnyAsync(a =>
            a.OrganizationId == company.Organization.Id && a.Action == "upload.reject" && !a.Allowed));
    }

    [Fact]
    public async Task Hex_escaped_active_content_names_are_still_refused()
    {
        var company = await TestCompany.OnboardAsync(_factory);

        var response = await UploadAsync(company.Owner.Client, TestDocuments.EscapedJavaScriptPdf());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("active_content", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_reasonable_image_is_accepted()
    {
        var company = await TestCompany.OnboardAsync(_factory);

        var upload = await UploadOkAsync(company.Owner.Client, TestDocuments.PngHeader(1240, 1754), "scan.png", "image/png");

        Assert.Equal("image/png", upload.MediaType);
        Assert.Equal(1, upload.PageCount);
    }

    [Fact]
    public async Task Another_company_cannot_see_download_or_delete_an_upload()
    {
        var owner = await TestCompany.OnboardAsync(_factory);
        var other = await TestCompany.OnboardAsync(_factory);
        var upload = await UploadOkAsync(owner.Owner.Client, TestDocuments.TextPdf(["confidential"]));
        var otherKey = await other.CreateKeyAsync();

        foreach (var client in new[] { other.Owner.Client, other.KeyClient(otherKey.Secret) })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/uploads/{upload.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/uploads/{upload.Id}/content")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/v1/uploads/{upload.Id}")).StatusCode);
        }
    }

    [Fact]
    public async Task Finance_and_developers_cannot_download_documents()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var upload = await UploadOkAsync(company.Owner.Client, TestDocuments.TextPdf(["payslip"]));
        var finance = await company.AddMemberAsync(OrganizationRole.Finance);
        var developer = await company.AddMemberAsync(OrganizationRole.Developer);
        var operatorUser = await company.AddMemberAsync(OrganizationRole.Operator);

        Assert.Equal(HttpStatusCode.Forbidden, (await finance.Client.GetAsync($"/api/v1/uploads/{upload.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.Client.GetAsync($"/api/v1/uploads/{upload.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await developer.Client.GetAsync($"/api/v1/uploads/{upload.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await developer.Client.GetAsync($"/api/v1/uploads/{upload.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await operatorUser.Client.GetAsync($"/api/v1/uploads/{upload.Id}/content")).StatusCode);
    }

    [Fact]
    public async Task Deleting_an_upload_removes_its_content_but_keeps_the_record()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var upload = await UploadOkAsync(company.Owner.Client, TestDocuments.TextPdf(["delete me"]));

        Assert.Equal(HttpStatusCode.NoContent, (await company.Owner.Client.DeleteAsync($"/api/v1/uploads/{upload.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.Gone, (await company.Owner.Client.GetAsync($"/api/v1/uploads/{upload.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await company.Owner.Client.GetAsync($"/api/v1/uploads/{upload.Id}")).StatusCode);

        using var scope = _factory.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
        Assert.False(await db.StoredObjects.AnyAsync(o => o.WorkspaceId == company.Workspace.Id));
    }

    [Fact]
    public async Task Retention_purges_expired_documents_and_results()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var upload = await UploadOkAsync(company.Owner.Client, TestDocuments.TextPdf(["old"]));
        var key = await company.CreateKeyAsync();
        var run = await company.KeyClient(key.Secret).PostAsync($"/api/v1/products/{TestCompany.SaIdProduct}/requests",
            new StringContent("""{"idNumber":"8001015009087"}""", Encoding.UTF8, "application/json"));
        var requestId = (await run.Content.ReadFromJsonAsync<Contracts.Requests.ApiRequestEnvelope>())!.RequestId;

        using (var scope = _factory.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
            await db.Uploads.Where(u => u.Id == upload.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
            await db.ApiRequests.Where(r => r.Id == requestId).ExecuteUpdateAsync(s => s.SetProperty(r => r.ResultExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        var purged = await _factory.Services.GetRequiredService<RetentionSweeper>().SweepAsync(1000, CancellationToken.None);
        Assert.True(purged >= 2);

        using (var scope = _factory.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
            var row = await db.Uploads.SingleAsync(u => u.Id == upload.Id);
            Assert.NotNull(row.DeletedAt);
            Assert.Null(row.StorageRef);
            Assert.False(await db.StoredObjects.AnyAsync(o => o.WorkspaceId == company.Workspace.Id));
            Assert.Null((await db.ApiRequests.SingleAsync(r => r.Id == requestId)).ResultJson);
        }

        Assert.Equal(HttpStatusCode.Gone, (await company.Owner.Client.GetAsync($"/api/v1/requests/{requestId}/result")).StatusCode);
    }

    [Fact]
    public async Task Live_uploads_are_refused_without_a_malware_scanner()
    {
        var company = await TestCompany.OnboardAsync(_factory);

        var response = await UploadAsync(company.Owner.Client, TestDocuments.TextPdf(["live"]), query: "?environment=Live");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("scanner_unavailable", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task With_a_scanner_infected_files_are_refused_and_clean_ones_marked_scanned()
    {
        await using var clamd = new FakeClamd();
        var scanned = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Helios:Uploads:Scanner", "clamav");
            builder.UseSetting("Helios:Uploads:ClamAv:Host", "127.0.0.1");
            builder.UseSetting("Helios:Uploads:ClamAv:Port", clamd.Port.ToString());
        });

        var company = await TestCompany.OnboardAsync(scanned);

        // EICAR as a raw comment in an otherwise valid PDF: structurally fine, flagged by the scanner.
        var infected = await UploadAsync(company.Owner.Client,
            [.. TestDocuments.TextPdf(["harmless"]), .. Encoding.ASCII.GetBytes("\n%" + TestDocuments.Eicar + "\n")]);
        Assert.Equal(HttpStatusCode.BadRequest, infected.StatusCode);
        Assert.Contains("malware_detected", await infected.Content.ReadAsStringAsync());

        var clean = await UploadOkAsync(company.Owner.Client, TestDocuments.TextPdf(["clean"]));
        Assert.Equal(ScanState.Clean, clean.ScanState);
        Assert.Equal(2, clamd.Scans);

        var live = await UploadAsync(company.Owner.Client, TestDocuments.TextPdf(["live"]), query: "?environment=Live");
        Assert.Equal(HttpStatusCode.Created, live.StatusCode);
    }

    [Fact]
    public async Task A_sandbox_key_cannot_see_a_live_upload_in_its_own_workspace()
    {
        await using var clamd = new FakeClamd();
        var scanned = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Helios:Uploads:Scanner", "clamav");
            builder.UseSetting("Helios:Uploads:ClamAv:Host", "127.0.0.1");
            builder.UseSetting("Helios:Uploads:ClamAv:Port", clamd.Port.ToString());
        });

        var company = await TestCompany.OnboardAsync(scanned);
        var liveUpload = await (await UploadAsync(company.Owner.Client, TestDocuments.TextPdf(["live"]), query: "?environment=Live"))
            .Content.ReadFromJsonAsync<UploadResponse>();
        var sandboxKey = await company.CreateKeyAsync();

        Assert.Equal(HttpStatusCode.NotFound,
            (await company.KeyClient(sandboxKey.Secret).GetAsync($"/api/v1/uploads/{liveUpload!.Id}")).StatusCode);
    }
}
