using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace Helios.IntegrationTests.Fixtures;

/// <summary>
/// A database that exists only for one test run. The previous fixture recreated a fixed
/// <c>helios_test</c> schema with <c>EnsureDeleted</c>, which could drop a database someone else
/// owned and made two concurrent runs destroy each other. This one:
/// <list type="bullet">
///   <item>generates a fresh name per run (<c>helios_it_yyyyMMddHHmmss_xxxxxxxx</c>);</item>
///   <item>refuses any host outside a local allow-list unless explicitly opted in;</item>
///   <item>refuses to proceed if that name already exists, so it never adopts a database it did not create;</item>
///   <item>drops only the database it created, and only when the name still matches the run pattern.</item>
/// </list>
/// </summary>
public sealed partial class DisposableTestDatabase
{
    private const string Prefix = "helios_it_";

    /// <summary>Hosts a developer machine or CI service container would use. Anything else needs an explicit opt-in.</summary>
    private static readonly HashSet<string> LocalHosts =
        new(StringComparer.OrdinalIgnoreCase) { "localhost", "127.0.0.1", "::1", "mysql", "host.docker.internal" };

    private readonly string _serverConnectionString;
    private bool _created;

    public string Name { get; }

    /// <summary>Connection string for the disposable database, GUIDs stored as Binary16 like the app.</summary>
    public string ConnectionString { get; }

    private DisposableTestDatabase(string serverConnectionString, string name)
    {
        _serverConnectionString = serverConnectionString;
        Name = name;
        ConnectionString = new MySqlConnectionStringBuilder(serverConnectionString)
        {
            Database = name,
            GuidFormat = MySqlGuidFormat.Binary16
        }.ConnectionString;
    }

    /// <summary>
    /// Reads the server credentials from <c>HELIOS_TEST_CONNECTION</c>, or the Helios.Api
    /// user-secrets connection string, and keeps only host, port and credentials from it. The
    /// database named in the source is discarded — it is never connected to.
    /// </summary>
    public static DisposableTestDatabase FromEnvironment()
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
                "ConnectionStrings:MySql in the Helios.Api user-secrets. Only host, port and " +
                "credentials are used; the tests create and drop their own uniquely named database.");

        var server = new MySqlConnectionStringBuilder(source) { Database = string.Empty };

        if (!LocalHosts.Contains(server.Server) &&
            Environment.GetEnvironmentVariable("HELIOS_TEST_ALLOW_REMOTE_HOST") != "1")
        {
            throw new InvalidOperationException(
                $"Integration tests refuse to run against host '{server.Server}'. Use a local MySQL, or set " +
                "HELIOS_TEST_ALLOW_REMOTE_HOST=1 for a dedicated disposable test server. Never point tests at " +
                "a customer or shared development server.");
        }

        return new DisposableTestDatabase(server.ConnectionString, NewName());
    }

    internal static string NewName()
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var random = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
        return $"{Prefix}{stamp}_{random}";
    }

    internal static bool IsRunDatabaseName(string name) => RunName().IsMatch(name);

    /// <summary>Creates the database. Fails if the name exists rather than reusing someone else's schema.</summary>
    public async Task CreateAsync()
    {
        EnsureRunName();

        await using var connection = new MySqlConnection(_serverConnectionString);
        await connection.OpenAsync();

        await using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = @name";
            exists.Parameters.AddWithValue("@name", Name);

            if (Convert.ToInt64(await exists.ExecuteScalarAsync(), CultureInfo.InvariantCulture) != 0)
            {
                throw new InvalidOperationException(
                    $"Database '{Name}' already exists. Refusing to adopt a database this run did not create.");
            }
        }

        await using (var create = connection.CreateCommand())
        {
            // No IF NOT EXISTS: a race with another creator must fail, not share.
            create.CommandText = $"CREATE DATABASE `{Name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci";
            await create.ExecuteNonQueryAsync();
        }

        _created = true;
    }

    /// <summary>Drops the database, but only one this instance created under the run naming pattern.</summary>
    public async Task DropAsync()
    {
        if (!_created)
        {
            return;
        }

        EnsureRunName();

        await using var connection = new MySqlConnection(_serverConnectionString);
        await connection.OpenAsync();

        await using var drop = connection.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS `{Name}`";
        await drop.ExecuteNonQueryAsync();

        _created = false;
    }

    private void EnsureRunName()
    {
        if (!IsRunDatabaseName(Name))
        {
            throw new InvalidOperationException(
                $"'{Name}' does not match the disposable test database pattern; refusing to create or drop it.");
        }
    }

    [GeneratedRegex("^helios_it_[0-9]{14}_[0-9a-f]{8}$", RegexOptions.CultureInvariant)]
    private static partial Regex RunName();
}
