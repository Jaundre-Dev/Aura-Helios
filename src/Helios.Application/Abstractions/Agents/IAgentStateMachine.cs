using Helios.Contracts.Agents;

namespace Helios.Application.Abstractions.Agents;

/// <summary>
/// Guards the transitions in plan section 7.1 so a run can never reach an
/// undefined state. Illegal transitions throw rather than silently proceed.
/// </summary>
public interface IAgentStateMachine
{
    bool CanTransition(AgentRunState from, AgentRunState to);

    IReadOnlyList<AgentRunState> AllowedFrom(AgentRunState state);
}
