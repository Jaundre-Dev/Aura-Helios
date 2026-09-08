using Helios.Contracts.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace Helios.Api.Hubs;

public sealed class ProjectHub : HeliosHub
{
    public Task JoinProject(Guid projectId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Project(projectId));

    public Task LeaveProject(Guid projectId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, HubGroups.Project(projectId));
}
