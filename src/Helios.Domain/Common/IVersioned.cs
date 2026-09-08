namespace Helios.Domain.Common;

/// <summary>
/// Agents, prompts, models, tools, workflows, policies and evaluators are versioned
/// (plan section 28.1). Anything implementing this is never edited in place.
/// </summary>
public interface IVersioned
{
    int Version { get; }
    bool IsActive { get; }
}
