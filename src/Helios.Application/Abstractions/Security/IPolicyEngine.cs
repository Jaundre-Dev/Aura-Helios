using Helios.Contracts.Agents;
using Helios.Contracts.Common;

namespace Helios.Application.Abstractions.Security;

/// <summary>
/// Least privilege. Every tool action passes through here before it runs, and the
/// decision is written to the audit trail whether it was allowed or denied.
/// </summary>
public interface IPolicyEngine
{
    Task<PolicyDecision> EvaluateAsync(PolicyRequest request, CancellationToken cancellationToken);
}

public sealed record PolicyRequest
{
    public required Guid WorkspaceId { get; init; }
    public required Guid AgentRunId { get; init; }
    public required ToolPermission Permission { get; init; }
    public required string ToolName { get; init; }
    public DataClassification Classification { get; init; } = DataClassification.Internal;
    public string? ResourceId { get; init; }
}

public sealed record PolicyDecision
{
    public required bool Allowed { get; init; }
    public bool RequiresApproval { get; init; }
    public RiskLevel Risk { get; init; }
    public string? Reason { get; init; }

    public static PolicyDecision Allow(RiskLevel risk) =>
        new() { Allowed = true, Risk = risk };

    public static PolicyDecision NeedsHuman(RiskLevel risk, string reason) =>
        new() { Allowed = true, RequiresApproval = true, Risk = risk, Reason = reason };

    public static PolicyDecision Deny(string reason) =>
        new() { Allowed = false, Reason = reason };
}
