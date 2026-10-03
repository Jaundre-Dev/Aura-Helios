using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Helios.Application.Features.Billing;
using Helios.Contracts.ApiKeys;
using Helios.Contracts.Billing;
using Helios.Contracts.Catalogue;
using Helios.Contracts.Organizations;
using Helios.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.IntegrationTests;

/// <summary>
/// Monthly caps per API key and per company, enforced at acceptance under the wallet lock (so
/// concurrency cannot exceed them), and per-credential rate limits on billable endpoints.
/// </summary>
[Collection(HeliosApiCollection.Name)]
public sealed class SpendAndRateLimitTests(HeliosApiFactory factory)
{
    private readonly HeliosApiFactory _factory = factory;

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())?.Extensions.TryGetValue("code", out var code) == true
            ? code?.ToString()
            : null;

    private static Task<HttpResponseMessage> RunAsync(HttpClient client, decimal units = 1)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/products/{TestProducts.Metered}/requests")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { units }), System.Text.Encoding.UTF8, "application/json")
        };
        message.Headers.Add("Idempotency-Key", $"k-{Guid.NewGuid():N}");
        return client.SendAsync(message);
    }

    private static async Task<CreatedApiKeyResponse> LiveKeyAsync(TestCompany company, decimal? budget)
    {
        var response = await company.Owner.Client.PostAsJsonAsync("/api/v1/api-keys",
            new CreateApiKeyRequest($"k {Guid.NewGuid():N}", ApiEnvironment.Live, [TestProducts.Metered], MonthlyBudget: budget));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedApiKeyResponse>())!;
    }

    private async Task<TestCompany> FundedLiveCompanyAsync(decimal funds)
    {
        var company = await TestCompany.OnboardAsync(_factory);
        await company.EnableAsync(TestProducts.Metered, ApiEnvironment.Live);
        await _factory.FundAsync(company.Organization.Id, funds);
        return company;
    }

    [Fact]
    public async Task A_key_stops_at_its_monthly_budget_while_other_keys_carry_on()
    {
        var company = await FundedLiveCompanyAsync(100m);
        var capped = await LiveKeyAsync(company, budget: 2m);
        Assert.Equal(2m, capped.Key.MonthlyBudget);
        var uncapped = await LiveKeyAsync(company, budget: null);

        Assert.Equal(HttpStatusCode.OK, (await RunAsync(company.KeyClient(capped.Secret))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RunAsync(company.KeyClient(capped.Secret))).StatusCode);

        var refused = await RunAsync(company.KeyClient(capped.Secret));
        Assert.Equal(HttpStatusCode.PaymentRequired, refused.StatusCode);
        Assert.Equal("key_budget_reached", await CodeOf(refused));

        Assert.Equal(HttpStatusCode.OK, (await RunAsync(company.KeyClient(uncapped.Secret))).StatusCode);

        // Raising the budget lets the key continue at once.
        (await company.Owner.Client.PutAsJsonAsync($"/api/v1/api-keys/{capped.Key.Id}/budget", new UpdateKeyBudgetRequest(3m))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await RunAsync(company.KeyClient(capped.Secret))).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await company.Owner.Client.PutAsJsonAsync($"/api/v1/api-keys/{capped.Key.Id}/budget", new UpdateKeyBudgetRequest(-1m))).StatusCode);
    }

    [Fact]
    public async Task Concurrent_requests_cannot_exceed_a_budget()
    {
        var company = await FundedLiveCompanyAsync(100m);
        var key = await LiveKeyAsync(company, budget: 5m);
        var client = company.KeyClient(key.Secret);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => RunAsync(client)));

        Assert.Equal(5, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(15, results.Count(r => r.StatusCode == HttpStatusCode.PaymentRequired));

        using var scope = _factory.CreateSystemScope();
        var balance = await scope.ServiceProvider.GetRequiredService<LedgerService>().GetBalanceAsync(company.Organization.Id, CancellationToken.None);
        Assert.Equal(95m, balance.Available);
    }

    [Fact]
    public async Task A_company_spend_limit_covers_every_key_and_only_billing_roles_set_it()
    {
        var company = await FundedLiveCompanyAsync(100m);
        var developer = await company.AddMemberAsync(OrganizationRole.Developer);
        var url = $"/api/v1/organizations/{company.Organization.Id}/billing/spend-limit";

        Assert.Equal(HttpStatusCode.Forbidden, (await developer.Client.PutAsJsonAsync(url, new UpdateSpendLimitRequest(3m))).StatusCode);
        (await company.Owner.Client.PutAsJsonAsync(url, new UpdateSpendLimitRequest(3m))).EnsureSuccessStatusCode();

        var first = await LiveKeyAsync(company, null);
        var second = await LiveKeyAsync(company, null);

        Assert.Equal(HttpStatusCode.OK, (await RunAsync(company.KeyClient(first.Secret), units: 2)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RunAsync(company.KeyClient(second.Secret))).StatusCode);

        var refused = await RunAsync(company.KeyClient(second.Secret));
        Assert.Equal(HttpStatusCode.PaymentRequired, refused.StatusCode);
        Assert.Equal("spend_limit_reached", await CodeOf(refused));

        var status = await company.Owner.Client.GetFromJsonAsync<SpendLimitResponse>(url);
        Assert.Equal((3m, 3m), (status!.MonthlySpendLimit, status.Committed));

        // Clearing the limit removes the cap.
        (await company.Owner.Client.PutAsJsonAsync(url, new UpdateSpendLimitRequest(null))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await RunAsync(company.KeyClient(second.Secret))).StatusCode);
    }

    [Fact]
    public async Task Each_credential_is_rate_limited_separately()
    {
        var limited = _factory.WithWebHostBuilder(builder => builder.UseSetting("Helios:RateLimits:Products:PermitLimit", "3"));
        var company = await TestCompany.OnboardAsync(limited);
        var first = company.KeyClient((await company.CreateKeyAsync()).Secret);
        var second = company.KeyClient((await company.CreateKeyAsync()).Secret);

        async Task<HttpResponseMessage> Validate(HttpClient client) =>
            await client.PostAsync($"/api/v1/products/{TestCompany.SaIdProduct}/requests",
                new StringContent("""{"idNumber":"8001015009087"}""", System.Text.Encoding.UTF8, "application/json"));

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await Validate(first)).StatusCode);
        }

        var throttled = await Validate(first);
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.True(throttled.Headers.Contains("Retry-After"));

        Assert.Equal(HttpStatusCode.OK, (await Validate(second)).StatusCode);
    }
}
