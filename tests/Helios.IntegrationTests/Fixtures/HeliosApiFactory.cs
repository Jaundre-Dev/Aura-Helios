using Helios.Api.Configuration;
using Helios.Application.Abstractions.Execution;
using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Features.Billing;
using Helios.Application.Features.Execution;
using Helios.Application.Features.Webhooks;
using Helios.Application.Features.Products;
using Helios.Contracts.Catalogue;
using Helios.Domain.Catalogue;
using Helios.Infrastructure.Persistence.MySql;
using Helios.Infrastructure.Persistence.MySql.Interceptors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MySqlConnector;

namespace Helios.IntegrationTests.Fixtures;

/// <summary>
/// An identity for code that runs outside an HTTP request in a test — the stores, for instance.
/// Set on one service scope only (see <see cref="HeliosApiFactory.CreateScopeAs"/>), so it can
/// never leak into a request: HTTP calls always resolve the real <see cref="HttpWorkspaceContext"/>.
/// </summary>
public sealed class TestWorkspaceContext : IWorkspaceContext
{
    public Guid? UserId { get; init; }
    public Guid? WorkspaceId { get; init; }
    public Guid? ApiKeyId => null;
    public bool IsSystem { get; init; }
    public string? IpAddress => "127.0.0.1";
    public string? CorrelationId => "test";
}

/// <summary>Scoped holder for an optional per-scope identity override.</summary>
public sealed class TestScopeIdentity
{
    public IWorkspaceContext? Override { get; set; }
}

/// <summary>
/// Boots the real API — real JWT authentication, real session checks — against a disposable,
/// uniquely named MySQL database created for this run and dropped afterwards. See
/// <see cref="DisposableTestDatabase"/> for the safeguards.
/// </summary>
public sealed class HeliosApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SigningKey = "integration-tests-signing-key-not-a-real-secret";
    public const string FakeGatewaySecret = "integration-tests-fake-gateway-secret-not-real";
    public const string FakeGatewayMerchant = "test-merchant";

    private readonly DisposableTestDatabase _database = DisposableTestDatabase.FromEnvironment();

    public string DatabaseName => _database.Name;

    /// <summary>Records every webhook delivery; answers 200 unless a URL is told to fail.</summary>
    public RecordingWebhookReceiver WebhookReceiver { get; } = new();

    /// <summary>Delivers due webhooks until none are left. Returns how many were attempted.</summary>
    public async Task<int> DrainWebhooksAsync(int max = 100)
    {
        var dispatcher = Services.GetRequiredService<WebhookDispatcher>();
        var attempted = 0;

        while (attempted < max && await dispatcher.DeliverNextAsync(CancellationToken.None))
        {
            attempted++;
        }

        return attempted;
    }

    /// <summary>The disposable database's connection string, for hosts other than the API (the worker).</summary>
    public string DatabaseConnectionString => _database.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        // UseSetting lands in host configuration before Program reads it, unlike
        // ConfigureAppConfiguration under minimal hosting. None of these are real secrets.
        builder.UseSetting("ConnectionStrings:MySql", _database.ConnectionString);
        builder.UseSetting("Helios:Jwt:SigningKey", SigningKey);
        builder.UseSetting("Helios:Jwt:Issuer", "helios-test");
        builder.UseSetting("Helios:Jwt:Audience", "helios-test");
        builder.UseSetting("Helios:Secrets:ActiveKeyId", "test");
        builder.UseSetting("Helios:Secrets:Keys:test", Convert.ToBase64String(new byte[32]));

        // Many tests register accounts from the same in-memory client address. The throttle
        // itself is covered by a dedicated test with a low limit.
        builder.UseSetting("Helios:RateLimits:Auth:PermitLimit", "100000");

        // Retries and reconciliation become due immediately, so tests step the worker without
        // waiting. Lease expiry is simulated by moving lease_expires_at.
        builder.UseSetting("Helios:Execution:RetryBaseDelaySeconds", "0");
        builder.UseSetting("Helios:Execution:ReconcileDelaySeconds", "0");
        builder.UseSetting("Helios:Execution:MaxReconcileAttempts", "3");

        builder.UseSetting("Helios:Webhooks:MaxAttempts", "3");
        builder.UseSetting("Helios:Webhooks:BaseDelaySeconds", "0");

        // The fake gateway, with a test-only signing secret. Production refuses this configuration.
        builder.UseSetting("Helios:Payments:Gateway", "fake-test");
        builder.UseSetting("Helios:Payments:FakeTest:WebhookSecret", FakeGatewaySecret);
        builder.UseSetting("Helios:Payments:FakeTest:MerchantId", FakeGatewayMerchant);

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<TestProviderLog>();
            services.AddSingleton<IProductExecutor, TestMeteredExecutor>();
            services.AddSingleton<IProductExecutor, TestProviderExecutor>();
            services.AddSingleton<ITenantScopeFactory, TestTenantScopes>();
            services.AddSingleton<JobWorker>();
            services.AddSingleton<WebhookDispatcher>();

            // Webhook deliveries go to an in-memory receiver instead of the network. The real,
            // connect-time SSRF guard is exercised directly in WebhookTests.
            services.AddHttpClient(Infrastructure.Webhooks.GuardedWebhookSender.ClientName)
                .ConfigurePrimaryHttpMessageHandler(() => WebhookReceiver);

            services.AddScoped<TestScopeIdentity>();
            services.RemoveAll<IWorkspaceContext>();
            services.AddScoped<IWorkspaceContext>(sp =>
                sp.GetRequiredService<TestScopeIdentity>().Override
                ?? ActivatorUtilities.CreateInstance<HttpWorkspaceContext>(sp));

            // Re-register the context outright as well: whatever the developer's own
            // configuration says, the only database these tests can reach is the disposable one.
            services.RemoveAll<DbContextOptions<HeliosDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<HeliosDbContext>();

            services.AddDbContext<HeliosDbContext>((provider, options) =>
            {
                MySqlConfiguration.Configure(options, _database.ConnectionString, "8.0.0");
                options.AddInterceptors(provider.GetRequiredService<AuditableEntityInterceptor>());
            });
        });
    }

    /// <summary>A service scope acting as the given identity, for tests that bypass HTTP.</summary>
    public IServiceScope CreateScopeAs(Guid? userId, Guid? workspaceId, bool isSystem = false)
    {
        var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestScopeIdentity>().Override =
            new TestWorkspaceContext { UserId = userId, WorkspaceId = workspaceId, IsSystem = isSystem };
        return scope;
    }

    /// <summary>A scope with system authority, for test setup that edits state directly.</summary>
    public IServiceScope CreateSystemScope() => CreateScopeAs(null, null, isSystem: true);

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _database.CreateAsync();

        using var scope = CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();

        // Last line of defence: ask the context where it actually points before migrating.
        var actual = db.Database.GetDbConnection().Database;
        if (!string.Equals(actual, _database.Name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Integration tests resolved to database '{actual}', not the disposable '{_database.Name}'. Refusing to run.");
        }

        // The real migrations, so a broken migration fails the suite.
        await db.Database.MigrateAsync();

        await SeedTestProductsAsync(scope.ServiceProvider);
    }

    /// <summary>Test-only, live-callable products with prices, so billing paths can be exercised.</summary>
    private static async Task SeedTestProductsAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<HeliosDbContext>();
        var prices = services.GetRequiredService<PriceService>();

        foreach (var (slug, mode, unitPrice) in new[]
                 {
                     (TestProducts.Metered, "sync", TestProducts.MeteredUnitPrice),
                     (TestProducts.Provider, "async", TestProducts.ProviderUnitPrice)
                 })
        {
            var product = new ApiProduct
            {
                Slug = slug,
                Name = $"Test product ({mode})",
                Category = "Test",
                Summary = "Integration-test-only product.",
                Delivery = ProductDelivery.Build,
                ReleaseState = ProductReleaseState.Live,
                Sensitivity = ProductSensitivity.Standard,
                BillingUnit = "Unit",
                CurrentVersion = "1"
            };

            db.ApiProducts.Add(product);
            db.ApiProductVersions.Add(new ApiProductVersion
            {
                ProductId = product.Id,
                Version = "1",
                ReleaseState = ProductReleaseState.Live,
                MaxInputBytes = 4096,
                PublishedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();

            await prices.PublishAsync(slug, ApiEnvironment.Live, "unit", unitPrice, 0m, "vat_exclusive_standard",
                DateTimeOffset.UtcNow.AddMinutes(-1), actor: null, CancellationToken.None);
        }
    }

    /// <summary>Credits a company's available balance through an audited ledger adjustment.</summary>
    public async Task FundAsync(Guid organizationId, decimal amount)
    {
        using var scope = CreateSystemScope();
        var ledger = scope.ServiceProvider.GetRequiredService<LedgerService>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await unitOfWork.ExecuteInTransactionAsync(
            ct => ledger.AdjustAsync(organizationId, $"test-fund:{Guid.NewGuid():N}", amount, "Test funding", null, ct),
            CancellationToken.None);
    }

    /// <summary>Runs worker ticks until nothing is due. Returns how many jobs were processed.</summary>
    public async Task<int> DrainJobsAsync(string workerId = "test-worker", int max = 50)
    {
        var worker = Services.GetRequiredService<JobWorker>();
        var processed = 0;

        while (processed < max && await worker.ProcessNextAsync(workerId, CancellationToken.None))
        {
            processed++;
        }

        return processed;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await MySqlConnection.ClearAllPoolsAsync();
        await _database.DropAsync();
    }
}
