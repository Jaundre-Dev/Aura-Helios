using Helios.Contracts.Common;
using Helios.Domain.Common;

namespace Helios.Domain.Ai;

/// <summary>Plan section 6.3. Policy decides the model; application modules never do.</summary>
public class RoutingPolicy : AuditableEntity, IAggregateRoot, IVersioned
{
    public Guid? WorkspaceId { get; set; }
    public required string Name { get; set; }
    public TaskType TaskType { get; set; } = TaskType.General;

    public List<string> PreferredProviders { get; set; } = [];
    public List<string> PreferredModels { get; set; } = [];
    public List<string> FallbackModels { get; set; } = [];

    public int MinimumQuality { get; set; }
    public decimal? MaximumCost { get; set; }
    public TimeSpan? MaximumLatency { get; set; }

    public bool LocalOnly { get; set; }
    public bool CloudAllowed { get; set; } = true;
    public DataClassification MaxClassificationForCloud { get; set; } = DataClassification.Confidential;

    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;
}
