using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Helios.Contracts.Catalogue;
using Helios.Contracts.Organizations;
using Helios.IntegrationTests.Fixtures;

namespace Helios.IntegrationTests;

/// <summary>The published contract (anonymous, with both auth schemes, without staff routes) and finance CSV exports.</summary>
[Collection(HeliosApiCollection.Name)]
public sealed class ApiDocsAndExportTests(HeliosApiFactory factory)
{
    private readonly HeliosApiFactory _factory = factory;

    [Fact]
    public async Task The_contract_is_published_with_both_auth_schemes_and_no_staff_routes()
    {
        var response = await _factory.CreateClient().GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var spec = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = spec.RootElement;

        Assert.Equal("AURA HELIOS API", root.GetProperty("info").GetProperty("title").GetString());

        var paths = root.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("/api/v1/products/{slug}/requests", paths);
        Assert.Contains("/api/v1/requests/{id}/review", paths);
        Assert.Contains("/api/v1/auth/mfa/login", paths);
        Assert.DoesNotContain(paths, p => p.StartsWith("/api/v1/platform/", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, p => p.StartsWith("/api/v1/payments/callbacks", StringComparison.Ordinal));

        var schemes = root.GetProperty("components").GetProperty("securitySchemes");
        Assert.Equal("bearer", schemes.GetProperty("portal").GetProperty("scheme").GetString());
        Assert.Equal("X-Api-Key", schemes.GetProperty("apiKey").GetProperty("name").GetString());
    }

    private static Task<HttpResponseMessage> RunMeteredAsync(HttpClient client, decimal units) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/v1/products/{TestProducts.Metered}/requests")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { units }), System.Text.Encoding.UTF8, "application/json"),
            Headers = { { "Idempotency-Key", $"k-{Guid.NewGuid():N}" } }
        });

    [Fact]
    public async Task Finance_exports_transactions_and_usage_as_csv()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var finance = await company.AddMemberAsync(OrganizationRole.Finance);
        var developer = await company.AddMemberAsync(OrganizationRole.Developer);
        var key = company.KeyClient((await company.CreateLiveKeyAsync(TestProducts.Metered)).Secret);
        await _factory.FundAsync(company.Organization.Id, 50m);

        (await RunMeteredAsync(key, 2m)).EnsureSuccessStatusCode();
        (await RunMeteredAsync(key, 3m)).EnsureSuccessStatusCode();

        var baseUrl = $"/api/v1/organizations/{company.Organization.Id}/billing/exports";

        var transactions = await finance.Client.GetAsync($"{baseUrl}/transactions.csv");
        Assert.Equal("text/csv", transactions.Content.Headers.ContentType!.MediaType);
        var lines = (await transactions.Content.ReadAsStringAsync()).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("created_at,type,description,available_change,reserved_change,currency,api_request_id,payment_id,transaction_id", lines[0]);
        Assert.Contains(lines, l => l.Contains(",Adjustment,") && l.Contains(",50"));
        Assert.Equal(2, lines.Count(l => l.Contains(",Settle,")));
        Assert.Contains(lines, l => l.Contains(",Reserve,") && l.Contains(",-2"));

        var usage = (await finance.Client.GetStringAsync($"{baseUrl}/usage.csv")).TrimStart('﻿')
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, usage.Length);
        Assert.Contains(usage.Skip(1).Select(l => l.Split(',')), c =>
            c[1] == TestProducts.Metered && c[3] == "Live" && decimal.Parse(c[5], System.Globalization.CultureInfo.InvariantCulture) == 3m &&
            decimal.Parse(c[6], System.Globalization.CultureInfo.InvariantCulture) == 3m);

        Assert.Equal(HttpStatusCode.Forbidden, (await developer.Client.GetAsync($"{baseUrl}/usage.csv")).StatusCode);

        var from = DateTimeOffset.UtcNow.ToString("O");
        var backwards = await finance.Client.GetAsync($"{baseUrl}/usage.csv?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"))}");
        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);
    }
}
