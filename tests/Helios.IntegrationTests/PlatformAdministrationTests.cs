using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Helios.Api.Security;
using Helios.Application.Features.Billing;
using Helios.Application.Features.Platform;
using Helios.Contracts.Billing;
using Helios.Contracts.Catalogue;
using Helios.Contracts.Platform;
using Helios.Contracts.Requests;
using Helios.Contracts.Webhooks;
using Helios.Domain.Catalogue;
using Helios.Domain.Execution;
using Helios.Infrastructure.Persistence.MySql;
using Helios.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.IntegrationTests;

/// <summary>
/// Platform administration: only a step-up (TOTP) staff session reaches it; codes cannot be replayed
/// or guessed; roles separate money, staff and approvals; no one changes their own record; and each
/// intervention (credit, prices, release states, approvals, stuck requests, dead letters) keeps the
/// ledger consistent and leaves an audit row.
/// </summary>
[Collection(HeliosApiCollection.Name)]
public sealed class PlatformAdministrationTests(HeliosApiFactory factory)
{
    private readonly HeliosApiFactory _factory = factory;

    private async Task<T> WithDbAsync<T>(Func<HeliosDbContext, Task<T>> query)
    {
        using var scope = _factory.CreateSystemScope();
        return await query(scope.ServiceProvider.GetRequiredService<HeliosDbContext>());
    }

    private async Task<BalanceResponse> BalanceAsync(Guid organizationId)
    {
        using var scope = _factory.CreateSystemScope();
        return await scope.ServiceProvider.GetRequiredService<LedgerService>().GetBalanceAsync(organizationId, CancellationToken.None);
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())?.Extensions.TryGetValue("code", out var code) == true
            ? code?.ToString()
            : null;

    // Access to the platform surface

    [Fact]
    public async Task Ordinary_sessions_and_api_keys_cannot_reach_platform_routes()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var key = await company.CreateKeyAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await company.Owner.Client.GetAsync("/api/v1/platform/organizations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await company.KeyClient(key.Secret).GetAsync("/api/v1/platform/organizations")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/v1/platform/organizations")).StatusCode);

        // A staff member's ordinary sign-in token is not a platform session either.
        var staff = await TestStaff.CreateAsync(_factory, PlatformRole.Administrator);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.Account.Client.GetAsync("/api/v1/platform/organizations")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.Platform.GetAsync("/api/v1/platform/organizations")).StatusCode);
    }

    [Fact]
    public async Task Customers_cannot_enrol_for_platform_access()
    {
        var customer = await TestAccount.RegisterAsync(_factory);

        var response = await customer.Client.PostAsync("/api/v1/platform/mfa/enrol", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("platform_access_denied", await CodeOf(response));
    }

    [Fact]
    public async Task A_code_is_accepted_once_and_never_replayed()
    {
        var staff = await TestStaff.EnrolledAsync(_factory, PlatformRole.Support);
        var step = Totp.StepAt(DateTimeOffset.UtcNow);

        var wrong = await staff.Account.Client.PostAsJsonAsync("/api/v1/platform/mfa/confirm",
            new MfaCodeRequest(WrongCode(staff, step)));
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Equal("mfa_invalid", await CodeOf(wrong));

        var confirmed = await staff.Account.Client.PostAsJsonAsync("/api/v1/platform/mfa/confirm", new MfaCodeRequest(staff.CodeAt(step)));
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var session = (await confirmed.Content.ReadFromJsonAsync<PlatformSessionResponse>())!;
        Assert.Equal(PlatformRole.Support, session.Role);
        Assert.True(session.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(16));

        // The same code again — a replay of an observed code — is refused.
        var replay = await staff.Account.Client.PostAsJsonAsync("/api/v1/platform/mfa/verify", new MfaCodeRequest(staff.CodeAt(step)));
        Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode);

        // A later step's code works.
        var next = await staff.Account.Client.PostAsJsonAsync("/api/v1/platform/mfa/verify", new MfaCodeRequest(staff.CodeAt(step + 1)));
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);

        // Once confirmed, a stolen password cannot enrol a new authenticator.
        var reEnrol = await staff.Account.Client.PostAsync("/api/v1/platform/mfa/enrol", null);
        Assert.Equal(HttpStatusCode.Conflict, reEnrol.StatusCode);

        var denials = await WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters()
            .CountAsync(a => a.Action == "platform.mfa.verify" && a.ResourceId == staff.Account.UserId.ToString() && !a.Allowed));
        Assert.Equal(2, denials);
    }

    [Fact]
    public async Task Five_invalid_codes_lock_out_even_the_right_code()
    {
        var staff = await TestStaff.EnrolledAsync(_factory, PlatformRole.Support);
        var step = Totp.StepAt(DateTimeOffset.UtcNow);

        for (var i = 0; i < 5; i++)
        {
            var bad = await staff.Account.Client.PostAsJsonAsync("/api/v1/platform/mfa/confirm", new MfaCodeRequest(WrongCode(staff, step)));
            Assert.Equal(HttpStatusCode.Forbidden, bad.StatusCode);
            Assert.Equal("mfa_invalid", await CodeOf(bad)); // not yet locked: the fifth failure is what locks
        }

        var right = await staff.Account.Client.PostAsJsonAsync("/api/v1/platform/mfa/confirm", new MfaCodeRequest(staff.CodeAt(step)));
        Assert.Equal(HttpStatusCode.Forbidden, right.StatusCode);
        Assert.Equal("mfa_locked", await CodeOf(right));
    }

    [Fact]
    public async Task Deactivation_role_change_and_mfa_reset_end_platform_sessions_immediately()
    {
        var admin = await TestStaff.CreateAsync(_factory, PlatformRole.Administrator);
        var support = await TestStaff.CreateAsync(_factory, PlatformRole.Support);
        var finance = await TestStaff.CreateAsync(_factory, PlatformRole.Finance);
        var other = await TestStaff.CreateAsync(_factory, PlatformRole.Support);

        Assert.Equal(HttpStatusCode.OK, (await support.Platform.GetAsync("/api/v1/platform/jobs")).StatusCode);

        (await admin.Platform.PostAsync($"/api/v1/platform/staff/{support.Account.UserId}/deactivate", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await support.Platform.GetAsync("/api/v1/platform/jobs")).StatusCode);

        (await admin.Platform.PostAsJsonAsync("/api/v1/platform/staff",
            new GrantPlatformRoleRequest(finance.Account.Email, PlatformRole.Support))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await finance.Platform.GetAsync("/api/v1/platform/jobs")).StatusCode);

        (await admin.Platform.PostAsync($"/api/v1/platform/staff/{other.Account.UserId}/reset-mfa", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.Platform.GetAsync("/api/v1/platform/jobs")).StatusCode);
    }

    [Fact]
    public async Task No_one_changes_their_own_staff_record()
    {
        var admin = await TestStaff.CreateAsync(_factory, PlatformRole.Administrator);

        var self = await admin.Platform.PostAsJsonAsync("/api/v1/platform/staff",
            new GrantPlatformRoleRequest(admin.Account.Email, PlatformRole.Support));
        Assert.Equal(HttpStatusCode.Forbidden, self.StatusCode);
        Assert.Equal("self_change_refused", await CodeOf(self));

        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.Platform.PostAsync($"/api/v1/platform/staff/{admin.Account.UserId}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.Platform.PostAsync($"/api/v1/platform/staff/{admin.Account.UserId}/reset-mfa", null)).StatusCode);
    }

    [Fact]
    public async Task Roles_separate_staff_money_and_approvals_and_denials_are_audited()
    {
        var support = await TestStaff.CreateAsync(_factory, PlatformRole.Support);
        var finance = await TestStaff.CreateAsync(_factory, PlatformRole.Finance);
        var company = await TestCompany.OnboardAsync(_factory);
        var target = await TestAccount.RegisterAsync(_factory);

        Assert.Equal(HttpStatusCode.Forbidden, (await support.Platform.PostAsJsonAsync(
            $"/api/v1/platform/organizations/{company.Organization.Id}/adjustments",
            new CreditAdjustmentRequest(10m, "Support trying credit", "s-1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.Platform.PostAsJsonAsync("/api/v1/platform/staff",
            new GrantPlatformRoleRequest(target.Email, PlatformRole.Administrator))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.Platform.PostAsJsonAsync("/api/v1/platform/staff",
            new GrantPlatformRoleRequest(target.Email, PlatformRole.Administrator))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.Platform.GetAsync("/api/v1/platform/entitlements/pending")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.Platform.GetAsync("/api/v1/platform/audit")).StatusCode);

        Assert.Equal(new BalanceResponse("ZAR", 0m, 0m, 0m), await BalanceAsync(company.Organization.Id));
        Assert.Null(await WithDbAsync(db => db.PlatformStaff.SingleOrDefaultAsync(s => s.UserId == target.UserId)));

        var denials = await WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.Action == "platform.access" && !a.Allowed &&
                        (a.ActorUserId == support.Account.UserId || a.ActorUserId == finance.Account.UserId))
            .CountAsync());
        Assert.Equal(5, denials);
    }

    // Money

    [Fact]
    public async Task Credit_adjustments_are_idempotent_bounded_and_audited()
    {
        var finance = await TestStaff.CreateAsync(_factory, PlatformRole.Finance);
        var company = await TestCompany.OnboardAsync(_factory);
        var url = $"/api/v1/platform/organizations/{company.Organization.Id}/adjustments";

        var first = await finance.Platform.PostAsJsonAsync(url, new CreditAdjustmentRequest(50m, "Pilot goodwill credit", "pilot-1"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.True((await first.Content.ReadFromJsonAsync<CreditAdjustmentResponse>())!.Posted);

        var repeat = await finance.Platform.PostAsJsonAsync(url, new CreditAdjustmentRequest(50m, "Pilot goodwill credit", "pilot-1"));
        Assert.False((await repeat.Content.ReadFromJsonAsync<CreditAdjustmentResponse>())!.Posted);

        var reused = await finance.Platform.PostAsJsonAsync(url, new CreditAdjustmentRequest(70m, "Pilot goodwill credit", "pilot-1"));
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        Assert.Equal("reference_reused", await CodeOf(reused));

        var overdraw = await finance.Platform.PostAsJsonAsync(url, new CreditAdjustmentRequest(-80m, "Too large a correction", "pilot-2"));
        Assert.Equal(HttpStatusCode.PaymentRequired, overdraw.StatusCode);

        var invalid = await finance.Platform.PostAsJsonAsync(url, new CreditAdjustmentRequest(0.001m, "Fractional cents", "pilot-3"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var debit = await finance.Platform.PostAsJsonAsync(url, new CreditAdjustmentRequest(-20m, "Correction of duplicate goodwill", "pilot-4"));
        Assert.Equal(HttpStatusCode.OK, debit.StatusCode);

        Assert.Equal(new BalanceResponse("ZAR", 30m, 0m, 30m), await BalanceAsync(company.Organization.Id));

        var audited = await WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.Action == "platform.credit.adjust" && a.OrganizationId == company.Organization.Id)
            .ToListAsync());
        Assert.Equal(3, audited.Count);
        Assert.All(audited, a => Assert.Equal(finance.Account.UserId, a.ActorUserId));

        var transactions = await WithDbAsync(db => db.LedgerTransactions
            .Where(t => t.OrganizationId == company.Organization.Id && t.Type == LedgerTransactionType.Adjustment).ToListAsync());
        Assert.Equal(2, transactions.Count);
        Assert.All(transactions, t => Assert.Equal(finance.Account.UserId, t.CreatedBy));
    }

    [Fact]
    public async Task Prices_cannot_be_backdated_and_live_needs_a_price_and_an_implementation()
    {
        var admin = await TestStaff.CreateAsync(_factory, PlatformRole.Administrator);
        var support = await TestStaff.CreateAsync(_factory, PlatformRole.Support);

        var backdated = await admin.Platform.PostAsJsonAsync("/api/v1/platform/prices", new PublishPriceRequest(
            "documents.invoice", ApiEnvironment.Live, "document", 2m, 0m, "vat_exclusive_standard", DateTimeOffset.UtcNow.AddHours(-1)));
        Assert.Equal(HttpStatusCode.BadRequest, backdated.StatusCode);
        Assert.Equal("price_backdated", await CodeOf(backdated));

        Assert.Equal(HttpStatusCode.Forbidden, (await support.Platform.PostAsJsonAsync("/api/v1/platform/prices", new PublishPriceRequest(
            "documents.invoice", ApiEnvironment.Live, "document", 2m, 0m, "vat_exclusive_standard", null))).StatusCode);

        var unimplemented = await admin.Platform.PostAsJsonAsync("/api/v1/platform/products/documents.sa-id/release-state",
            new ChangeReleaseStateRequest(ProductReleaseState.Sandbox, "Trying to offer an unbuilt product"));
        Assert.Equal(HttpStatusCode.Conflict, unimplemented.StatusCode);
        Assert.Equal("no_implementation", await CodeOf(unimplemented));

        // ocr.general: implemented, sandbox, and (in this run) never priced for live.
        var unpriced = await admin.Platform.PostAsJsonAsync("/api/v1/platform/products/ocr.general/release-state",
            new ChangeReleaseStateRequest(ProductReleaseState.Beta, "Pilot without a price"));
        Assert.Equal(HttpStatusCode.Conflict, unpriced.StatusCode);
        Assert.Equal("price_unavailable", await CodeOf(unpriced));

        var catalogue = await _factory.CreateClient().GetFromJsonAsync<ProductDetailResponse>("/api/v1/catalogue/ocr.general");
        Assert.Equal(ProductReleaseState.Sandbox, catalogue!.Product.ReleaseState);
    }

    [Fact]
    public async Task A_priced_product_can_move_to_beta_and_back_and_each_change_is_audited()
    {
        var admin = await TestStaff.CreateAsync(_factory, PlatformRole.Administrator);

        // test.provider is live and priced by the test host; suspend it, then restore it.
        var suspended = await admin.Platform.PostAsJsonAsync("/api/v1/platform/products/test.provider/release-state",
            new ChangeReleaseStateRequest(ProductReleaseState.Suspended, "Provider incident drill"));
        try
        {
            Assert.Equal(HttpStatusCode.OK, suspended.StatusCode);
            var summary = (await suspended.Content.ReadFromJsonAsync<ProductSummaryResponse>())!;
            Assert.False(summary.CallableInLive);
            Assert.False(summary.CallableInSandbox);
        }
        finally
        {
            (await admin.Platform.PostAsJsonAsync("/api/v1/platform/products/test.provider/release-state",
                new ChangeReleaseStateRequest(ProductReleaseState.Live, "Drill complete"))).EnsureSuccessStatusCode();
        }

        var changes = await WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.Action == "platform.product.release_state" && a.ActorUserId == admin.Account.UserId)
            .ToListAsync());
        Assert.Equal(2, changes.Count);
        Assert.Contains(changes, c => c.Metadata!.Contains("Provider incident drill"));
    }

    [Fact]
    public async Task Publishing_a_future_price_closes_the_current_one_at_that_moment()
    {
        var finance = await TestStaff.CreateAsync(_factory, PlatformRole.Finance);
        var from = DateTimeOffset.UtcNow.AddDays(30);

        var response = await finance.Platform.PostAsJsonAsync("/api/v1/platform/prices", new PublishPriceRequest(
            TestProducts.Metered, ApiEnvironment.Sandbox, "unit", 0.5m, 0m, "vat_exclusive_standard", from));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var prices = await finance.Platform.GetFromJsonAsync<List<PriceVersionResponse>>($"/api/v1/platform/prices?product={TestProducts.Metered}");
        var sandbox = prices!.Where(p => p.Environment == ApiEnvironment.Sandbox).ToList();
        Assert.Contains(sandbox, p => p.UnitPrice == 0.5m && p.EffectiveTo is null);

        var created = await WithDbAsync(db => db.PriceVersions.SingleAsync(p => p.CreatedBy == finance.Account.UserId));
        Assert.Equal(0.5m, created.UnitPrice);
    }

    // Approvals

    [Fact]
    public async Task Restricted_entitlements_are_approved_or_rejected_with_a_reason()
    {
        var admin = await TestStaff.CreateAsync(_factory, PlatformRole.Administrator);
        var company = await TestCompany.OnboardAsync(_factory);

        var (approvable, unavailable) = await WithDbAsync(async db =>
        {
            var metered = await db.ApiProducts.SingleAsync(p => p.Slug == TestProducts.Metered);
            var ocr = await db.ApiProducts.SingleAsync(p => p.Slug == "ocr.general");

            var a = new Entitlement { OrganizationId = company.Organization.Id, ProductId = metered.Id, Environment = ApiEnvironment.Live, State = EntitlementState.PendingApproval, Purpose = "Supplier onboarding" };
            var b = new Entitlement { OrganizationId = company.Organization.Id, ProductId = ocr.Id, Environment = ApiEnvironment.Live, State = EntitlementState.PendingApproval, Purpose = "Capture" };
            db.Entitlements.AddRange(a, b);
            await db.SaveChangesAsync();
            return (a.Id, b.Id);
        });

        var pending = await admin.Platform.GetFromJsonAsync<List<PendingEntitlementResponse>>("/api/v1/platform/entitlements/pending");
        Assert.Contains(pending!, p => p.Id == approvable && p.Purpose == "Supplier onboarding" && p.OrganizationName == company.Organization.Name);

        var noReason = await admin.Platform.PostAsJsonAsync($"/api/v1/platform/entitlements/{approvable}/decision",
            new DecideEntitlementRequest(ApprovalDecision.Approve, ""));
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        // An approval never makes an unavailable product usable.
        var blocked = await admin.Platform.PostAsJsonAsync($"/api/v1/platform/entitlements/{unavailable}/decision",
            new DecideEntitlementRequest(ApprovalDecision.Approve, "Customer asked nicely"));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        (await admin.Platform.PostAsJsonAsync($"/api/v1/platform/entitlements/{approvable}/decision",
            new DecideEntitlementRequest(ApprovalDecision.Approve, "Purpose reviewed against policy"))).EnsureSuccessStatusCode();
        (await admin.Platform.PostAsJsonAsync($"/api/v1/platform/entitlements/{unavailable}/decision",
            new DecideEntitlementRequest(ApprovalDecision.Reject, "Product not available live"))).EnsureSuccessStatusCode();

        var again = await admin.Platform.PostAsJsonAsync($"/api/v1/platform/entitlements/{approvable}/decision",
            new DecideEntitlementRequest(ApprovalDecision.Reject, "Changed my mind"));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var states = await WithDbAsync(db => db.Entitlements
            .Where(e => e.Id == approvable || e.Id == unavailable).ToDictionaryAsync(e => e.Id, e => e.State));
        Assert.Equal(EntitlementState.Enabled, states[approvable]);
        Assert.Equal(EntitlementState.Disabled, states[unavailable]);

        var audit = await admin.Platform.GetFromJsonAsync<List<PlatformAuditEntry>>(
            $"/api/v1/platform/audit?organizationId={company.Organization.Id}&action=platform.entitlement");
        Assert.Equal(2, audit!.Count);
        Assert.Contains(audit, a => a.Action == "platform.entitlement.approve" && a.Metadata!.Contains("Purpose reviewed against policy"));
    }

    // Operations

    private static async Task<ApiRequestEnvelope> SubmitProviderAsync(HttpClient client, string scenario)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/products/{TestProducts.Provider}/requests")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { units = 1, scenario }), System.Text.Encoding.UTF8, "application/json")
        };
        message.Headers.Add("Idempotency-Key", $"k-{Guid.NewGuid():N}");

        var response = await client.SendAsync(message);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiRequestEnvelope>())!;
    }

    [Fact]
    public async Task A_request_stuck_in_review_is_released_once_and_its_customer_is_notified()
    {
        var finance = await TestStaff.CreateAsync(_factory, PlatformRole.Finance);
        var support = await TestStaff.CreateAsync(_factory, PlatformRole.Support);
        var company = await TestCompany.OnboardAsync(_factory);
        var key = await company.CreateLiveKeyAsync(TestProducts.Provider);
        await _factory.FundAsync(company.Organization.Id, 10m);

        var host = $"hooks-{Guid.NewGuid():N}.example.com";
        (await company.Owner.Client.PostAsJsonAsync("/api/v1/webhooks",
            new CreateWebhookRequest($"https://{host}/helios", [WebhookEventTypes.RequestFailed]))).EnsureSuccessStatusCode();

        var envelope = await SubmitProviderAsync(company.KeyClient(key.Secret), "unknown-forever");
        await _factory.DrainJobsAsync();
        Assert.Equal(new BalanceResponse("ZAR", 10m, 2.5m, 7.5m), await BalanceAsync(company.Organization.Id));

        var queue = await support.Platform.GetFromJsonAsync<List<PlatformJobResponse>>("/api/v1/platform/jobs");
        var stuck = Assert.Single(queue!, j => j.RequestId == envelope.RequestId);
        Assert.Equal("NeedsReview", stuck.Status);
        Assert.Equal(2.5m, stuck.ReservedAmount);

        // Support can see the queue but not resolve it.
        Assert.Equal(HttpStatusCode.Forbidden, (await support.Platform.PostAsJsonAsync(
            $"/api/v1/platform/requests/{envelope.RequestId}/resolve",
            new ResolveRequestRequest(ResolutionAction.Release, "Support attempt"))).StatusCode);

        var resolved = await finance.Platform.PostAsJsonAsync($"/api/v1/platform/requests/{envelope.RequestId}/resolve",
            new ResolveRequestRequest(ResolutionAction.Release, "Provider confirmed no record of the check"));
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);

        var again = await finance.Platform.PostAsJsonAsync($"/api/v1/platform/requests/{envelope.RequestId}/resolve",
            new ResolveRequestRequest(ResolutionAction.Release, "Double click"));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        Assert.Equal(new BalanceResponse("ZAR", 10m, 0m, 10m), await BalanceAsync(company.Organization.Id));

        var summary = await company.KeyClient(key.Secret).GetFromJsonAsync<ApiRequestSummary>($"/api/v1/requests/{envelope.RequestId}");
        Assert.Equal(ApiRequestStatus.Failed, summary!.Status);
        Assert.Equal("outcome_unknown_released", summary.Error);
        Assert.Equal(BillingState.Released, summary.Billing.State);

        await _factory.DrainWebhooksAsync();
        var received = Assert.Single(_factory.WebhookReceiver.For(host));
        Assert.Contains("outcome_unknown_released", received.Body);

        var releases = await WithDbAsync(db => db.LedgerTransactions.CountAsync(t =>
            t.ApiRequestId == envelope.RequestId && t.Type == LedgerTransactionType.Release));
        Assert.Equal(1, releases);
    }

    [Fact]
    public async Task A_request_can_be_sent_back_to_reconciliation_with_a_fresh_budget()
    {
        var admin = await TestStaff.CreateAsync(_factory, PlatformRole.Administrator);
        var company = await TestCompany.OnboardAsync(_factory);
        var key = await company.CreateLiveKeyAsync(TestProducts.Provider);
        await _factory.FundAsync(company.Organization.Id, 10m);

        var host = $"hooks-{Guid.NewGuid():N}.example.com";
        (await company.Owner.Client.PostAsJsonAsync("/api/v1/webhooks",
            new CreateWebhookRequest($"https://{host}/helios", [WebhookEventTypes.RequestNeedsReview]))).EnsureSuccessStatusCode();

        var envelope = await SubmitProviderAsync(company.KeyClient(key.Secret), "unknown-forever");
        await _factory.DrainJobsAsync();

        var response = await admin.Platform.PostAsJsonAsync($"/api/v1/platform/requests/{envelope.RequestId}/resolve",
            new ResolveRequestRequest(ResolutionAction.Reconcile, "Provider says their status API is back"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var job = await WithDbAsync(db => db.Jobs.SingleAsync(j => j.ApiRequestId == envelope.RequestId));
        Assert.Equal(JobStatus.Reconciling, job.Status);
        Assert.Equal(0, job.ReconcileAttempts);

        // Still unknown at the provider: back to review after the budget, money still held. Reaching
        // review a second time must not fail on the already-sent review event.
        await _factory.DrainJobsAsync();
        job = await WithDbAsync(db => db.Jobs.SingleAsync(j => j.ApiRequestId == envelope.RequestId));
        Assert.Equal(JobStatus.NeedsReview, job.Status);
        Assert.Equal(new BalanceResponse("ZAR", 10m, 2.5m, 7.5m), await BalanceAsync(company.Organization.Id));

        await _factory.DrainWebhooksAsync();
        Assert.Single(_factory.WebhookReceiver.For(host));
    }

    [Fact]
    public async Task Support_redelivers_a_dead_letter_to_an_active_endpoint_only()
    {
        var support = await TestStaff.CreateAsync(_factory, PlatformRole.Support);
        var company = await TestCompany.OnboardAsync(_factory);
        var host = $"hooks-{Guid.NewGuid():N}.example.com";
        _factory.WebhookReceiver.StatusByHost[host] = HttpStatusCode.InternalServerError;

        var created = await company.Owner.Client.PostAsJsonAsync("/api/v1/webhooks",
            new CreateWebhookRequest($"https://{host}/helios", [WebhookEventTypes.RequestSucceeded]));
        var endpoint = (await created.Content.ReadFromJsonAsync<CreatedWebhookResponse>())!;

        var key = await company.CreateKeyAsync();
        (await company.KeyClient(key.Secret).PostAsync($"/api/v1/products/{TestCompany.SaIdProduct}/requests",
            new StringContent("""{"idNumber":"8001015009087"}""", System.Text.Encoding.UTF8, "application/json"))).EnsureSuccessStatusCode();

        await _factory.DrainWebhooksAsync();

        var deadLetters = await support.Platform.GetFromJsonAsync<List<PlatformDeliveryResponse>>("/api/v1/platform/webhook-deliveries");
        var dead = Assert.Single(deadLetters!, d => d.EndpointId == endpoint.Endpoint.Id);
        Assert.Equal("Failed", dead.Status);

        _factory.WebhookReceiver.StatusByHost[host] = HttpStatusCode.OK;
        var retried = await support.Platform.PostAsync($"/api/v1/platform/webhook-deliveries/{dead.Id}/retry", null);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);

        Assert.Equal(HttpStatusCode.Conflict,
            (await support.Platform.PostAsync($"/api/v1/platform/webhook-deliveries/{dead.Id}/retry", null)).StatusCode);

        await _factory.DrainWebhooksAsync();
        var deliveries = await company.Owner.Client.GetFromJsonAsync<List<WebhookDeliveryResponse>>(
            $"/api/v1/webhooks/{endpoint.Endpoint.Id}/deliveries");
        Assert.Equal("Delivered", Assert.Single(deliveries!).Status);

        // A dead letter for an endpoint its owner deactivated stays dead.
        _factory.WebhookReceiver.StatusByHost[host] = HttpStatusCode.InternalServerError;
        (await company.KeyClient(key.Secret).PostAsync($"/api/v1/products/{TestCompany.SaIdProduct}/requests",
            new StringContent("""{"idNumber":"8001015009087"}""", System.Text.Encoding.UTF8, "application/json"))).EnsureSuccessStatusCode();
        await _factory.DrainWebhooksAsync();

        var second = (await support.Platform.GetFromJsonAsync<List<PlatformDeliveryResponse>>("/api/v1/platform/webhook-deliveries"))!
            .Single(d => d.EndpointId == endpoint.Endpoint.Id);
        (await company.Owner.Client.DeleteAsync($"/api/v1/webhooks/{endpoint.Endpoint.Id}")).EnsureSuccessStatusCode();

        var refused = await support.Platform.PostAsync($"/api/v1/platform/webhook-deliveries/{second.Id}/retry", null);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("endpoint_inactive", await CodeOf(refused));
    }

    [Fact]
    public async Task The_console_command_grants_a_role_to_an_existing_account_only()
    {
        var person = await TestAccount.RegisterAsync(_factory);

        var command = PlatformStaffCommand.TryParse(["platform-staff", "grant", person.Email, "Administrator", "--urls", "x"], out var hostArgs);
        Assert.NotNull(command);
        Assert.Equal(["--urls", "x"], hostArgs);
        Assert.Null(PlatformStaffCommand.TryParse(["--urls", "x"], out _));
        Assert.Throws<ArgumentException>(() => PlatformStaffCommand.TryParse(["platform-staff", "grant", person.Email, "Root"], out _));

        var output = new StringWriter();
        Assert.Equal(0, await command!.RunAsync(_factory.Services, output));

        var missing = PlatformStaffCommand.TryParse(["platform-staff", "grant", "nobody@helios.test", "Support"], out _)!;
        Assert.Equal(1, await missing.RunAsync(_factory.Services, output));

        var staff = await WithDbAsync(db => db.PlatformStaff.SingleAsync(s => s.UserId == person.UserId));
        Assert.Equal(PlatformRole.Administrator, staff.Role);
        Assert.False(staff.MfaConfirmed);

        var audit = await WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters()
            .SingleAsync(a => a.Action == "platform.staff.grant" && a.ResourceId == person.UserId.ToString()));
        Assert.Contains("console", audit.Metadata);
    }

    private static string WrongCode(TestStaff staff, long step)
    {
        var valid = new HashSet<string> { staff.CodeAt(step - 1), staff.CodeAt(step), staff.CodeAt(step + 1) };
        return Enumerable.Range(0, 1000).Select(i => i.ToString("D6")).First(c => !valid.Contains(c));
    }
}
