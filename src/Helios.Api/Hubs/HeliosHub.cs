using Helios.Contracts.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace Helios.Api.Hubs;

/// <summary>
/// Shared subscription plumbing. A client only receives what it explicitly joined,
/// so a workspace never leaks another workspace's run trace.
/// </summary>
public abstract class HeliosHub : Hub
{
    public Task JoinWorkspace(Guid workspaceId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Workspace(workspaceId));

    public Task LeaveWorkspace(Guid workspaceId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, HubGroups.Workspace(workspaceId));
}
