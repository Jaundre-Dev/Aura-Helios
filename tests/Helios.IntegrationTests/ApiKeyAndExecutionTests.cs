using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Helios.Contracts.ApiKeys;
using Helios.Contracts.Catalogue;
using Helios.Contracts.Organizations;
using Helios.Contracts.Requests;
using Helios.Infrastructure.Persistence.MySql;
using Helios.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.IntegrationTests;

/// <summary>
/// The P1 acceptance gate (HELIOS-IMPLEMENTATION-PLAN.md section 14): two companies independently
/// sign up and run the sandbox utility; keys cannot cross tenant, environment or product
/// boundaries; revoked keys fail immediately; finance users cannot read results.
/// </summary>
[Collection(HeliosApiCollection.Name)]
public sealed class ApiKeyAndExecutionTests(HeliosApiFactory factory)
{
    private readonly HeliosApiFactory _factory = factory;

    private const string ValidId = "8001015009087";
    private const string ExecuteUrl = $"/api/v1/products/{TestCompany.SaIdProduct}/requests";

    private static StringContent IdBody(string idNumber) =>
        new(JsonSerializer.Serialize(new { idNumber }), System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task Two_companies_independently_sign_up_and_run_the_sandbox_utility()
    {
        var alpha = await TestCompany.OnboardAsync(_factory);
        var beta = await TestCompany.OnboardAsync(_factory);

        foreach (var company in new[] { alpha, beta })
        {
            var key = await company.CreateKeyAsync();
            Assert.StartsWith("hk_test_", key.Secret);

            var response = await company.KeyClient(key.Secret).PostAsync(ExecuteUrl, IdBody(ValidId));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var envelope = (await response.Content.ReadFromJsonAsync<ApiRequestEnvelope>())!;
            Assert.Equal(TestCompany.SaIdProduct, envelope.Product);
            Assert.Equal(ApiEnvironment.Sandbox, envelope.Environment);
            Assert.Equal(ApiRequestStatus.Succeeded, envelope.Status);
            Assert.True(envelope.Result!.Value.GetProperty("valid").GetBoolean());
            Assert.Equal("1980-01-01", envelope.Result.Value.GetProperty("derived").GetProperty("dateOfBirth").GetString());
            Assert.Equal(new UsageInfo("request", 1m), envelope.Usage);
            Assert.Equal(new BillingInfo(BillingState.NotBillable, "ZAR", 0m), envelope.Billing);
        }

        using var scope = _factory.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
        Assert.Equal(1, await db.ApiRequests.CountAsync(r => r.OrganizationId == alpha.Organization.Id));
        Assert.Equal(1, await db.ApiRequests.CountAsync(r => r.OrganizationId == beta.Organization.Id));
    }

    [Fact]
    public async Task A_bad_checksum_is_a_successful_execution_with_a_negative_result()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var key = await company.CreateKeyAsync();

        var response = await company.KeyClient(key.Secret).PostAsync(ExecuteUrl, IdBody("8001015009088"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = (await response.Content.ReadFromJsonAsync<ApiRequestEnvelope>())!;
        Assert.False(envelope.Result!.Value.GetProperty("valid").GetBoolean());
        Assert.Equal("fail", envelope.Result.Value.GetProperty("checks").GetProperty("checksum").GetString());
    }

    [Fact]
    public async Task The_raw_id_number_is_never_stored()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var key = await company.CreateKeyAsync();

        var response = await company.KeyClient(key.Secret).PostAsync(ExecuteUrl, IdBody(ValidId));
        var envelope = (await response.Content.ReadFromJsonAsync<ApiRequestEnvelope>())!;

        using var scope = _factory.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
        var row = await db.ApiRequests.SingleAsync(r => r.Id == envelope.RequestId);

        Assert.DoesNotContain(ValidId, row.ResultJson);
        Assert.DoesNotContain(ValidId, row.PayloadFingerprint);
        Assert.Equal(64, row.PayloadFingerprint.Length);
    }

    [Fact]
    public async Task A_key_cannot_read_another_companys_request()
    {
        var alpha = await TestCompany.OnboardAsync(_factory);
        var beta = await TestCompany.OnboardAsync(_factory);
        var alphaKey = await alpha.CreateKeyAsync();
        var betaKey = await beta.CreateKeyAsync();

        var run = await alpha.KeyClient(alphaKey.Secret).PostAsync(ExecuteUrl, IdBody(ValidId));
        var requestId = (await run.Content.ReadFromJsonAsync<ApiRequestEnvelope>())!.RequestId;

        var betaClient = beta.KeyClient(betaKey.Secret);
        Assert.Equal(HttpStatusCode.NotFound, (await betaClient.GetAsync($"/api/v1/requests/{requestId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await betaClient.GetAsync($"/api/v1/requests/{requestId}/result")).StatusCode);
        Assert.Empty((await betaClient.GetFromJsonAsync<List<ApiRequestSummary>>("/api/v1/requests"))!);

        // A signed-in member of the other company cannot see it either.
        Assert.Equal(HttpStatusCode.NotFound, (await beta.Owner.Client.GetAsync($"/api/v1/requests/{requestId}")).StatusCode);
    }

    [Fact]
    public async Task A_sandbox_key_cannot_act_in_live()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var key = await company.CreateKeyAsync();

        var response = await company.KeyClient(key.Secret).PostAsync($"{ExecuteUrl}?environment=Live", IdBody(ValidId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("key_environment_mismatch", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_live_key_cannot_be_created_without_a_live_entitlement()
    {
        var company = await TestCompany.OnboardAsync(_factory);

        var response = await company.Owner.Client.PostAsJsonAsync("/api/v1/api-keys",
            new CreateApiKeyRequest("live", ApiEnvironment.Live, [TestCompany.SaIdProduct]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("product_not_enabled", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_key_cannot_call_a_product_outside_its_scope()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var key = await company.CreateKeyAsync();

        // Narrow the key to a different product directly: the only callable product today is
        // this one, so a second scope cannot be obtained through the API yet.
        using (var scope = _factory.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
            var row = await db.ApiKeys.SingleAsync(k => k.Id == key.Key.Id);
            row.Scopes = "ocr.general";
            await db.SaveChangesAsync();
        }

        var response = await company.KeyClient(key.Secret).PostAsync(ExecuteUrl, IdBody(ValidId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("key_scope", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_key_cannot_be_scoped_to_a_product_the_company_has_not_enabled()
    {
        var company = await TestCompany.OnboardAsync(_factory);

        var response = await company.Owner.Client.PostAsJsonAsync("/api/v1/api-keys",
            new CreateApiKeyRequest("ocr", ApiEnvironment.Sandbox, ["ocr.general"]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Disabling_the_entitlement_stops_existing_keys_from_calling_the_product()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var key = await company.CreateKeyAsync();
        var entitlements = await company.Owner.Client.GetFromJsonAsync<List<EntitlementResponse>>(
            $"/api/v1/organizations/{company.Organization.Id}/entitlements");

        (await company.Owner.Client.DeleteAsync(
            $"/api/v1/organizations/{company.Organization.Id}/entitlements/{entitlements!.Single().Id}")).EnsureSuccessStatusCode();

        var response = await company.KeyClient(key.Secret).PostAsync(ExecuteUrl, IdBody(ValidId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("product_not_enabled", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_revoked_key_fails_on_the_very_next_request()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var key = await company.CreateKeyAsync();
        var client = company.KeyClient(key.Secret);

        (await client.PostAsync(ExecuteUrl, IdBody(ValidId))).EnsureSuccessStatusCode();

        var revoke = await company.Owner.Client.PostAsync($"/api/v1/api-keys/{key.Key.Id}/revoke", null);
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(ExecuteUrl, IdBody(ValidId))).StatusCode);
    }

    [Fact]
    public async Task An_expired_key_is_rejected()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var key = await company.CreateKeyAsync();

        using (var scope = _factory.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
            var row = await db.ApiKeys.SingleAsync(k => k.Id == key.Key.Id);
            row.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await company.KeyClient(key.Secret).PostAsync(ExecuteUrl, IdBody(ValidId))).StatusCode);
    }

    [Fact]
    public async Task Rotation_issues_a_working_replacement_and_retires_the_original()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var original = await company.CreateKeyAsync();

        var rotate = await company.Owner.Client.PostAsync($"/api/v1/api-keys/{original.Key.Id}/rotate", null);
        var replacement = (await rotate.Content.ReadFromJsonAsync<CreatedApiKeyResponse>())!;

        Assert.NotEqual(original.Secret, replacement.Secret);
        Assert.Equal(original.Key.Scopes, replacement.Key.Scopes);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await company.KeyClient(original.Secret).PostAsync(ExecuteUrl, IdBody(ValidId))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await company.KeyClient(replacement.Secret).PostAsync(ExecuteUrl, IdBody(ValidId))).StatusCode);
    }

    [Theory]
    [InlineData("hk_test_aaaaaaaaaaaaaaaa_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")] // well formed, unknown
    [InlineData("hk_test_garbage")]
    public async Task An_unknown_or_malformed_key_is_unauthorized(string presented)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", presented);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(ExecuteUrl, IdBody(ValidId))).StatusCode);
    }

    [Fact]
    public async Task A_tampered_secret_with_a_real_public_id_is_unauthorized()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var key = await company.CreateKeyAsync();
        var tampered = key.Secret[..^2] + (key.Secret[^2..] == "AA" ? "BB" : "AA");

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await company.KeyClient(tampered).PostAsync(ExecuteUrl, IdBody(ValidId))).StatusCode);
    }

    [Fact]
    public async Task A_key_is_refused_on_portal_management_endpoints()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var key = await company.CreateKeyAsync();
        var client = company.KeyClient(key.Secret);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/organizations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/api-keys")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/v1/api-keys",
                new CreateApiKeyRequest("escalate", ApiEnvironment.Sandbox, [TestCompany.SaIdProduct]))).StatusCode);
    }

    [Fact]
    public async Task A_deactivated_company_s_keys_stop_working()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var key = await company.CreateKeyAsync();

        using (var scope = _factory.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
            var organization = await db.Organizations.SingleAsync(o => o.Id == company.Organization.Id);
            organization.IsActive = false;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await company.KeyClient(key.Secret).PostAsync(ExecuteUrl, IdBody(ValidId))).StatusCode);
    }

    [Fact]
    public async Task Finance_cannot_read_results_while_operators_can()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var key = await company.CreateKeyAsync();
        var run = await company.KeyClient(key.Secret).PostAsync(ExecuteUrl, IdBody(ValidId));
        var requestId = (await run.Content.ReadFromJsonAsync<ApiRequestEnvelope>())!.RequestId;

        var finance = await company.AddMemberAsync(OrganizationRole.Finance);
        var developer = await company.AddMemberAsync(OrganizationRole.Developer);
        var operatorUser = await company.AddMemberAsync(OrganizationRole.Operator);

        Assert.Equal(HttpStatusCode.Forbidden, (await finance.Client.GetAsync($"/api/v1/requests/{requestId}/result")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.Client.GetAsync($"/api/v1/requests/{requestId}")).StatusCode);

        // Developers see redacted diagnostics, never the payload.
        Assert.Equal(HttpStatusCode.OK, (await developer.Client.GetAsync($"/api/v1/requests/{requestId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await developer.Client.GetAsync($"/api/v1/requests/{requestId}/result")).StatusCode);

        var result = await operatorUser.Client.GetFromJsonAsync<ApiRequestEnvelope>($"/api/v1/requests/{requestId}/result");
        Assert.True(result!.Result!.Value.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task An_operator_runs_the_product_from_the_portal_through_the_same_path()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var operatorUser = await company.AddMemberAsync(OrganizationRole.Operator);
        var finance = await company.AddMemberAsync(OrganizationRole.Finance);

        var run = await operatorUser.Client.PostAsync(ExecuteUrl, IdBody(ValidId));
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);

        var envelope = (await run.Content.ReadFromJsonAsync<ApiRequestEnvelope>())!;
        var summary = await operatorUser.Client.GetFromJsonAsync<ApiRequestSummary>($"/api/v1/requests/{envelope.RequestId}");
        Assert.Equal("portal", summary!.Channel);
        Assert.Equal(operatorUser.UserId, summary.ActorUserId);

        // Finance cannot execute products.
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.Client.PostAsync(ExecuteUrl, IdBody(ValidId))).StatusCode);
    }

    [Fact]
    public async Task The_same_idempotency_key_replays_and_a_different_input_conflicts()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var key = await company.CreateKeyAsync();
        var client = company.KeyClient(key.Secret);
        var idempotencyKey = $"order-{Guid.NewGuid():N}";

        async Task<HttpResponseMessage> Send(string id)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, ExecuteUrl) { Content = IdBody(id) };
            message.Headers.Add("Idempotency-Key", idempotencyKey);
            return await client.SendAsync(message);
        }

        var first = await Send(ValidId);
        var second = await Send("800101 5009 087");   // same canonical input
        var different = await Send("8001015009088");

        var firstEnvelope = (await first.Content.ReadFromJsonAsync<ApiRequestEnvelope>())!;
        var secondEnvelope = (await second.Content.ReadFromJsonAsync<ApiRequestEnvelope>())!;

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(firstEnvelope.RequestId, secondEnvelope.RequestId);
        Assert.Equal("true", second.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.False(first.Headers.Contains("Idempotent-Replayed"));

        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
        Assert.Contains("idempotency_key_reused", await different.Content.ReadAsStringAsync());

        using var scope = _factory.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
        Assert.Equal(1, await db.ApiRequests.CountAsync(r => r.IdempotencyKey == idempotencyKey));
    }

    [Fact]
    public async Task Concurrent_requests_with_one_idempotency_key_record_once()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var key = await company.CreateKeyAsync();
        var idempotencyKey = $"burst-{Guid.NewGuid():N}";

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
        {
            var message = new HttpRequestMessage(HttpMethod.Post, ExecuteUrl) { Content = IdBody(ValidId) };
            message.Headers.Add("Idempotency-Key", idempotencyKey);
            return company.KeyClient(key.Secret).SendAsync(message);
        }));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var ids = await Task.WhenAll(responses.Select(async r => (await r.Content.ReadFromJsonAsync<ApiRequestEnvelope>())!.RequestId));
        Assert.Single(ids.Distinct());

        using var scope = _factory.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
        Assert.Equal(1, await db.ApiRequests.CountAsync(r => r.IdempotencyKey == idempotencyKey));
    }

    [Theory]
    [InlineData("""{"idNumber":8001015009087}""")]
    [InlineData("""{"id":"8001015009087"}""")]
    [InlineData("""{"idNumber":"8001015009087","extra":true}""")]
    [InlineData("""["8001015009087"]""")]
    [InlineData("""not json""")]
    public async Task Invalid_input_is_rejected_and_nothing_is_recorded(string body)
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var key = await company.CreateKeyAsync();

        var response = await company.KeyClient(key.Secret).PostAsync(ExecuteUrl,
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = _factory.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
        Assert.False(await db.ApiRequests.AnyAsync(r => r.OrganizationId == company.Organization.Id));
    }

    [Fact]
    public async Task A_body_over_the_product_limit_is_rejected_before_processing()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var key = await company.CreateKeyAsync();
        var padded = $$"""{"idNumber":"{{ValidId}}"{{new string(' ', 2048)}}}""";

        var response = await company.KeyClient(key.Secret).PostAsync(ExecuteUrl,
            new StringContent(padded, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task A_planned_product_cannot_be_executed()
    {
        var company = await TestCompany.OnboardAsync(_factory);

        var response = await company.Owner.Client.PostAsync("/api/v1/products/ocr.general/requests",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("product_unavailable", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Key_listings_never_include_the_secret_or_its_hash()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var key = await company.CreateKeyAsync();

        var listing = await company.Owner.Client.GetStringAsync("/api/v1/api-keys");

        Assert.Contains(key.Key.Prefix, listing);
        Assert.DoesNotContain(key.Secret, listing);
        Assert.DoesNotContain("secretHash", listing, StringComparison.OrdinalIgnoreCase);
    }
}
