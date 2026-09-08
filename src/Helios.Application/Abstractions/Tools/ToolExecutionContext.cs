using Helios.Contracts.Common;

namespace Helios.Application.Abstractions.Tools;

public sealed record ToolExecutionContext
{
    public required Guid AgentRunId { get; init; }
    public required Guid WorkspaceId { get; init; }
    public Guid? ProjectId { get; init; }
    public Guid? ActorUserId { get; init; }

    public required string ArgumentsJson { get; init; }
    public DataClassification Classification { get; init; } = DataClassification.Internal;

    /// <summary>Absolute path the tool is confined to. Nothing outside it is readable.</summary>
    public string? WorkingDirectory { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
}

public sealed record ToolResult
{
    public required bool Success { get; init; }
    public string? Output { get; init; }
    public string? Error { get; init; }
    public TimeSpan Duration { get; init; }

    /// <summary>Set when the tool stopped to wait for a human decision.</summary>
    public Guid? PendingApprovalId { get; init; }

    public static ToolResult Ok(string output, TimeSpan duration = default) =>
        new() { Success = true, Output = output, Duration = duration };

    public static ToolResult Fail(string error, TimeSpan duration = default) =>
        new() { Success = false, Error = error, Duration = duration };

    public static ToolResult NeedsApproval(Guid approvalId) =>
        new() { Success = false, PendingApprovalId = approvalId, Error = "Awaiting human approval." };
}
