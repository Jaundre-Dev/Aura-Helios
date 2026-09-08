namespace Helios.Contracts.Identity;

/// <summary>
/// Coarse role within a workspace. Fine-grained tool authority lives on the agent
/// version, not here — this decides what a person may do, not what an agent may do.
/// </summary>
public enum WorkspaceRole
{
    Viewer = 0,
    Reviewer = 1,
    Engineer = 2,
    Admin = 3,
    Owner = 4
}
