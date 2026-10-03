using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Helios.Contracts.Billing;
using Helios.Contracts.Organizations;
using Helios.Infrastructure.Payments;
using Helios.Infrastructure.Persistence.MySql;
using Helios.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.IntegrationTests;

/// <summary>
/// P2 payment gate: credit only from a verified callback; duplicate, concurrent, stale, forged and
/// mismatched callbacks never credit (or never credit twice); finance roles only.
/// </summary>
[Collection(HeliosApiCollection.Name)]
public sealed class PaymentTests(HeliosApiFactory factory)
{
    private readonly HeliosApiFactory _factory = factory;
    private const string CallbackUrl = "/api/v1/payments/callbacks/fake-test";

    private async Task<(TestCompany Company, PaymentResponse Payment)> StartTopUpAsync(decimal amount = 250m)
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var response = await company.Owner.Client.PostAsJsonAsync(
            $"/api/v1/organizations/{company.Organization.Id}/billing/top-ups", new CreateTopUpRequest(amount));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (company, (await response.Content.ReadFromJsonAsync<PaymentResponse>())!);
    }

    private static string Reference(PaymentResponse payment) => payment.CheckoutUrl!.Split('/').Last();

    private static string Body(string type, string reference, decimal amount, string? id = null,
        string currency = "ZAR", string merchant = HeliosApiFactory.FakeGatewayMerchant) =>
        JsonSerializer.Serialize(new
        {
            id = id ?? $"evt_{Guid.NewGuid():N}",
            type,
            reference,
            amount = amount.ToString(CultureInfo.InvariantCulture),
            currency,
            merchant,
            occurredAt = DateTimeOffset.UtcNow
        });

    private Task<HttpResponseMessage> CallbackAsync(string body, string secret = HeliosApiFactory.FakeGatewaySecret, DateTimeOffset? at = null)
    {
        var timestamp = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var message = new HttpRequestMessage(HttpMethod.Post, CallbackUrl)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        message.Headers.Add(FakeTestPaymentGateway.SignatureHeader, $"t={timestamp},v1={FakeTestPaymentGateway.Sign(secret, body, timestamp)}");
        return _factory.CreateClient().SendAsync(message);
    }

    private async Task<BalanceResponse> BalanceAsync(TestCompany company) =>
        (await company.Owner.Client.GetFromJsonAsync<BalanceResponse>(
            $"/api/v1/organizations/{company.Organization.Id}/billing/balance"))!;

    [Fact]
    public async Task Starting_a_top_up_credits_nothing_until_the_gateway_confirms()
    {
        var (company, payment) = await StartTopUpAsync();

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.StartsWith("https://payments.invalid/", payment.CheckoutUrl);
        Assert.Equal(new BalanceResponse("ZAR", 0m, 0m, 0m), await BalanceAsync(company));

        var callback = await CallbackAsync(Body("payment.succeeded", Reference(payment), 250m));
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);

        Assert.Equal(new BalanceResponse("ZAR", 250m, 0m, 250m), await BalanceAsync(company));
        var payments = await company.Owner.Client.GetFromJsonAsync<List<PaymentResponse>>(
            $"/api/v1/organizations/{company.Organization.Id}/billing/payments");
        Assert.Equal(PaymentStatus.Succeeded, Assert.Single(payments!).Status);
    }

    [Fact]
    public async Task A_replayed_callback_credits_once()
    {
        var (company, payment) = await StartTopUpAsync();
        var body = Body("payment.succeeded", Reference(payment), 250m);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await CallbackAsync(body)).StatusCode);
        }

        // A second, distinct success event for the same payment also has no further effect.
        Assert.Equal(HttpStatusCode.OK, (await CallbackAsync(Body("payment.succeeded", Reference(payment), 250m))).StatusCode);

        Assert.Equal(250m, (await BalanceAsync(company)).Available);
    }

    [Fact]
    public async Task Concurrent_deliveries_credit_once()
    {
        var (company, payment) = await StartTopUpAsync();
        var body = Body("payment.succeeded", Reference(payment), 250m);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            CallbackAsync(i % 2 == 0 ? body : Body("payment.succeeded", Reference(payment), 250m))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(250m, (await BalanceAsync(company)).Available);

        using var scope = _factory.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
        Assert.Equal(1, await db.LedgerTransactions.CountAsync(t => t.PaymentId == payment.Id));
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("merchant")]
    [InlineData("reference")]
    public async Task An_authentic_but_mismatched_callback_is_rejected(string mismatch)
    {
        var (company, payment) = await StartTopUpAsync();

        var body = mismatch switch
        {
            "amount" => Body("payment.succeeded", Reference(payment), 2500m),
            "currency" => Body("payment.succeeded", Reference(payment), 250m, currency: "USD"),
            "merchant" => Body("payment.succeeded", Reference(payment), 250m, merchant: "someone-else"),
            _ => Body("payment.succeeded", "fake_unknown", 250m)
        };

        Assert.Equal(HttpStatusCode.OK, (await CallbackAsync(body)).StatusCode);
        Assert.Equal(0m, (await BalanceAsync(company)).Available);

        using var scope = _factory.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
        var eventId = JsonDocument.Parse(body).RootElement.GetProperty("id").GetString();
        var recorded = await db.PaymentEvents.SingleAsync(e => e.EventId == eventId);
        Assert.Equal(PaymentEventOutcome.Rejected, recorded.Outcome);
    }

    [Fact]
    public async Task A_forged_or_stale_callback_is_unauthorized_and_changes_nothing()
    {
        var (company, payment) = await StartTopUpAsync();
        var body = Body("payment.succeeded", Reference(payment), 250m);

        var forged = await CallbackAsync(body, secret: "an-attacker-guess-at-the-signing-secret!!");
        var stale = await CallbackAsync(body, at: DateTimeOffset.UtcNow.AddMinutes(-30));

        var unsigned = await _factory.CreateClient().PostAsync(CallbackUrl,
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
        Assert.Equal(0m, (await BalanceAsync(company)).Available);

        using var scope = _factory.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
        Assert.False(await db.PaymentEvents.AnyAsync(e => e.PaymentId == payment.Id));
    }

    [Fact]
    public async Task A_failure_arriving_after_success_is_ignored()
    {
        var (company, payment) = await StartTopUpAsync();

        await CallbackAsync(Body("payment.succeeded", Reference(payment), 250m));
        await CallbackAsync(Body("payment.failed", Reference(payment), 250m));

        Assert.Equal(250m, (await BalanceAsync(company)).Available);
        var payments = await company.Owner.Client.GetFromJsonAsync<List<PaymentResponse>>(
            $"/api/v1/organizations/{company.Organization.Id}/billing/payments");
        Assert.Equal(PaymentStatus.Succeeded, payments!.Single().Status);
    }

    [Fact]
    public async Task A_refund_is_recorded_for_review_without_moving_money()
    {
        var (company, payment) = await StartTopUpAsync();
        await CallbackAsync(Body("payment.succeeded", Reference(payment), 250m));

        var refund = Body("payment.refunded", Reference(payment), 250m);
        await CallbackAsync(refund);

        Assert.Equal(250m, (await BalanceAsync(company)).Available);

        using var scope = _factory.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
        var eventId = JsonDocument.Parse(refund).RootElement.GetProperty("id").GetString();
        Assert.Equal(PaymentEventOutcome.NeedsReview, (await db.PaymentEvents.SingleAsync(e => e.EventId == eventId)).Outcome);
    }

    [Fact]
    public async Task Only_billing_roles_start_top_ups_or_see_balances()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var developer = await company.AddMemberAsync(OrganizationRole.Developer);
        var finance = await company.AddMemberAsync(OrganizationRole.Finance);
        var stranger = await TestAccount.RegisterAsync(_factory);
        var billing = $"/api/v1/organizations/{company.Organization.Id}/billing";

        Assert.Equal(HttpStatusCode.Forbidden,
            (await developer.Client.PostAsJsonAsync($"{billing}/top-ups", new CreateTopUpRequest(100m))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await developer.Client.GetAsync($"{billing}/balance")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.GetAsync($"{billing}/balance")).StatusCode);

        Assert.Equal(HttpStatusCode.Created,
            (await finance.Client.PostAsJsonAsync($"{billing}/top-ups", new CreateTopUpRequest(100m))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await finance.Client.GetAsync($"{billing}/balance")).StatusCode);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(100_001)]
    [InlineData(10.005)]
    public async Task Top_up_amounts_are_bounded(decimal amount)
    {
        var company = await TestCompany.OnboardAsync(_factory);

        var response = await company.Owner.Client.PostAsJsonAsync(
            $"/api/v1/organizations/{company.Organization.Id}/billing/top-ups", new CreateTopUpRequest(amount));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Funds_from_a_verified_top_up_pay_for_live_usage_visible_in_usage_and_transactions()
    {
        var (company, payment) = await StartTopUpAsync(100m);
        await CallbackAsync(Body("payment.succeeded", Reference(payment), 100m));

        var key = await company.CreateLiveKeyAsync(TestProducts.Metered);
        var message = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/products/{TestProducts.Metered}/requests")
        {
            Content = new StringContent("""{"units":4}""", System.Text.Encoding.UTF8, "application/json")
        };
        message.Headers.Add("Idempotency-Key", $"use-{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.OK, (await company.KeyClient(key.Secret).SendAsync(message)).StatusCode);

        var billing = $"/api/v1/organizations/{company.Organization.Id}/billing";
        Assert.Equal(new BalanceResponse("ZAR", 96m, 0m, 96m), await BalanceAsync(company));

        var usage = await company.Owner.Client.GetFromJsonAsync<UsageResponse>($"{billing}/usage");
        var line = Assert.Single(usage!.Lines);
        Assert.Equal(TestProducts.Metered, line.Product);
        Assert.Equal(4m, line.Quantity);
        Assert.Equal(4m, usage.Total);

        var transactions = (await company.Owner.Client.GetFromJsonAsync<List<LedgerTransactionResponse>>($"{billing}/transactions"))!;
        Assert.Equal(
            [LedgerTransactionType.TopUp, LedgerTransactionType.Reserve, LedgerTransactionType.Settle],
            transactions.Select(t => t.Type).Order());
        Assert.Equal(100m, transactions.Single(t => t.Type == LedgerTransactionType.TopUp).AvailableChange);
        Assert.Equal(-4m, transactions.Single(t => t.Type == LedgerTransactionType.Reserve).AvailableChange);
    }
}
