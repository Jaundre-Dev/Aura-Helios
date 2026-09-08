using Helios.Contracts.Agents;
using Helios.Contracts.Ai;

namespace Helios.Application.Abstractions.Tools;

/// <summary>
/// Plan section 8. Every tool declares the permission it needs; the runtime checks
/// it against the agent version, workspace policy and — when required — a human.
/// </summary>
public interface IAgentTool
{
    string Name { get; }

    ToolDefinition Definition { get; }

    ToolPermission RequiredPermission { get; }

    Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken);
}
