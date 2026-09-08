using Helios.Contracts.Agents;
using Helios.Contracts.Common;
using Helios.Domain.Common;

namespace Helios.Domain.Agents;

/// <summary>
/// The observability unit of the platform (plan section 17). Every run records what
/// was asked, which model answered, which tools ran, what it cost and how it scored.
/// </summary>
public class AgentRun : AuditableEntity, IAggregateRoot
{
    public Guid AgentDefinitionId { get; set; }
    public Guid AgentVersionId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid? ProjectId { get; set; }

    public required string Goal { get; set; }
    public AgentRunState State { get; set; } = AgentRunState.Created;
    public DataClassification Classification { get; set; } = DataClassification.Internal;

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public TimeSpan? Duration => CompletedAt - StartedAt;

    public string? RoutingDecision { get; set; }
    public string? ResolvedProviderId { get; set; }
    public string? ResolvedModelId { get; set; }

    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public decimal? EstimatedCost { get; set; }
    public int ToolCallCount { get; set; }
    public int RetryCount { get; set; }
    public int HumanInterventionCount { get; set; }

    public string? Result { get; set; }
    public string? Error { get; set; }
    public double? EvaluationScore { get; set; }

    public List<AgentStep> Steps { get; set; } = [];
}
