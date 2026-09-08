using Helios.Contracts.Common;

namespace Helios.Contracts.Ai;

/// <summary>
/// A capability request. ProviderId/ModelId stay null unless the caller is pinning
/// a model on purpose — otherwise the router decides.
/// </summary>
public sealed record ModelRequest
{
    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    public TaskType TaskType { get; init; } = TaskType.General;
    public DataClassification Classification { get; init; } = DataClassification.Internal;

    public string? ProviderId { get; init; }
    public string? ModelId { get; init; }

    public double? Temperature { get; init; }
    public int? MaxOutputTokens { get; init; }
    public string? StopSequence { get; init; }

    public IReadOnlyList<ToolDefinition>? Tools { get; init; }
    public string? ResponseJsonSchema { get; init; }

    /// <summary>Correlates the call back to the AgentRun that caused it.</summary>
    public Guid? AgentRunId { get; init; }
    public Guid? ProjectId { get; init; }
    public Guid? WorkspaceId { get; init; }
}
