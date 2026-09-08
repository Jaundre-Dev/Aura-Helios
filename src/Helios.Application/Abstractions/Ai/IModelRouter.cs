using Helios.Contracts.Ai;

namespace Helios.Application.Abstractions.Ai;

/// <summary>Chooses provider and model from capability, policy, health, cost and latency.</summary>
public interface IModelRouter
{
    Task<RoutingDecision> RouteAsync(
        ModelRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Recorded on every AgentRun so a result can always be explained.</summary>
public sealed record RoutingDecision
{
    public required string ProviderId { get; init; }
    public required string ModelId { get; init; }
    public required string Reason { get; init; }
    public bool ForcedLocal { get; init; }
    public IReadOnlyList<string> Fallbacks { get; init; } = [];
    public IReadOnlyList<string> RejectedCandidates { get; init; } = [];
}
