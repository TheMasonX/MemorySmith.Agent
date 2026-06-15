namespace Agent.Core;

/// <summary>
/// Abstracts game-world communication. The Minecraft adapter implements this via
/// a Node.js/Mineflayer subprocess connected over WebSocket. Other game adapters
/// (or mock adapters for testing) can be substituted without changing agent logic.
/// </summary>
public interface IWorldAdapter
{
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task SendActionAsync(ActionData action, CancellationToken cancellationToken = default);
    IAsyncEnumerable<WorldEvent> ReceiveEventsAsync(CancellationToken cancellationToken = default);
}
