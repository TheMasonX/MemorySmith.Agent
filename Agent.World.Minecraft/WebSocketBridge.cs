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
///
/// Wire-name responsibility: each tool sets <see cref="ActionData.Tool"/> to the
/// correct Node.js action name via <see cref="ActionProtocol"/> constants.
/// This bridge forwards the value as-is (no lowercasing). See ADR-010.
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
    ///
    /// The <see cref="ActionData.Tool"/> value is forwarded as-is — tools set the
    /// correct wire name via <see cref="ActionProtocol"/> constants.
    /// </summary>
    public async Task SendAsync(ActionData action, CancellationToken cancellationToken = default)
    {
        if (_ws is not { State: WebSocketState.Open })
            throw new InvalidOperationException("WebSocket is not connected.");

        using var ms = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(ms);

        writer.WriteStartObject();
        writer.WriteString("action", action.Tool); // wire name set by the tool via ActionProtocol
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

    /// <summary>
    /// Sprint 3a: Parse JSON into typed event subtypes instead of
    /// <c>WorldEvent(string, Dictionary, DateTimeOffset)</c>.
    /// </summary>
    private static WorldEvent? ParseEvent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("event", out var eventProp))
                return null;

            var eventType = eventProp.GetString() ?? string.Empty;
            var now       = DateTimeOffset.UtcNow;

            return eventType switch
            {
                "spawn" => new SpawnEvent(
                    Pos: new Position(GetInt(root, "x"), GetInt(root, "y"), GetInt(root, "z")),
                    Health: GetInt(root, "hp", 20),
                    Food: GetInt(root, "food", 20),
                    Timestamp: now),

                "health" => new HealthEvent(
                    Health: GetInt(root, "hp", 20),
                    Food: GetInt(root, "food", 20),
                    Timestamp: now),

                "move" or "moveComplete" => new MoveEvent(
                    Pos: new Position(GetInt(root, "x"), GetInt(root, "y"), GetInt(root, "z")),
                    Timestamp: now),

                "blockMined" => new BlockMinedEvent(
                    Block: GetString(root, "block") ?? "unknown",
                    Count: GetInt(root, "count", 1),
                    Pos: new Position(GetInt(root, "x"), GetInt(root, "y"), GetInt(root, "z")),
                    Timestamp: now),

                "chat" => new ChatEvent(
                    Username: GetString(root, "username") ?? "?",
                    Message: GetString(root, "message") ?? string.Empty,
                    OnlinePlayers: GetInt(root, "onlinePlayers", 1),
                    PlayerPos: root.TryGetProperty("playerX", out _)
                        ? new Position(GetInt(root, "playerX"), GetInt(root, "playerY"), GetInt(root, "playerZ"))
                        : null,
                    Timestamp: now),

                "error" => new ErrorEvent(
                    Action: GetString(root, "action") ?? "?",
                    Message: GetString(root, "message") ?? "unknown",
                    Timestamp: now),

                "blockNotFound" => new BlockNotFoundEvent(
                    Block: GetString(root, "block") ?? "?",
                    MinedCount: GetInt(root, "mined"),
                    Timestamp: now),

                "craftComplete" => new CraftCompleteEvent(
                    Item: GetString(root, "item") ?? "?",
                    Count: GetInt(root, "count", 1),
                    Timestamp: now),

                "smeltComplete" => new SmeltCompleteEvent(
                    Input: GetString(root, "item") ?? "?",
                    Result: GetString(root, "result") ?? "?",
                    Count: GetInt(root, "count", 1),
                    Timestamp: now),

                "death" => new DeathEvent(
                    Pos: new Position(GetInt(root, "x"), GetInt(root, "y"), GetInt(root, "z")),
                    Timestamp: now),

                "status" => ParseStatus(root, now),

                "blockPlaced" => new BlockPlacedEvent(
                    X: GetInt(root, "x"), Y: GetInt(root, "y"), Z: GetInt(root, "z"),
                    Block: GetString(root, "block") ?? "?",
                    Timestamp: now),

                "wanderComplete" => new WanderCompleteEvent(
                    Pos: new Position(GetInt(root, "x"), GetInt(root, "y"), GetInt(root, "z")),
                    TargetX: GetInt(root, "targetX"),
                    TargetZ: GetInt(root, "targetZ"),
                    Timestamp: now),

                "wanderFailed" => new WanderFailedEvent(
                    Message: GetString(root, "message") ?? "?",
                    Pos: new Position(GetInt(root, "x"), GetInt(root, "y"), GetInt(root, "z")),
                    Timestamp: now),

                "kicked" => new KickedEvent(
                    Reason: GetString(root, "reason") ?? "?",
                    Timestamp: now),

                "flatAreaFound" => new FlatAreaFoundEvent(
                    X: GetInt(root, "x"),
                    Y: GetInt(root, "y"),
                    Z: GetInt(root, "z"),
                    Area: GetInt(root, "area"),
                    MinX: GetInt(root, "minX"),
                    MaxX: GetInt(root, "maxX"),
                    MinZ: GetInt(root, "minZ"),
                    MaxZ: GetInt(root, "maxZ"),
                    Timestamp: now),

                _ => null, // unknown event type — ignored
            };
        }
        catch
        {
            return null;
        }
    }

    private static StatusEvent ParseStatus(JsonElement root, DateTimeOffset now)
    {
        var inv = new Dictionary<string, int>();
        if (root.TryGetProperty("inventory", out var invEl))
        {
            if (invEl.ValueKind == JsonValueKind.String)
            {
                // legacy: inventory sent as JSON string
                try
                {
                    using var invDoc = JsonDocument.Parse(invEl.GetString()!);
                    foreach (var prop in invDoc.RootElement.EnumerateObject())
                        if (prop.Value.TryGetInt32(out var qty) && qty > 0)
                            inv[prop.Name] = qty;
                }
                catch { /* malformed inventory — leave empty */ }
            }
            else if (invEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in invEl.EnumerateObject())
                    if (prop.Value.TryGetInt32(out var qty) && qty > 0)
                        inv[prop.Name] = qty;
            }
        }

        return new StatusEvent(
            Pos: new Position(GetInt(root, "x"), GetInt(root, "y"), GetInt(root, "z")),
            Health: GetInt(root, "hp", 20),
            Food: GetInt(root, "food", 20),
            Inventory: inv,
            Timestamp: now);
    }

    // ── JSON helpers ──────────────────────────────────────────────────────────

    private static int GetInt(JsonElement root, string key, int defaultValue = 0)
    {
        if (!root.TryGetProperty(key, out var el)) return defaultValue;
        return el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i)
            ? i : defaultValue;
    }

    private static string? GetString(JsonElement root, string key, string? defaultValue = null)
    {
        if (!root.TryGetProperty(key, out var el)) return defaultValue;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : defaultValue;
    }

    // ── Dispose ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _receiveCts?.Cancel();
        _receiveCts?.Dispose();
        _ws?.Dispose();
    }
}
