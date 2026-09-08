using Helios.Contracts.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace Helios.Api.Hubs;

/// <summary>Live agent trace: steps, tool calls, streamed model output, approvals.</summary>
public sealed class AgentHub : HeliosHub
{
    public Task JoinRun(Guid agentRunId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.AgentRun(agentRunId));

    public Task LeaveRun(Guid agentRunId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, HubGroups.AgentRun(agentRunId));
}
