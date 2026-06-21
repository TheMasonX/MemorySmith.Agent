namespace WebUI.Blazor.Dashboard;

/// <summary>
/// Centralized SignalR hub event name constants.
/// All dashboard SignalR events should use these strings to prevent drift
/// between the C# hub and the JavaScript client.
/// </summary>
public static class DashboardHubEvents
{
    /// <summary>Full state snapshot pushed after every event tick.</summary>
    public const string SnapshotUpdated = "SnapshotUpdated";

    /// <summary>Goal change notification (set/cancel).</summary>
    public const string GoalUpdated = "GoalUpdated";

    /// <summary>New chat message (player or bot).</summary>
    public const string ChatReceived = "ChatReceived";
}
