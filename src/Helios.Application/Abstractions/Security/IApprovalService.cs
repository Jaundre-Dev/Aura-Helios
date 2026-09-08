using Helios.Contracts.Agents;
using Helios.Contracts.Common;

namespace Helios.Application.Abstractions.Security;

public interface IApprovalService
{
    Task<Guid> RequestAsync(
        Guid agentRunId,
        ToolPermission permission,
        RiskLevel risk,
        string description,
        CancellationToken cancellationToken);

    Task GrantAsync(Guid approvalId, Guid userId, string? reason, CancellationToken cancellationToken);

    Task RejectAsync(Guid approvalId, Guid userId, string reason, CancellationToken cancellationToken);
}
