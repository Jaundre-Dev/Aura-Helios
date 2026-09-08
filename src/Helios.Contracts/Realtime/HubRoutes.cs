namespace Helios.Contracts.Realtime;

/// <summary>SignalR hub paths — shared by the API and the web client.</summary>
public static class HubRoutes
{
    public const string Workspace    = "/hubs/workspace";
    public const string Agent        = "/hubs/agent";
    public const string Project      = "/hubs/project";
    public const string Notification = "/hubs/notification";
    public const string Workflow     = "/hubs/workflow";
    public const string Monitoring   = "/hubs/monitoring";
}

/// <summary>Client-side method names invoked by the server.</summary>
public static class HubMethods
{
    public const string ReceiveEvent        = "ReceiveEvent";
    public const string ReceiveStreamChunk  = "ReceiveStreamChunk";
    public const string ReceiveNotification = "ReceiveNotification";
}

/// <summary>Group naming so a client only receives what it subscribed to.</summary>
public static class HubGroups
{
    public static string Workspace(Guid id) => $"workspace:{id}";
    public static string Project(Guid id) => $"project:{id}";
    public static string AgentRun(Guid id) => $"run:{id}";
    public static string User(Guid id) => $"user:{id}";
}
