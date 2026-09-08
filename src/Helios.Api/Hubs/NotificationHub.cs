using Helios.Contracts.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace Helios.Api.Hubs;

/// <summary>Per-user notifications, including approval requests that block a run.</summary>
public sealed class NotificationHub : HeliosHub
{
    public Task JoinUser(Guid userId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.User(userId));
}
