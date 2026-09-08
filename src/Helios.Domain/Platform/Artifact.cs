using Helios.Domain.Common;

namespace Helios.Domain.Platform;

/// <summary>Durable output of a run: a report, a diff, a diagram, a postmortem.</summary>
public class Artifact : AuditableEntity
{
    public Guid? AgentRunId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid WorkspaceId { get; set; }

    public required string Name { get; set; }

    /// <summary>report, diff, diagram, adr, postmortem, test-result, scan-result.</summary>
    public required string Kind { get; set; }

    public required string ContentType { get; set; }
    public string? StorageRef { get; set; }
    public string? InlineContent { get; set; }
    public long SizeBytes { get; set; }
}
