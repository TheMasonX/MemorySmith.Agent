namespace Agent.World.Minecraft;

using Agent.Core;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

/// <summary>
/// WebSocket bridge between the C# agent host and the Node.js/Mineflayer process.
///
/// Sends action commands as JSON; receives world events as JSON.
/// A background receive loop buffers incoming frames into a Channel so that
/// <see cref="ReceiveAsync"/> and <see cref="SendAsync"/> can run concurrently
/// without blocking each other.
///
/// C# → Node command protocol:
///   {"action":"move",    "arguments":{"x":10,"y":64,"z":20}}
///   {"action":"mine",    "arguments":{"block":"minecraft:oak_log","count":5}}
///   {"action":"status",  "arguments":{}}
///
/// Node → C# event protocol:
///   {"event":"spawn",        "x":100,"y":64,"z":100}
///   {"event":"health",       "hp":20,"food":20}
///   {"event":"move",         "x":110,"y":64,"z":110}
///   {"event":"blockMined",   "block":"oak_log","count":3}
///   {"event":"moveComplete", "x":110,"y":64,"z":110}
///   {"event":"status",       "x":100,"y":64,"z":100,"hp":20}
///   {"event":"error",        "message":"path blocked"}
/// </summary>
public sealed class WebSocketBridge(string uri) : IDisposable
{
    private ClientWebSocket? _ws;
    private readonly Uri _uri = new(uri);
    private CancellationTokenSource? _receiveCts;

    // Inbound events buffered from the background receive loop
    private readonly Channel<WorldEvent> _inbound =
        Channel.CreateUnbounded<WorldEvent>(new UnboundedChannelOptions { SingleWriter = true });

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public bool IsOpen => _ws?.State == WebSocketState.Open;

    // ── Connect / Close ──────────────────────────────────────────────────────

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(_uri, cancellationToken);

        // Start background receive loop
        _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = ReceiveLoopAsync(_receiveCts.Token);
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        _receiveCts?.Cancel();
        if (_ws?.State == WebSocketState.Open)
        {
            try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnect", cancellationToken); }
            catch { /* best effort */ }
        }
    }

    // ── Send ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Serialises an <see cref="ActionData"/> to the Node.js command protocol
    /// and sends it over the WebSocket.
    /// </summary>
    public async Task SendAsync(ActionData action, CancellationToken cancellationToken = default)
    {
        if (_ws is not { State: WebSocketState.Open })
            throw new InvalidOperationException("WebSocket is not connected.");

        // Node.js expects lowercase "action" + "arguments"
        using var ms = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(ms);

        writer.WriteStartObject();
        writer.WriteString("action", action.Tool.ToLowerInvariant());
        writer.WritePropertyName("arguments");
        writer.WriteStartObject();
        foreach (var kv in action.Arguments)
        {
            switch (kv.Value)
            {
                case int i:    writer.WriteNumber(kv.Key, i);  break;
                case long l:   writer.WriteNumber(kv.Key, l);  break;
                case double d: writer.WriteNumber(kv.Key, d);  break;
                case float f:  writer.WriteNumber(kv.Key, f);  break;
                case bool b:   writer.WriteBoolean(kv.Key, b); break;
                case null:     writer.WriteNull(kv.Key);        break;
                default:       writer.WriteString(kv.Key, kv.Value.ToString()); break;
            }
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken);

        await _ws.SendAsync(ms.ToArray(), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }

    // ── Receive (consumer-facing) ─────────────────────────────────────────────

    /// <summary>
    /// Streams <see cref="WorldEvent"/>s as they arrive from Node.js.
    /// Events are buffered by the background loop so this never races with
    /// <see cref="SendAsync"/>.
    /// </summary>
    public async IAsyncEnumerable<WorldEvent> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            WorldEvent ev;
            try { ev = await _inbound.Reader.ReadAsync(cancellationToken); }
            catch (OperationCanceledException) { yield break; }
            catch (ChannelClosedException) { yield break; }
            yield return ev;
        }
    }

    // ── Background receive loop ───────────────────────────────────────────────

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[32_768];
        var sb = new StringBuilder(4096);

        try
        {
            while (_ws is { State: WebSocketState.Open } && !ct.IsCancellationRequested)
            {
                sb.Clear();
                WebSocketReceiveResult result;

                // Reassemble multi-frame messages
                do
                {
                    result = await _ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                var ev = ParseEvent(sb.ToString());
                if (ev is not null)
                    _inbound.Writer.TryWrite(ev);
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (WebSocketException) { /* connection dropped */ }
        finally
        {
            _inbound.Writer.TryComplete();
        }
    }

    // ── JSON parsing ─────────────────────────────────────────────────────────

    private static WorldEvent? ParseEvent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("event", out var eventProp))
                return null;

            var eventType = eventProp.GetString() ?? "unknown";
            var payload   = new Dictionary<string, object?>();

            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Name == "event") continue;
                payload[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.Number  => prop.Value.TryGetInt32(out var i)
                                                ? (object?)i
                                                : prop.Value.GetDouble(),
                    JsonValueKind.String  => prop.Value.GetString(),
                    JsonValueKind.True    => true,
                    JsonValueKind.False   => false,
                    JsonValueKind.Null    => null,
                    _                    => prop.Value.GetRawText(),
                };
            }

            return new WorldEvent(eventType, payload, DateTimeOffset.UtcNow);
        }
        catch
        {
            return null;
        }
    }

    // ── Dispose ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _receiveCts?.Cancel();
        _receiveCts?.Dispose();
        _ws?.Dispose();
    }
}
