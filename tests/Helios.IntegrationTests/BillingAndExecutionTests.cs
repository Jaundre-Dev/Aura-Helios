using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Helios.Application.Abstractions.Execution;
using Helios.Application.Features.Billing;
using Helios.Application.Features.Execution;
using Helios.Contracts.Billing;
using Helios.Contracts.Requests;
using Helios.Domain.Execution;
using Helios.Infrastructure.Persistence.MySql;
using Helios.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.IntegrationTests;

/// <summary>
/// The P2 acceptance gate (plan section 14): concurrent spend cannot exceed available credit;
/// duplicate submissions never double-charge; restart recovery cannot duplicate settlement;
/// unknown vendor completion reconciles; failed internal processing releases credit; status
/// polling is free. Every test ends by checking the ledger is balanced and its projections match.
/// </summary>
[Collection(HeliosApiCollection.Name)]
public sealed class BillingAndExecutionTests(HeliosApiFactory factory)
{
    private readonly HeliosApiFactory _factory = factory;

    private static string Url(string slug) => $"/api/v1/products/{slug}/requests";

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string slug, object body, string? idempotencyKey = "auto")
    {
        var message = new HttpRequestMessage(HttpMethod.Post, Url(slug))
        {
            Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json")
        };

        if (idempotencyKey is not null)
        {
            message.Headers.Add("Idempotency-Key", idempotencyKey == "auto" ? $"k-{Guid.NewGuid():N}" : idempotencyKey);
        }

        return await client.SendAsync(message);
    }

    private async Task<(TestCompany Company, HttpClient Client)> FundedLiveCompanyAsync(decimal funds, params string[] products)
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var key = await company.CreateLiveKeyAsync(products.Length == 0 ? [TestProducts.Metered] : products);
        if (funds > 0)
        {
            await _factory.FundAsync(company.Organization.Id, funds);
        }

        return (company, company.KeyClient(key.Secret));
    }

    private async Task<BalanceResponse> BalanceAsync(Guid organizationId)
    {
        using var scope = _factory.CreateSystemScope();
        return await scope.ServiceProvider.GetRequiredService<LedgerService>().GetBalanceAsync(organizationId, CancellationToken.None);
    }

    private async Task<T> WithDbAsync<T>(Func<HeliosDbContext, Task<T>> query)
    {
        using var scope = _factory.CreateSystemScope();
        return await query(scope.ServiceProvider.GetRequiredService<HeliosDbContext>());
    }

    /// <summary>Every transaction balances; every customer balance equals the sum of its entries; none negative.</summary>
    private async Task AssertLedgerConsistentAsync(Guid organizationId)
    {
        await WithDbAsync(async db =>
        {
            var transactions = await db.LedgerTransactions.Include(t => t.Entries)
                .Where(t => t.OrganizationId == organizationId).ToListAsync();
            Assert.All(transactions, t => Assert.Equal(0m, t.Entries.Sum(e => e.Amount)));

            var accounts = await db.LedgerAccounts.Where(a => a.OrganizationId == organizationId).ToListAsync();
            foreach (var account in accounts)
            {
                var sum = await db.LedgerEntries.Where(e => e.AccountId == account.Id).SumAsync(e => (decimal?)e.Amount) ?? 0m;
                Assert.Equal(sum, account.Balance);
                Assert.True(account.Balance >= 0m, $"{account.Type} went negative: {account.Balance}");
            }

            return true;
        });
    }

    [Fact]
    public async Task A_billable_request_reserves_then_settles_exactly_its_charge()
    {
        var (company, client) = await FundedLiveCompanyAsync(10m);

        var response = await SendAsync(client, TestProducts.Metered, new { units = 3 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = (await response.Content.ReadFromJsonAsync<ApiRequestEnvelope>())!;
        Assert.Equal(ApiRequestStatus.Succeeded, envelope.Status);
        Assert.Equal(new BillingInfo(BillingState.Settled, "ZAR", 3.00m), envelope.Billing);
        Assert.Equal(new UsageInfo("unit", 3m), envelope.Usage);

        Assert.Equal(new BalanceResponse("ZAR", 7m, 0m, 7m), await BalanceAsync(company.Organization.Id));

        var usage = await WithDbAsync(db => db.UsageEvents.SingleAsync(u => u.ApiRequestId == envelope.RequestId));
        Assert.Equal(3m, usage.Amount);
        Assert.NotNull(usage.PriceVersionId);

        // The sealed input is purged once the job is finished.
        var job = await WithDbAsync(db => db.Jobs.SingleAsync(j => j.ApiRequestId == envelope.RequestId));
        Assert.Null(job.InputEnvelope);

        await AssertLedgerConsistentAsync(company.Organization.Id);
    }

    [Fact]
    public async Task Usage_below_the_estimate_charges_only_what_was_used()
    {
        var (company, client) = await FundedLiveCompanyAsync(10m);

        var response = await SendAsync(client, TestProducts.Metered, new { units = 5, actualUnits = 2 });
        var envelope = (await response.Content.ReadFromJsonAsync<ApiRequestEnvelope>())!;

        Assert.Equal(2.00m, envelope.Billing.Amount);
        Assert.Equal(new BalanceResponse("ZAR", 8m, 0m, 8m), await BalanceAsync(company.Organization.Id));
        await AssertLedgerConsistentAsync(company.Organization.Id);
    }

    [Fact]
    public async Task Insufficient_credit_is_refused_with_nothing_recorded_or_held()
    {
        var (company, client) = await FundedLiveCompanyAsync(2m);

        var response = await SendAsync(client, TestProducts.Metered, new { units = 3 });

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        Assert.Contains("insufficient_credit", await response.Content.ReadAsStringAsync());
        Assert.Equal(new BalanceResponse("ZAR", 2m, 0m, 2m), await BalanceAsync(company.Organization.Id));
        Assert.False(await WithDbAsync(db => db.ApiRequests.AnyAsync(r => r.OrganizationId == company.Organization.Id)));
    }

    [Fact]
    public async Task Concurrent_spend_never_exceeds_available_credit()
    {
        var (company, client) = await FundedLiveCompanyAsync(10m);

        // Thirty simultaneous R1 requests against R10: exactly ten may succeed.
        var responses = await Task.WhenAll(Enumerable.Range(0, 30)
            .Select(_ => SendAsync(client, TestProducts.Metered, new { units = 1 })));

        Assert.Equal(10, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(20, responses.Count(r => r.StatusCode == HttpStatusCode.PaymentRequired));
        Assert.Equal(new BalanceResponse("ZAR", 0m, 0m, 0m), await BalanceAsync(company.Organization.Id));

        var revenue = await WithDbAsync(db => db.LedgerTransactions
            .Where(t => t.OrganizationId == company.Organization.Id && t.Type == LedgerTransactionType.Settle)
            .CountAsync());
        Assert.Equal(10, revenue);

        await AssertLedgerConsistentAsync(company.Organization.Id);
    }

    [Fact]
    public async Task Concurrent_duplicate_submissions_charge_once()
    {
        var (company, client) = await FundedLiveCompanyAsync(10m);
        var idempotencyKey = $"dup-{Guid.NewGuid():N}";

        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => SendAsync(client, TestProducts.Metered, new { units = 2 }, idempotencyKey)));

        // The first completes (200); duplicates replay the original — 202 while it is still in flight,
        // 200 once finished — and are marked as replays. None runs a second time.
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Accepted }));
        Assert.Single(responses, r => !r.Headers.Contains("Idempotent-Replayed"));
        var ids = await Task.WhenAll(responses.Select(async r => (await r.Content.ReadFromJsonAsync<ApiRequestEnvelope>())!.RequestId));
        Assert.Single(ids.Distinct());

        Assert.Equal(new BalanceResponse("ZAR", 8m, 0m, 8m), await BalanceAsync(company.Organization.Id));
        await AssertLedgerConsistentAsync(company.Organization.Id);
    }

    [Fact]
    public async Task Billable_requests_require_an_idempotency_key()
    {
        var (_, client) = await FundedLiveCompanyAsync(10m);

        var response = await SendAsync(client, TestProducts.Metered, new { units = 1 }, idempotencyKey: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("idempotency_key_required", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Status_polling_is_free()
    {
        var (company, client) = await FundedLiveCompanyAsync(10m);
        var envelope = (await (await SendAsync(client, TestProducts.Metered, new { units = 1 }))
            .Content.ReadFromJsonAsync<ApiRequestEnvelope>())!;
        var before = await WithDbAsync(db => db.LedgerTransactions.CountAsync(t => t.OrganizationId == company.Organization.Id));

        for (var i = 0; i < 20; i++)
        {
            (await client.GetAsync($"/api/v1/requests/{envelope.RequestId}")).EnsureSuccessStatusCode();
            (await client.GetAsync($"/api/v1/requests/{envelope.RequestId}/result")).EnsureSuccessStatusCode();
        }

        Assert.Equal(before, await WithDbAsync(db => db.LedgerTransactions.CountAsync(t => t.OrganizationId == company.Organization.Id)));
        Assert.Equal(new BalanceResponse("ZAR", 9m, 0m, 9m), await BalanceAsync(company.Organization.Id));
    }

    [Fact]
    public async Task Failed_internal_processing_releases_the_reservation()
    {
        var (company, client) = await FundedLiveCompanyAsync(10m);

        var response = await SendAsync(client, TestProducts.Metered, new { units = 4, mode = "fail" });

        // First attempt failed inline; the request is accepted and queued for retry, money held.
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var envelope = (await response.Content.ReadFromJsonAsync<ApiRequestEnvelope>())!;
        Assert.Equal(new BillingInfo(BillingState.Reserved, "ZAR", 0m, 4m), envelope.Billing);
        Assert.Equal(new BalanceResponse("ZAR", 10m, 4m, 6m), await BalanceAsync(company.Organization.Id));

        await _factory.DrainJobsAsync();

        var final = await client.GetFromJsonAsync<ApiRequestSummary>($"/api/v1/requests/{envelope.RequestId}");
        Assert.Equal(ApiRequestStatus.Failed, final!.Status);
        Assert.Equal("processing_failed", final.Error);
        Assert.Equal(new BillingInfo(BillingState.Released, "ZAR", 0m), final.Billing);
        Assert.Equal(new BalanceResponse("ZAR", 10m, 0m, 10m), await BalanceAsync(company.Organization.Id));
        Assert.Equal(3, (await WithDbAsync(db => db.Jobs.SingleAsync(j => j.ApiRequestId == envelope.RequestId))).Attempts);

        await AssertLedgerConsistentAsync(company.Organization.Id);
    }

    [Fact]
    public async Task An_async_product_is_accepted_then_completed_by_the_worker()
    {
        var (company, client) = await FundedLiveCompanyAsync(10m, TestProducts.Provider);

        var response = await SendAsync(client, TestProducts.Provider, new { units = 2, scenario = "ok" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var envelope = (await response.Content.ReadFromJsonAsync<ApiRequestEnvelope>())!;
        Assert.Equal($"/api/v1/requests/{envelope.RequestId}", response.Headers.Location!.OriginalString);
        Assert.Equal(ApiRequestStatus.Queued, envelope.Status);
        Assert.Equal(new BalanceResponse("ZAR", 10m, 5m, 5m), await BalanceAsync(company.Organization.Id));

        await _factory.DrainJobsAsync();

        var result = await client.GetFromJsonAsync<ApiRequestEnvelope>($"/api/v1/requests/{envelope.RequestId}/result");
        Assert.Equal(ApiRequestStatus.Succeeded, result!.Status);
        Assert.Equal(new BillingInfo(BillingState.Settled, "ZAR", 5.00m), result.Billing);
        Assert.Equal(new BalanceResponse("ZAR", 5m, 0m, 5m), await BalanceAsync(company.Organization.Id));
        await AssertLedgerConsistentAsync(company.Organization.Id);
    }

    [Fact]
    public async Task An_unknown_provider_outcome_reconciles_without_repeating_the_call()
    {
        var (company, client) = await FundedLiveCompanyAsync(10m, TestProducts.Provider);
        var log = _factory.Services.GetRequiredService<TestProviderLog>();

        var envelope = (await (await SendAsync(client, TestProducts.Provider, new { units = 1, scenario = "unknown" }))
            .Content.ReadFromJsonAsync<ApiRequestEnvelope>())!;

        // One tick: the call "times out" and the job moves to reconciliation, money still held.
        await RunUntilAsync(envelope.RequestId, ApiRequestStatus.Reconciling);
        Assert.Equal(new BalanceResponse("ZAR", 10m, 2.5m, 7.5m), await BalanceAsync(company.Organization.Id));

        // Reconciliation asks the provider, learns it completed, and settles — once.
        await _factory.DrainJobsAsync();

        var summary = await client.GetFromJsonAsync<ApiRequestSummary>($"/api/v1/requests/{envelope.RequestId}");
        Assert.Equal(ApiRequestStatus.Succeeded, summary!.Status);
        Assert.Equal(1, log.CallsFor(envelope.RequestId));
        Assert.Equal(new BalanceResponse("ZAR", 7.5m, 0m, 7.5m), await BalanceAsync(company.Organization.Id));
        await AssertLedgerConsistentAsync(company.Organization.Id);
    }

    [Fact]
    public async Task A_provider_confirming_non_completion_is_retried_safely()
    {
        var (company, client) = await FundedLiveCompanyAsync(10m, TestProducts.Provider);
        var log = _factory.Services.GetRequiredService<TestProviderLog>();

        var envelope = (await (await SendAsync(client, TestProducts.Provider, new { units = 1, scenario = "unknown-not-done" }))
            .Content.ReadFromJsonAsync<ApiRequestEnvelope>())!;

        await _factory.DrainJobsAsync();

        var summary = await client.GetFromJsonAsync<ApiRequestSummary>($"/api/v1/requests/{envelope.RequestId}");
        Assert.Equal(ApiRequestStatus.Succeeded, summary!.Status);
        Assert.Equal(2, log.CallsFor(envelope.RequestId));   // re-run only after the provider said "not done"
        Assert.Equal(2.5m, summary.Billing.Amount);
        await AssertLedgerConsistentAsync(company.Organization.Id);
    }

    [Fact]
    public async Task A_permanently_unknown_outcome_goes_to_review_with_money_still_held()
    {
        var (company, client) = await FundedLiveCompanyAsync(10m, TestProducts.Provider);

        var envelope = (await (await SendAsync(client, TestProducts.Provider, new { units = 1, scenario = "unknown-forever" }))
            .Content.ReadFromJsonAsync<ApiRequestEnvelope>())!;

        await _factory.DrainJobsAsync();

        var summary = await client.GetFromJsonAsync<ApiRequestSummary>($"/api/v1/requests/{envelope.RequestId}");
        Assert.Equal(ApiRequestStatus.NeedsReview, summary!.Status);
        Assert.Equal("outcome_unknown", summary.Error);
        Assert.Equal(new BalanceResponse("ZAR", 10m, 2.5m, 7.5m), await BalanceAsync(company.Organization.Id));
        await AssertLedgerConsistentAsync(company.Organization.Id);
    }

    [Fact]
    public async Task A_crash_mid_call_is_reconciled_after_lease_expiry_not_re_executed()
    {
        var (company, client) = await FundedLiveCompanyAsync(10m, TestProducts.Provider);
        var log = _factory.Services.GetRequiredService<TestProviderLog>();

        var envelope = (await (await SendAsync(client, TestProducts.Provider, new { units = 1, scenario = "crash" }))
            .Content.ReadFromJsonAsync<ApiRequestEnvelope>())!;

        // The worker "dies" during the provider call: the job stays leased with execution started.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunUntilAsync(envelope.RequestId, ApiRequestStatus.Succeeded));
        var stuck = await WithDbAsync(db => db.Jobs.SingleAsync(j => j.ApiRequestId == envelope.RequestId));
        Assert.Equal(JobStatus.Running, stuck.Status);
        Assert.NotNull(stuck.ExecutionStartedAt);

        await ExpireLeaseAsync(envelope.RequestId);
        await _factory.DrainJobsAsync("recovery-worker");

        var summary = await client.GetFromJsonAsync<ApiRequestSummary>($"/api/v1/requests/{envelope.RequestId}");
        Assert.Equal(ApiRequestStatus.Succeeded, summary!.Status);
        Assert.Equal(1, log.CallsFor(envelope.RequestId));   // reconciled, never blindly repeated
        Assert.Equal(new BalanceResponse("ZAR", 7.5m, 0m, 7.5m), await BalanceAsync(company.Organization.Id));
        await AssertLedgerConsistentAsync(company.Organization.Id);
    }

    [Fact]
    public async Task A_stale_worker_cannot_commit_after_its_lease_is_taken_over()
    {
        var (company, client) = await FundedLiveCompanyAsync(10m, TestProducts.Provider);

        var envelope = (await (await SendAsync(client, TestProducts.Provider, new { units = 2, scenario = "ok" }))
            .Content.ReadFromJsonAsync<ApiRequestEnvelope>())!;

        // Worker A claims, then stalls before doing anything; its lease lapses.
        var staleClaim = await ClaimAsync(envelope.RequestId, "worker-a");
        await ExpireLeaseAsync(envelope.RequestId);

        // Worker B reclaims and completes the job, settling it.
        await _factory.DrainJobsAsync("worker-b");

        // Worker A wakes up and tries to run with its old fencing token: nothing may change.
        using (var scope = _factory.CreateScopeAs(null, company.Workspace.Id))
        {
            await scope.ServiceProvider.GetRequiredService<JobRunner>().RunAsync(staleClaim, CancellationToken.None);
        }

        var settlements = await WithDbAsync(db => db.LedgerTransactions
            .CountAsync(t => t.ApiRequestId == envelope.RequestId && t.Type == LedgerTransactionType.Settle));
        Assert.Equal(1, settlements);
        Assert.Equal(1, await WithDbAsync(db => db.UsageEvents.CountAsync(u => u.ApiRequestId == envelope.RequestId)));
        Assert.Equal(new BalanceResponse("ZAR", 5m, 0m, 5m), await BalanceAsync(company.Organization.Id));
        await AssertLedgerConsistentAsync(company.Organization.Id);
    }

    [Fact]
    public async Task Settlement_and_release_are_each_applied_at_most_once()
    {
        var (company, client) = await FundedLiveCompanyAsync(10m);
        var envelope = (await (await SendAsync(client, TestProducts.Metered, new { units = 1 }))
            .Content.ReadFromJsonAsync<ApiRequestEnvelope>())!;

        using var scope = _factory.CreateSystemScope();
        var ledger = scope.ServiceProvider.GetRequiredService<LedgerService>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<Application.Abstractions.Persistence.IUnitOfWork>();

        var settledAgain = await unitOfWork.ExecuteInTransactionAsync(ct => ledger.SettleAsync(envelope.RequestId, 1m, ct), CancellationToken.None);
        var releasedAfter = await unitOfWork.ExecuteInTransactionAsync(ct => ledger.ReleaseAsync(envelope.RequestId, "test", ct), CancellationToken.None);

        Assert.False(settledAgain);
        Assert.False(releasedAfter);
        Assert.Equal(new BalanceResponse("ZAR", 9m, 0m, 9m), await BalanceAsync(company.Organization.Id));
    }

    [Fact]
    public async Task A_queued_request_can_be_cancelled_and_its_reservation_released()
    {
        var (company, client) = await FundedLiveCompanyAsync(10m, TestProducts.Provider);

        var envelope = (await (await SendAsync(client, TestProducts.Provider, new { units = 2, scenario = "ok" }))
            .Content.ReadFromJsonAsync<ApiRequestEnvelope>())!;

        var cancel = await client.PostAsync($"/api/v1/requests/{envelope.RequestId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var summary = (await cancel.Content.ReadFromJsonAsync<ApiRequestSummary>())!;
        Assert.Equal(ApiRequestStatus.Cancelled, summary.Status);
        Assert.Equal(BillingState.Released, summary.Billing.State);
        Assert.Equal(new BalanceResponse("ZAR", 10m, 0m, 10m), await BalanceAsync(company.Organization.Id));

        // Nothing runs it afterwards, and a second cancel is refused.
        await _factory.DrainJobsAsync();
        Assert.Equal(0, _factory.Services.GetRequiredService<TestProviderLog>().CallsFor(envelope.RequestId));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/v1/requests/{envelope.RequestId}/cancel", null)).StatusCode);
        await AssertLedgerConsistentAsync(company.Organization.Id);
    }

    [Fact]
    public async Task Sandbox_requests_are_never_billable_and_need_no_funds()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        await company.EnableAsync(TestProducts.Metered, Contracts.Catalogue.ApiEnvironment.Sandbox);
        var key = await company.CreateKeyAsync(TestProducts.Metered);

        var response = await SendAsync(company.KeyClient(key.Secret), TestProducts.Metered, new { units = 5 }, idempotencyKey: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = (await response.Content.ReadFromJsonAsync<ApiRequestEnvelope>())!;
        Assert.Equal(new BillingInfo(BillingState.NotBillable, "ZAR", 0m), envelope.Billing);
        Assert.Equal(new BalanceResponse("ZAR", 0m, 0m, 0m), await BalanceAsync(company.Organization.Id));
    }

    private async Task<ClaimedJob> ClaimAsync(Guid requestId, string workerId)
    {
        // Make this job the only one due so the claim picks it.
        using var scope = _factory.CreateSystemScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();

        for (var i = 0; i < 50; i++)
        {
            var claim = await queue.ClaimNextAsync(workerId, TimeSpan.FromMinutes(2), CancellationToken.None)
                ?? throw new InvalidOperationException("Job was not claimable.");

            var job = await WithDbAsync(db => db.Jobs.SingleAsync(j => j.Id == claim.JobId));
            if (job.ApiRequestId == requestId)
            {
                return claim;
            }

            // Another test's leftover job: process it so it stops competing.
            using var tenant = _factory.CreateScopeAs(null, claim.WorkspaceId);
            await tenant.ServiceProvider.GetRequiredService<JobRunner>().RunAsync(claim, CancellationToken.None);
        }

        throw new InvalidOperationException("Job was not claimed.");
    }

    /// <summary>Steps the worker on this request's job until it reaches the given status.</summary>
    private async Task RunUntilAsync(Guid requestId, ApiRequestStatus target)
    {
        for (var i = 0; i < 10; i++)
        {
            var claim = await ClaimAsync(requestId, "step-worker");
            var workspaceId = claim.WorkspaceId;
            using (var tenant = _factory.CreateScopeAs(null, workspaceId))
            {
                await tenant.ServiceProvider.GetRequiredService<JobRunner>().RunAsync(claim, CancellationToken.None);
            }

            var status = await WithDbAsync(db => db.ApiRequests.Where(r => r.Id == requestId).Select(r => r.Status).SingleAsync());
            if (status == target)
            {
                return;
            }
        }

        throw new InvalidOperationException($"Request never reached {target}.");
    }

    private Task ExpireLeaseAsync(Guid requestId) =>
        WithDbAsync(db => db.Jobs.Where(j => j.ApiRequestId == requestId)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.LeaseExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))));
}
