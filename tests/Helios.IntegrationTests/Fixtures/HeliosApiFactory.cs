using Helios.Api.Configuration;
using Helios.Application.Abstractions.Security;
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

    private readonly DisposableTestDatabase _database = DisposableTestDatabase.FromEnvironment();

    public string DatabaseName => _database.Name;

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

        builder.ConfigureServices(services =>
        {
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
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await MySqlConnection.ClearAllPoolsAsync();
        await _database.DropAsync();
    }
}
