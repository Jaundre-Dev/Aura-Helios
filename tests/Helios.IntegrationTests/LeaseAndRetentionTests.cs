using System.Net.Http.Json;
using System.Text.Json;
using Helios.Application.Features.Retention;
using Helios.Contracts.Billing;
using Helios.Contracts.Requests;
using Helios.Contracts.Webhooks;
using Helios.Domain.Execution;
using Helios.Domain.Webhooks;
using Helios.Infrastructure.Persistence.MySql;
using Helios.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.IntegrationTests;

/// <summary>
/// Long work keeps its lease (so it is not reclaimed and repeated), a worker that lost its lease
/// stops and commits nothing, and finished webhook deliveries are purged after their retention.
/// </summary>
[Collection(HeliosApiCollection.Name)]
public sealed class LeaseAndRetentionTests(HeliosApiFactory factory)
{
    private readonly HeliosApiFactory _factory = factory;
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    private async Task<T> WithDbAsync<T>(Func<HeliosDbContext, Task<T>> query)
    {
        using var scope = _factory.CreateSystemScope();
        return await query(scope.ServiceProvider.GetRequiredService<HeliosDbContext>());
    }

    private async Task<(TestCompany Company, Guid RequestId)> SlowRequestAsync()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var key = company.KeyClient((await company.CreateLiveKeyAsync(TestProducts.Provider)).Secret);
        await _factory.FundAsync(company.Organization.Id, 10m);

        var message = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/products/{TestProducts.Provider}/requests")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { units = 1, scenario = "slow" }), System.Text.Encoding.UTF8, "application/json")
        };
        message.Headers.Add("Idempotency-Key", $"k-{Guid.NewGuid():N}");

        var response = await key.SendAsync(message);
        response.EnsureSuccessStatusCode();
        return (company, (await response.Content.ReadFromJsonAsync<ApiRequestEnvelope>())!.RequestId);
    }

    private Task<Job> JobAsync(Guid requestId) => WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.ApiRequestId == requestId));

    [Fact]
    public async Task A_long_attempt_keeps_extending_its_lease_and_completes_once()
    {
        var log = _factory.Services.GetRequiredService<TestProviderLog>();
        var (company, id) = await SlowRequestAsync();

        var worker = Task.Run(() => _factory.DrainJobsAsync($"slow-{id:N}"));
        await log.Started(id).Task.WaitAsync(Wait);

        var first = (await JobAsync(id)).LeaseExpiresAt!.Value;
        var deadline = DateTime.UtcNow + Wait;
        DateTimeOffset later;
        do
        {
            await Task.Delay(100);
            later = (await JobAsync(id)).LeaseExpiresAt!.Value;
        }
        while (later <= first && DateTime.UtcNow < deadline);

        Assert.True(later > first, "The running attempt never renewed its lease.");

        log.Gate(id).TrySetResult();
        await worker.WaitAsync(Wait);

        var job = await JobAsync(id);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Null(job.LeaseExpiresAt);
        Assert.Equal(1, log.CallsFor(id));

        using var scope = _factory.CreateSystemScope();
        var balance = await scope.ServiceProvider.GetRequiredService<Application.Features.Billing.LedgerService>()
            .GetBalanceAsync(company.Organization.Id, CancellationToken.None);
        Assert.Equal(new BalanceResponse("ZAR", 7.5m, 0m, 7.5m), balance);
    }

    [Fact]
    public async Task A_worker_that_loses_its_lease_stops_and_commits_nothing()
    {
        var log = _factory.Services.GetRequiredService<TestProviderLog>();
        var (company, id) = await SlowRequestAsync();

        var worker = Task.Run(() => _factory.DrainJobsAsync($"loser-{id:N}"));
        await log.Started(id).Task.WaitAsync(Wait);

        // Another worker takes the job over: its claim moves the fencing token.
        await WithDbAsync(db => db.Jobs.IgnoreQueryFilters().Where(j => j.ApiRequestId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.FencingToken, j => j.FencingToken + 1).SetProperty(j => j.LeaseOwner, "new-owner")));

        await worker.WaitAsync(Wait);
        Assert.True(log.Cancelled.GetValueOrDefault(id), "The executor was not told to stop.");

        var job = await JobAsync(id);
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Equal("new-owner", job.LeaseOwner);
        Assert.Equal(0, await WithDbAsync(db => db.UsageEvents.IgnoreQueryFilters().CountAsync(u => u.ApiRequestId == id)));

        // The new owner's lease lapses; recovery reconciles (the provider did not finish) and re-runs.
        log.Gate(id).TrySetResult();
        await WithDbAsync(db => db.Jobs.IgnoreQueryFilters().Where(j => j.ApiRequestId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.LeaseExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))));
        await _factory.DrainJobsAsync($"recovery-{id:N}");

        Assert.Equal(JobStatus.Succeeded, (await JobAsync(id)).Status);
        Assert.Equal(1, await WithDbAsync(db => db.UsageEvents.IgnoreQueryFilters().CountAsync(u => u.ApiRequestId == id)));
        Assert.Equal(1, await WithDbAsync(db => db.LedgerTransactions.CountAsync(t => t.ApiRequestId == id && t.Type == LedgerTransactionType.Settle)));
    }

    [Fact]
    public async Task Finished_deliveries_are_purged_after_retention_but_pending_ones_never_are()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var host = $"hooks-{Guid.NewGuid():N}.example.com";
        var created = await company.Owner.Client.PostAsJsonAsync("/api/v1/webhooks",
            new CreateWebhookRequest($"https://{host}/helios", [WebhookEventTypes.RequestSucceeded]));
        var endpoint = (await created.Content.ReadFromJsonAsync<CreatedWebhookResponse>())!.Endpoint.Id;

        var old = DateTimeOffset.UtcNow.AddDays(-45);
        var ids = await WithDbAsync(async db =>
        {
            WebhookDelivery Make(WebhookDeliveryStatus status, DateTimeOffset at) => new()
            {
                EndpointId = endpoint,
                OrganizationId = company.Organization.Id,
                WorkspaceId = company.Workspace.Id,
                EventId = $"evt_test_{Guid.NewGuid():N}",
                EventType = WebhookEventTypes.RequestSucceeded,
                PayloadJson = "{}",
                Status = status,
                CreatedAt = at,
                NextAttemptAt = status == WebhookDeliveryStatus.Pending ? DateTimeOffset.UtcNow.AddYears(1) : at
            };

            var oldDelivered = Make(WebhookDeliveryStatus.Delivered, old);
            var oldFailed = Make(WebhookDeliveryStatus.Failed, old);
            var oldPending = Make(WebhookDeliveryStatus.Pending, old);
            var recent = Make(WebhookDeliveryStatus.Delivered, DateTimeOffset.UtcNow.AddDays(-1));
            db.WebhookDeliveries.AddRange(oldDelivered, oldFailed, oldPending, recent);
            await db.SaveChangesAsync();
            return (oldDelivered.Id, oldFailed.Id, oldPending.Id, recent.Id);
        });

        await _factory.Services.GetRequiredService<RetentionSweeper>().SweepAsync(1000, CancellationToken.None);

        var remaining = await WithDbAsync(db => db.WebhookDeliveries.IgnoreQueryFilters()
            .Where(d => d.EndpointId == endpoint).Select(d => d.Id).ToListAsync());

        Assert.DoesNotContain(ids.Item1, remaining);
        Assert.DoesNotContain(ids.Item2, remaining);
        Assert.Contains(ids.Item3, remaining);
        Assert.Contains(ids.Item4, remaining);
    }
}
