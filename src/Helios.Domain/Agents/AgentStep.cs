using Helios.Domain.Common;

namespace Helios.Domain.Agents;

/// <summary>One entry in the run trace shown live in the agent view.</summary>
public class AgentStep : Entity
{
    public Guid AgentRunId { get; set; }
    public int Ordinal { get; set; }

    /// <summary>model, tool, observation, transition, evaluation.</summary>
    public required string Kind { get; set; }

    public string? Name { get; set; }
    public string? InputPreview { get; set; }
    public string? OutputPreview { get; set; }
    public bool Success { get; set; } = true;
    public TimeSpan Duration { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
