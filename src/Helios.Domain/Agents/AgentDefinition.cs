using Helios.Contracts.Agents;
using Helios.Domain.Common;

namespace Helios.Domain.Agents;

/// <summary>The stable identity of an agent. Behaviour lives in AgentVersion.</summary>
public class AgentDefinition : AuditableEntity, IAggregateRoot
{
    public Guid WorkspaceId { get; set; }
    public required string Name { get; set; }

    /// <summary>developer, security-analyst, architect, qa, pm, devops, reviewer.</summary>
    public required string Role { get; set; }

    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;

    public List<AgentVersion> Versions { get; set; } = [];
}
