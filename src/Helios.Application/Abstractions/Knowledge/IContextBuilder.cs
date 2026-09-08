using Helios.Contracts.Ai;
using Helios.Contracts.Common;

namespace Helios.Application.Abstractions.Knowledge;

/// <summary>
/// Plan section 11.2. Never send an entire repository to a model by default:
/// context is selected, ranked, compressed and policy-filtered.
/// </summary>
public interface IContextBuilder
{
    Task<BuiltContext> BuildAsync(ContextRequest request, CancellationToken cancellationToken);
}

public sealed record ContextRequest
{
    public required string Task { get; init; }
    public Guid? ProjectId { get; init; }
    public Guid? AgentRunId { get; init; }
    public DataClassification Classification { get; init; } = DataClassification.Internal;
    public int TokenBudget { get; init; } = 32_000;
}

public sealed record BuiltContext
{
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public required IReadOnlyList<ContextSource> Sources { get; init; }
    public int EstimatedTokens { get; init; }
    public int DroppedForBudget { get; init; }
    public int FilteredByPolicy { get; init; }
}

/// <summary>Citation evidence. Retrieved fact and model interpretation stay distinguishable.</summary>
public sealed record ContextSource(
    string Kind,
    string Reference,
    string? Version,
    double Relevance);
