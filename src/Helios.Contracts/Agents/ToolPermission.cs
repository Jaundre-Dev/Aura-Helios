using Helios.Contracts.Common;

namespace Helios.Contracts.Agents;

/// <summary>Plan section 8. A model never receives unrestricted authority.</summary>
public enum ToolPermission
{
    ReadRepository = 0,
    WriteRepository,
    ExecuteTerminal,
    CreatePullRequest,
    MergePullRequest,
    ReadProduction,
    WriteProduction,
    DeleteResource,
    Deploy
}

public static class ToolPermissionRisk
{
    private static readonly Dictionary<ToolPermission, RiskLevel> Map = new()
    {
        [ToolPermission.ReadRepository]    = RiskLevel.Low,
        [ToolPermission.WriteRepository]   = RiskLevel.Medium,
        [ToolPermission.ExecuteTerminal]   = RiskLevel.High,
        [ToolPermission.CreatePullRequest] = RiskLevel.Medium,
        [ToolPermission.MergePullRequest]  = RiskLevel.High,
        [ToolPermission.ReadProduction]    = RiskLevel.High,
        [ToolPermission.WriteProduction]   = RiskLevel.Critical,
        [ToolPermission.DeleteResource]    = RiskLevel.Critical,
        [ToolPermission.Deploy]            = RiskLevel.High
    };

    public static RiskLevel RiskOf(ToolPermission permission) => Map[permission];

    /// <summary>High and Critical actions require a human gate by default.</summary>
    public static bool RequiresApproval(ToolPermission permission) =>
        RiskOf(permission) >= RiskLevel.High;
}
