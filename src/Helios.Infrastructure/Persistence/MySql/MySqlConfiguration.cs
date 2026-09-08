using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Helios.Infrastructure.Persistence.MySql;

/// <summary>
/// One place that decides how Helios talks to MySQL, shared by the runtime host and the
/// design-time factory so migrations can never be generated against different settings
/// than the ones the application runs with.
/// </summary>
internal static class MySqlConfiguration
{
    /// <summary>
    /// Matches the native MySQL80 service on the development machine. Override through
    /// configuration when the target server differs — the compose stack runs 8.4.
    /// </summary>
    private static readonly Version DefaultServerVersion = new(8, 0, 0);

    public static void Configure(
        DbContextOptionsBuilder options,
        string connectionString,
        string? serverVersion)
    {
        var version = ParseServerVersion(serverVersion);

        options.UseMySql(
            Normalise(connectionString),
            new MySqlServerVersion(version),
            mysql => mysql
                .MigrationsAssembly(typeof(MySqlConfiguration).Assembly.FullName)
                .MigrationsHistoryTable("__helios_migrations_history")
                .EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null));
    }

    /// <summary>
    /// Forces GuidFormat=Binary16 whatever the supplied connection string says.
    /// <para>
    /// The domain issues time-ordered UUIDv7 keys. Binary16 preserves that ordering in
    /// big-endian byte order, so clustered-index inserts stay sequential on the tables
    /// that grow fastest. Left to its default the driver would store char(36) — twenty
    /// extra bytes per row and no index locality. LittleEndianBinary16 would store
    /// compactly but scramble the ordering, which is the worst of both.
    /// </para>
    /// </summary>
    private static string Normalise(string connectionString)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            GuidFormat = MySqlGuidFormat.Binary16
        };

        return builder.ConnectionString;
    }

    private static Version ParseServerVersion(string? configured) =>
        Version.TryParse(configured, out var parsed) ? parsed : DefaultServerVersion;
}
