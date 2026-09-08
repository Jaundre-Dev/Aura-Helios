namespace Helios.Application.Abstractions.Tools;

public interface IToolRegistry
{
    IAgentTool? Find(string name);

    IReadOnlyList<IAgentTool> All();

    /// <summary>Only the tools the given agent version is allowed to see.</summary>
    IReadOnlyList<IAgentTool> ForAgentVersion(Guid agentVersionId);
}
