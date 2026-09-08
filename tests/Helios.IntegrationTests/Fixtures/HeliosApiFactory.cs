using Helios.Application.Abstractions.Security;
using Helios.Infrastructure.Persistence.MySql;
using Helios.Infrastructure.Persistence.MySql.Interceptors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MySqlConnector;

namespace Helios.IntegrationTests.Fixtures;

/// <summary>
/// Identity for a test run. Replaces <see cref="IWorkspaceContext"/> in the container so
/// the endpoints can be exercised before WP0.4 delivers real authentication.
/// </summary>
/// <remarks>
/// A test-only seam, not a development backdoor. Nothing in the API can select it, so no
/// configuration or header makes production skip authentication.
/// </remarks>
public sealed class TestWorkspaceContext : IWorkspaceContext
{
    public Guid? UserId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public bool IsSystem { get; set; }
    public string? IpAddress => "127.0.0.1";
}

/// <summary>
/// Boots the real API against a real MySQL schema. Testcontainers is the plan's target
/// once Docker is installed; until then this uses the native MySQL already on the machine
/// and a throwaway database.
/// </summary>
public sealed class HeliosApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>
    /// The only database these tests may touch. Enforced twice below, because this
    /// fixture calls <c>EnsureDeleted</c> and an earlier version of it dropped the
    /// developer's real schema when configuration precedence put the app's own
    /// user-secrets ahead of the test override.
    /// </summary>
    private const string TestDatabase = "helios_test";

    public TestWorkspaceContext Context { get; } = new();

    /// <summary>
    /// Reads the developer's connection string for its credentials and host, then
    /// replaces the database name outright rather than by string substitution — a
    /// substitution only works if the original happens to be named what you expected.
    /// </summary>
    private static string BuildTestConnectionString()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<Program>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var source =
            Environment.GetEnvironmentVariable("HELIOS_TEST_CONNECTION")
            ?? configuration.GetConnectionString("MySql")
            ?? throw new InvalidOperationException(
                "No connection string for integration tests. Set HELIOS_TEST_CONNECTION, or " +
                "ConnectionStrings:MySql in the Helios.Api user-secrets.");

        return new MySqlConnectionStringBuilder(source)
        {
            Database = TestDatabase,
            GuidFormat = MySqlGuidFormat.Binary16
        }.ConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWorkspaceContext>();
            services.AddSingleton<IWorkspaceContext>(Context);

            // Re-register the context outright instead of overriding configuration.
            // Configuration precedence between the host's own sources and a test
            // override is subtle and version-dependent; this is not.
            services.RemoveAll<DbContextOptions<HeliosDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<HeliosDbContext>();

            services.AddDbContext<HeliosDbContext>((provider, options) =>
            {
                MySqlConfiguration.Configure(options, BuildTestConnectionString(), "8.0.46");
                options.AddInterceptors(provider.GetRequiredService<AuditableEntityInterceptor>());
            });
        });
    }

    /// <summary>
    /// Last line of defence before anything destructive runs. Asks the context which
    /// database it actually resolved to, rather than trusting that the wiring above did
    /// what it was supposed to.
    /// </summary>
    private static void AssertTargetsTestDatabase(HeliosDbContext db)
    {
        var actual = db.Database.GetDbConnection().Database;

        if (!string.Equals(actual, TestDatabase, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Integration tests resolved to database '{actual}', not '{TestDatabase}'. " +
                "Refusing to run: this fixture drops and recreates whatever it points at.");
        }
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();

        AssertTargetsTestDatabase(db);

        // Rebuild from the real migrations rather than EnsureCreated, so these tests fail
        // if a migration is broken — which is most of the point of running them.
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();

        AssertTargetsTestDatabase(db);

        await db.Database.EnsureDeletedAsync();
    }
}
