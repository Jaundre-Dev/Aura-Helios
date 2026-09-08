using Helios.Contracts.Agents;
using Helios.Domain.Common;

namespace Helios.Domain.Agents;

/// <summary>
/// An immutable snapshot of agent behaviour (plan section 7.2). Changing an agent
/// creates a new version so runs stay reproducible and regressions stay traceable.
/// </summary>
public class AgentVersion : AuditableEntity, IVersioned
{
    public Guid AgentDefinitionId { get; set; }
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; }

    public required string SystemPrompt { get; set; }
    public int PromptVersion { get; set; } = 1;

    public Guid? RoutingPolicyId { get; set; }
    public List<string> AllowedTools { get; set; } = [];
    public List<ToolPermission> Permissions { get; set; } = [];
    public List<ToolPermission> RequiresApproval { get; set; } = [];

    public int MaxTokenBudget { get; set; } = 200_000;
    public decimal? MaxCostBudget { get; set; }
    public TimeSpan MaxDuration { get; set; } = TimeSpan.FromMinutes(30);
    public int MaxToolCalls { get; set; } = 100;

    public string? MemoryPolicy { get; set; }
    public string? KnowledgeScope { get; set; }
    public string? EvaluationPolicy { get; set; }
}
