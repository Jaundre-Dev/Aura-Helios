using System.Security.Claims;
using System.Text.Encodings.Web;
using Helios.Api.Security;
using Helios.Application.Abstractions.Security;
using Helios.Infrastructure.Persistence.MySql;
using Helios.Infrastructure.Persistence.MySql.Interceptors;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Helios.IntegrationTests.Fixtures;

/// <summary>
/// Authenticates every request from the <see cref="TestWorkspaceContext"/> the test set on
/// the fixture, so <c>RequireAuthorization</c> is satisfied without minting a real JWT per
/// call. The real JWT pipeline is covered separately by <c>AuthEndpointTests</c> and the
/// token-issuer unit tests; these endpoint tests are about isolation and RBAC, not signing.
/// </summary>
public sealed class TestAuthHandler(
    IWorkspaceContext workspaceContext,
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // No user set on the context means an anonymous client — let the challenge return
        // 401, which is what an unauthenticated caller should see.
        if (workspaceContext.UserId is not { } userId)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new(HeliosClaims.Subject, userId.ToString()) };

        if (workspaceContext.WorkspaceId is { } workspaceId)
        {
            claims.Add(new Claim(HeliosClaims.Workspace, workspaceId.ToString()));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, HeliosClaims.Subject, HeliosClaims.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

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

        // Give JwtOptions.ValidateOnStart a valid key so the host boots without depending on
        // the developer's user-secrets. These tests do not validate real tokens.
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Helios:Jwt:SigningKey"] = "integration-tests-signing-key-not-a-real-secret",
                ["Helios:Jwt:Issuer"] = "helios-test",
                ["Helios:Jwt:Audience"] = "helios-test",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWorkspaceContext>();
            services.AddSingleton<IWorkspaceContext>(Context);

            // Make the test scheme the default so RequireAuthorization uses it. The real
            // JwtBearer scheme stays registered but is no longer the default.
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

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
