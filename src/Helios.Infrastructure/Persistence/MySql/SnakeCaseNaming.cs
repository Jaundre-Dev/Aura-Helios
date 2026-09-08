using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Helios.Infrastructure.Persistence.MySql;

/// <summary>
/// MySQL identifier case sensitivity differs between Windows and Linux. Forcing
/// snake_case removes the class of bug where a migration works locally and fails in a
/// container. Applied after entity configurations so nothing escapes it.
/// </summary>
internal static class SnakeCaseNaming
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (entity.GetTableName() is { } table)
            {
                entity.SetTableName(ToSnakeCase(table));
            }

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }

            foreach (var key in entity.GetKeys())
            {
                if (key.GetName() is { } name)
                {
                    key.SetName(ToSnakeCase(name));
                }
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                if (foreignKey.GetConstraintName() is { } name)
                {
                    foreignKey.SetConstraintName(ToSnakeCase(name));
                }
            }

            foreach (var index in entity.GetIndexes())
            {
                if (index.GetDatabaseName() is { } name)
                {
                    index.SetDatabaseName(ToSnakeCase(name));
                }
            }
        }
    }

    internal static string ToSnakeCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 8);

        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];

            if (char.IsUpper(current))
            {
                var previous = i > 0 ? value[i - 1] : '\0';
                var next = i + 1 < value.Length ? value[i + 1] : '\0';

                // Break before a capital that starts a word, and at the end of an acronym
                // so "AgentRunId" becomes agent_run_id and "AIProvider" becomes ai_provider.
                var startsWord = i > 0 && (previous != '_') &&
                    (!char.IsUpper(previous) || (char.IsUpper(previous) && char.IsLower(next)));

                if (startsWord)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(current));
            }
            else
            {
                builder.Append(current);
            }
        }

        return builder.ToString();
    }
}
