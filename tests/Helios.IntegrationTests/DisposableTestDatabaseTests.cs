using Helios.Infrastructure.Persistence.MySql;
using Helios.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.IntegrationTests;

/// <summary>Proves the suite really talks to its own freshly migrated disposable database.</summary>
[Collection(HeliosApiCollection.Name)]
public sealed class DisposableDatabaseConnectionTests(HeliosApiFactory factory)
{
    [Fact]
    public async Task The_api_is_connected_to_this_runs_migrated_disposable_database()
    {
        using var scope = factory.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();

        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DATABASE()";
        var current = (string?)await command.ExecuteScalarAsync();

        Assert.Equal(factory.DatabaseName, current);
        Assert.True(DisposableTestDatabase.IsRunDatabaseName(current!));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
}

/// <summary>
/// The fixture's naming guard, checked without a database: only names this fixture generates
/// may ever be created or dropped, so no real schema can match.
/// </summary>
public sealed class DisposableTestDatabaseTests
{
    [Fact]
    public void Generated_names_match_the_run_pattern_and_are_unique()
    {
        var first = DisposableTestDatabase.NewName();
        var second = DisposableTestDatabase.NewName();

        Assert.True(DisposableTestDatabase.IsRunDatabaseName(first));
        Assert.True(DisposableTestDatabase.IsRunDatabaseName(second));
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("helios")]
    [InlineData("helios_test")]
    [InlineData("helios_it_")]
    [InlineData("helios_it_20260101000000_ABCDEF12")]
    [InlineData("helios_it_20260101000000_abcdef12; DROP DATABASE helios")]
    [InlineData("smartceo")]
    public void Other_names_are_never_treated_as_disposable(string name)
    {
        Assert.False(DisposableTestDatabase.IsRunDatabaseName(name));
    }
}
