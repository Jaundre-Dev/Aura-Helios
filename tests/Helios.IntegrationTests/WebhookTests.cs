using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Helios.Application.Features.Webhooks;
using Helios.Contracts.Organizations;
using Helios.Contracts.Requests;
using Helios.Contracts.Webhooks;
using Helios.Infrastructure.Webhooks;
using Helios.IntegrationTests.Fixtures;

namespace Helios.IntegrationTests;

/// <summary>
/// Signed customer webhooks: transactional outbox, redacted payloads, retries with dead letters,
/// tenant isolation, and SSRF protection at registration and at connection time.
/// </summary>
[Collection(HeliosApiCollection.Name)]
public sealed class WebhookTests(HeliosApiFactory factory)
{
    private readonly HeliosApiFactory _factory = factory;
    private const string ValidId = "8001015009087";

    private static string UniqueHost() => $"hooks-{Guid.NewGuid():N}.example.com";

    private async Task<CreatedWebhookResponse> RegisterAsync(TestCompany company, string host, params string[] events)
    {
        var response = await company.Owner.Client.PostAsJsonAsync("/api/v1/webhooks",
            new CreateWebhookRequest($"https://{host}/helios", events.Length == 0 ? [WebhookEventTypes.RequestSucceeded] : events));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedWebhookResponse>())!;
    }

    private static async Task<ApiRequestEnvelope> RunSandboxAsync(TestCompany company)
    {
        var key = await company.CreateKeyAsync();
        var response = await company.KeyClient(key.Secret).PostAsync(
            $"/api/v1/products/{TestCompany.SaIdProduct}/requests",
            new StringContent(JsonSerializer.Serialize(new { idNumber = ValidId }), System.Text.Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiRequestEnvelope>())!;
    }

    [Fact]
    public async Task A_finished_request_is_delivered_signed_and_without_its_result()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var host = UniqueHost();
        var webhook = await RegisterAsync(company, host);
        Assert.StartsWith("whsec_", webhook.SigningSecret);

        var envelope = await RunSandboxAsync(company);
        await _factory.DrainWebhooksAsync();

        var received = Assert.Single(_factory.WebhookReceiver.For(host));
        var signature = received.Headers["Helios-Signature"];
        var timestamp = long.Parse(signature.Split(',')[0][2..]);
        Assert.Equal($"t={timestamp},v1={WebhookDispatcher.Sign(webhook.SigningSecret, received.Body, timestamp)}", signature);
        Assert.Equal(WebhookEventTypes.RequestSucceeded, received.Headers["Helios-Event-Type"]);

        using var body = JsonDocument.Parse(received.Body);
        Assert.Equal(envelope.RequestId, body.RootElement.GetProperty("data").GetProperty("requestId").GetGuid());
        Assert.Equal(received.Headers["Helios-Event-Id"], body.RootElement.GetProperty("id").GetString());
        Assert.DoesNotContain(ValidId, received.Body);
        Assert.DoesNotContain("dateOfBirth", received.Body);

        var deliveries = await company.Owner.Client.GetFromJsonAsync<List<WebhookDeliveryResponse>>(
            $"/api/v1/webhooks/{webhook.Endpoint.Id}/deliveries");
        var delivery = Assert.Single(deliveries!);
        Assert.Equal("Delivered", delivery.Status);
        Assert.Equal(200, delivery.LastStatusCode);
    }

    [Fact]
    public async Task Unsubscribed_events_are_not_sent()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var host = UniqueHost();
        await RegisterAsync(company, host, WebhookEventTypes.RequestFailed);

        await RunSandboxAsync(company);
        await _factory.DrainWebhooksAsync();

        Assert.Empty(_factory.WebhookReceiver.For(host));
    }

    [Fact]
    public async Task A_failing_endpoint_is_retried_then_dead_lettered()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var host = UniqueHost();
        _factory.WebhookReceiver.StatusByHost[host] = HttpStatusCode.InternalServerError;
        var webhook = await RegisterAsync(company, host);

        await RunSandboxAsync(company);
        await _factory.DrainWebhooksAsync();

        Assert.Equal(3, _factory.WebhookReceiver.For(host).Count);   // MaxAttempts in the test host
        var delivery = Assert.Single((await company.Owner.Client.GetFromJsonAsync<List<WebhookDeliveryResponse>>(
            $"/api/v1/webhooks/{webhook.Endpoint.Id}/deliveries"))!);
        Assert.Equal("Failed", delivery.Status);
        Assert.Equal(3, delivery.Attempts);
        Assert.Equal(500, delivery.LastStatusCode);
    }

    [Fact]
    public async Task A_deactivated_endpoint_receives_nothing_further()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var host = UniqueHost();
        var webhook = await RegisterAsync(company, host);

        await RunSandboxAsync(company);
        (await company.Owner.Client.DeleteAsync($"/api/v1/webhooks/{webhook.Endpoint.Id}")).EnsureSuccessStatusCode();
        await RunSandboxAsync(company);
        await _factory.DrainWebhooksAsync();

        Assert.Empty(_factory.WebhookReceiver.For(host));
    }

    [Fact]
    public async Task Another_company_cannot_see_endpoints_or_deliveries()
    {
        var owner = await TestCompany.OnboardAsync(_factory);
        var other = await TestCompany.OnboardAsync(_factory);
        var webhook = await RegisterAsync(owner, UniqueHost());

        Assert.Empty((await other.Owner.Client.GetFromJsonAsync<List<WebhookEndpointResponse>>("/api/v1/webhooks"))!);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.Owner.Client.GetAsync($"/api/v1/webhooks/{webhook.Endpoint.Id}/deliveries")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.Owner.Client.DeleteAsync($"/api/v1/webhooks/{webhook.Endpoint.Id}")).StatusCode);
    }

    [Fact]
    public async Task Finance_and_api_keys_cannot_manage_webhooks()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var finance = await company.AddMemberAsync(OrganizationRole.Finance);
        var key = await company.CreateKeyAsync();
        var request = new CreateWebhookRequest($"https://{UniqueHost()}/x", [WebhookEventTypes.RequestSucceeded]);

        Assert.Equal(HttpStatusCode.Forbidden, (await finance.Client.PostAsJsonAsync("/api/v1/webhooks", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await company.KeyClient(key.Secret).PostAsJsonAsync("/api/v1/webhooks", request)).StatusCode);
    }

    [Theory]
    [InlineData("http://hooks.example.com/x")]
    [InlineData("https://127.0.0.1/x")]
    [InlineData("https://10.1.2.3/x")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://[::1]/x")]
    [InlineData("https://localhost/x")]
    [InlineData("https://db.internal/x")]
    [InlineData("https://user:secret@hooks.example.com/x")]
    [InlineData("not a url")]
    public async Task Unsafe_destinations_are_refused_at_registration(string url)
    {
        var company = await TestCompany.OnboardAsync(_factory);

        var response = await company.Owner.Client.PostAsJsonAsync("/api/v1/webhooks",
            new CreateWebhookRequest(url, [WebhookEventTypes.RequestSucceeded]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_webhook_url", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("http://127.0.0.1:9/hook")]
    [InlineData("http://localhost:9/hook")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    public async Task The_real_sender_refuses_to_connect_to_internal_addresses(string url)
    {
        // The production handler, not the test receiver: its connect-time check runs after DNS
        // resolution, so a public-looking name that resolves inward is refused just the same.
        using var client = new HttpClient(GuardedWebhookSender.CreateGuardedHandler(allowPrivateNetworks: false));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.PostAsync(url, new StringContent("{}")));

        Assert.Contains("does not deliver to", error.Message + error.InnerException?.Message);
    }
}
