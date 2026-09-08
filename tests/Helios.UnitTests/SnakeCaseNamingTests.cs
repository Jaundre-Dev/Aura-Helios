using Helios.Infrastructure.Persistence.MySql;

namespace Helios.UnitTests;

/// <summary>
/// The naming convention decides every identifier in the database, so its edge cases are
/// worth pinning down. Acronyms are the ones that go wrong quietly.
/// </summary>
public class SnakeCaseNamingTests
{
    [Theory]
    [InlineData("Id", "id")]
    [InlineData("Name", "name")]
    [InlineData("WorkspaceId", "workspace_id")]
    [InlineData("AgentRunId", "agent_run_id")]
    [InlineData("EstimatedCost", "estimated_cost")]
    [InlineData("MaxOutputTokens", "max_output_tokens")]
    public void Splits_pascal_case_on_word_boundaries(string input, string expected)
    {
        Assert.Equal(expected, SnakeCaseNaming.ToSnakeCase(input));
    }

    [Theory]
    [InlineData("AIProvider", "ai_provider")]
    [InlineData("MySqlGuid", "my_sql_guid")]
    [InlineData("IPAddress", "ip_address")]
    public void Breaks_at_the_end_of_an_acronym_not_inside_it(string input, string expected)
    {
        Assert.Equal(expected, SnakeCaseNaming.ToSnakeCase(input));
    }

    [Theory]
    [InlineData("already_snake", "already_snake")]
    [InlineData("audit_logs", "audit_logs")]
    public void Leaves_snake_case_untouched(string input, string expected)
    {
        Assert.Equal(expected, SnakeCaseNaming.ToSnakeCase(input));
    }

    [Fact]
    public void Handles_empty_input()
    {
        Assert.Equal(string.Empty, SnakeCaseNaming.ToSnakeCase(string.Empty));
    }
}
