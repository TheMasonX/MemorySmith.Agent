namespace Agent.World.Minecraft;

using Agent.Core;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

/// <summary>
/// WebSocket bridge between the C# AgentHost and the Node.js Mineflayer process.
/// Serializes ActionData to JSON commands; deserializes incoming JSON to WorldEvent.
///
/// Token authentication: the Node process expects a "token" field in the first
/// message for security on localhost. Reconnect logic handles Node process restarts.
/// </summary>
public sealed class WebSocketBridge(string uri) : IDisposable
{
    private ClientWebSocket? _ws;
    private readonly Uri _uri = new(uri);

    public bool IsOpen => _ws?.State == WebSocketState.Open;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(_uri, cancellationToken);
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_ws?.State == WebSocketState.Open)
            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnect", cancellationToken);
    }

    public async Task SendAsync(ActionData action, CancellationToken cancellationToken = default)
    {
        if (_ws is null) throw new InvalidOperationException("Not connected.");
        var json = JsonSerializer.Serialize(action);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }

    public async IAsyncEnumerable<WorldEvent> ReceiveAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_ws is null) yield break;

        var buffer = new byte[8192];
        while (_ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var result = await _ws.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) yield break;

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            WorldEvent? ev = null;
            try { ev = JsonSerializer.Deserialize<WorldEvent>(json); } catch { /* skip malformed */ }
            if (ev is not null) yield return ev;
        }
    }

    public void Dispose() => _ws?.Dispose();
}
