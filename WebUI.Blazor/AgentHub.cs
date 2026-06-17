// ──────────────────────────────────────────────────────────────────────────────
// Sprint 4a — SignalR AgentHub
// Sprint 4b — Extended with ChatMessage and GoalUpdate broadcasts.
//
// Pushes real-time agent status, chat messages, and goal changes to the
// dashboard via WebSocket. Falls back to polling when SignalR is unavailable
// (the JS client adds both paths).
// ──────────────────────────────────────────────────────────────────────────────

using Microsoft.AspNetCore.SignalR;

namespace WebUI.Blazor;

/// <summary>
/// Serializable snapshot of agent state for the dashboard. Keeps the hub
/// payload small and avoids coupling the SignalR contract to WorldState internals.
/// </summary>
public sealed record AgentStatusUpdate(
    string Status,              // "active", "idle", "disabled", "reconnecting"
    string? Goal,
    string? GoalDescription,
    int Health,
    int Food,
    int X, int Y, int Z,
    int QueuedActions,
    int ConsecutiveFailures,
    IReadOnlyDictionary<string, int> Inventory);

/// <summary>
/// SignalR hub for real-time agent event streaming to the dashboard.
///
/// Events:
///   StatusUpdated  — full state snapshot (pushed after every event-processing tick)
///   ChatMessage    — player or bot chat (pushed when a chat event is processed)
///   GoalUpdate     — goal change notification (pushed when SetGoal / CancelGoal fires)
/// </summary>
public sealed class AgentHub : Hub
{
    private const string Group = "dashboard";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, Group);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, Group);
        await base.OnDisconnectedAsync(exception);
    }
}
