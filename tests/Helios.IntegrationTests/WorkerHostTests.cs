using System.Net;
using System.Text.Json;
using Helios.Application.Features.Products;
using Helios.Contracts.Requests;
using Helios.Infrastructure.Persistence.MySql;
using Helios.IntegrationTests.Fixtures;
using Helios.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Helios.IntegrationTests;

/// <summary>
/// Boots the real worker process wiring (<see cref="WorkerServiceCollectionExtensions.AddHeliosWorker"/>)
/// against the test database and lets its background pollers pick up an accepted job — proving the
/// production worker, not only the test harness, completes and settles work.
/// </summary>
[Collection(HeliosApiCollection.Name)]
public sealed class WorkerHostTests(HeliosApiFactory factory)
{
    [Fact]
    public async Task The_worker_host_completes_and_settles_a_queued_job()
    {
        var company = await TestCompany.OnboardAsync(factory);
        var key = await company.CreateLiveKeyAsync(TestProducts.Provider);
        await factory.FundAsync(company.Organization.Id, 10m);

        var message = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/products/{TestProducts.Provider}/requests")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { units = 1, scenario = "ok" }), System.Text.Encoding.UTF8, "application/json")
        };
        message.Headers.Add("Idempotency-Key", $"worker-{Guid.NewGuid():N}");
        var response = await company.KeyClient(key.Secret).SendAsync(message);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var requestId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("requestId").GetGuid();

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MySql"] = factory.DatabaseConnectionString,
            ["Helios:Database:ServerVersion"] = "8.0.0",
            ["Helios:Secrets:ActiveKeyId"] = "test",
            ["Helios:Secrets:Keys:test"] = Convert.ToBase64String(new byte[32]),
            ["Helios:Worker:Concurrency"] = "2",
            ["Helios:Worker:PollSeconds"] = "0.1",
        });
        builder.Services.AddHeliosWorker(builder.Configuration);

        // The test product's executor and its provider log, as the API test host has them.
        builder.Services.AddSingleton(factory.Services.GetRequiredService<TestProviderLog>());
        builder.Services.AddSingleton<IProductExecutor, TestProviderExecutor>();

        using var host = builder.Build();
        await host.StartAsync();

        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            ApiRequestStatus status;
            do
            {
                await Task.Delay(200);
                using var scope = factory.CreateSystemScope();
                status = await scope.ServiceProvider.GetRequiredService<HeliosDbContext>()
                    .ApiRequests.Where(r => r.Id == requestId).Select(r => r.Status).SingleAsync();
            }
            while (status != ApiRequestStatus.Succeeded && DateTime.UtcNow < deadline);

            Assert.Equal(ApiRequestStatus.Succeeded, status);
        }
        finally
        {
            await host.StopAsync();
        }

        using (var scope = factory.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
            var request = await db.ApiRequests.SingleAsync(r => r.Id == requestId);
            Assert.Equal(BillingState.Settled, request.BillingState);
            Assert.Equal(TestProducts.ProviderUnitPrice, request.BillingAmount);
        }
    }
}
