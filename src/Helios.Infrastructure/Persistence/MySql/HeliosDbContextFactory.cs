using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Helios.Infrastructure.Persistence.MySql;

/// <summary>
/// Lets "dotnet ef migrations" run without booting the API. Reads
/// HELIOS_MIGRATION_CONNECTION when set, otherwise assumes a local MySQL.
/// </summary>
public sealed class HeliosDbContextFactory : IDesignTimeDbContextFactory<HeliosDbContext>
{
    public HeliosDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("HELIOS_MIGRATION_CONNECTION")
            ?? "Server=localhost;Port=3306;Database=helios;User=root;Password=root;";

        var options = new DbContextOptionsBuilder<HeliosDbContext>();
        MySqlConfiguration.Configure(options, connectionString, serverVersion: null);

        return new HeliosDbContext(options.Options, new SystemWorkspaceContext());
    }
}
