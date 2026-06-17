using Microsoft.AspNetCore.SignalR;

namespace WebUI.Blazor;

/// <summary>
/// SignalR hub for real-time dashboard push.
/// Clients join the "dashboard" group on connection to receive
/// StatusUpdated, ChatMessage, and GoalUpdate events.
/// </summary>
public sealed class AgentHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "dashboard");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "dashboard");
        await base.OnDisconnectedAsync(exception);
    }
}
