namespace Agent.World.Minecraft;

using Agent.Core;

/// <summary>
/// IWorldAdapter implementation for Minecraft via Node.js/Mineflayer.
///
/// The C# host spawns a Node process (Process.Start) running MineflayerAdapter/index.js.
/// Communication uses WebSocket over localhost: C# sends JSON commands, Node sends JSON events.
///
/// Protocol (C# → Node):
///   {"action":"mine","block":"minecraft:iron_ore","quantity":5}
///
/// Protocol (Node → C#):
///   {"event":"blockMined","block":"iron_ore","count":3,"position":{"x":10,"y":64,"z":20}}
///
/// The adapter encapsulates all Minecraft-specific logic. Higher-level agent code
/// uses only IWorldAdapter — it does not know about Mineflayer or Node.
/// </summary>
public sealed class MinecraftAdapter : IWorldAdapter, IDisposable
{
    private WebSocketBridge? _bridge;
    private System.Diagnostics.Process? _nodeProcess;

    public bool IsConnected => _bridge?.IsOpen ?? false;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        // TODO: start Node subprocess
        _bridge = new WebSocketBridge("ws://localhost:3000");
        await _bridge.ConnectAsync(cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_bridge is not null)
            await _bridge.CloseAsync(cancellationToken);

        if (_nodeProcess is not null && !_nodeProcess.HasExited)
            _nodeProcess.Kill();
    }

    public Task SendActionAsync(ActionData action, CancellationToken cancellationToken = default)
    {
        if (_bridge is null || !_bridge.IsOpen)
            throw new InvalidOperationException("Minecraft adapter is not connected.");

        return _bridge.SendAsync(action, cancellationToken);
    }

    public IAsyncEnumerable<WorldEvent> ReceiveEventsAsync(CancellationToken cancellationToken = default)
    {
        if (_bridge is null)
            throw new InvalidOperationException("Minecraft adapter is not connected.");

        return _bridge.ReceiveAsync(cancellationToken);
    }

    public void Dispose()
    {
        _bridge?.Dispose();
        _nodeProcess?.Dispose();
    }
}
