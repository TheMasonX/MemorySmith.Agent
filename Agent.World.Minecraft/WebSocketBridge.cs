namespace Agent.World.Minecraft;

using Agent.Core;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
///   {"event":"itemCollected","item":"diamond","count":1}   Sprint 35 P0-A
///   {"event":"mineComplete", "block":"oak_log","mined":5,"targetCount":5}  Sprint 35 P0-B
///
/// Wire-name responsibility: each tool sets <see cref="ActionData.Tool"/> to the
/// correct Node.js action name via <see cref="ActionProtocol"/> constants.
/// This bridge forwards the value as-is (no lowercasing). See ADR-010.
/// </summary>
public sealed class WebSocketBridge(string uri,
    ILogger<WebSocketBridge>? logger = null) : IDisposable
{
    private readonly ILogger<WebSocketBridge> _logger = logger ?? NullLogger<WebSocketBridge>.Instance;
    private ClientWebSocket? _ws;
    private readonly Uri _uri = new(uri);
    private CancellationTokenSource? _receiveCts;
    private string? _adapterSecret;

    // Inbound events buffered from the background receive loop
    private readonly Channel<WorldEvent> _inbound =
        Channel.CreateUnbounded<WorldEvent>(new UnboundedChannelOptions { SingleWriter = true });

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public bool IsOpen => _ws?.State == WebSocketState.Open;

    // ── Connect / Close ──────────────────────────────────────────────────────

    public async Task ConnectAsync(CancellationToken cancellationToken = default,
        string? adapterSecret = null)
    {
        _adapterSecret = adapterSecret;
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(_uri, cancellationToken);

        // Sprint 32 SEC-02: send handshake message so the Node.js server can validate
        // the shared secret before accepting commands. The secret is never logged.
        // When null or empty, no handshake is sent (dev/localhost mode).
        if (!string.IsNullOrWhiteSpace(adapterSecret))
        {
            using var ms = new System.IO.MemoryStream();
            using var writer = new Utf8JsonWriter(ms);
            writer.WriteStartObject();
            writer.WriteString("type", "handshake");
            writer.WriteString("secret", adapterSecret);
            writer.WriteEndObject();
            await writer.FlushAsync(cancellationToken);
            await _ws.SendAsync(ms.ToArray(), WebSocketMessageType.Text,
                endOfMessage: true, cancellationToken);
        }

        // Start background receive loop
        _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = RunReceiveLoopWithRetryAsync(_receiveCts.Token);
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        _receiveCts?.Cancel();
        if (_ws?.State == WebSocketState.Open)
        {
            try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnect", cancellationToken); }
            catch (Exception ex) { _logger.LogWarning(ex, "WebSocketBridge.CloseAsync: {Message}", ex.Message); }
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

    private async Task RunReceiveLoopWithRetryAsync(CancellationToken ct)
    {
        const int maxRetries = 3;
        const int retryDelayMs = 5000;

        try
        {
            for (var attempt = 0; attempt <= maxRetries; attempt++)
            {
                if (ct.IsCancellationRequested) return;

                try
                {
                    await ReceiveLoopAsync(ct);
                    return; // Normal completion (connection closed cleanly)
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("WebSocketBridge: Receive loop cancelled (normal shutdown)");
                    return;
                }
                catch (WebSocketException ex) when (attempt < maxRetries)
                {
                    _logger.LogWarning(ex,
                        "WebSocketBridge: Receive loop crashed (attempt {Attempt}/{MaxRetries}). Reconnecting in {DelayMs}ms...",
                        attempt + 1, maxRetries, retryDelayMs);
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    _logger.LogWarning(ex,
                        "WebSocketBridge: Receive loop crashed with unexpected error (attempt {Attempt}/{MaxRetries}). Reconnecting in {DelayMs}ms...",
                        attempt + 1, maxRetries, retryDelayMs);
                }

                // Attempt reconnection
                try
                {
                    await Task.Delay(retryDelayMs, ct);

                    // Dispose old socket and reconnect
                    if (_ws is not null)
                    {
                        try { _ws.Dispose(); } catch { /* best-effort cleanup */ }
                    }

                    _ws = new ClientWebSocket();
                    await _ws.ConnectAsync(_uri, ct);

                    // Re-send handshake if previously configured
                    if (!string.IsNullOrWhiteSpace(_adapterSecret))
                    {
                        using var ms = new System.IO.MemoryStream();
                        using var writer = new Utf8JsonWriter(ms);
                        writer.WriteStartObject();
                        writer.WriteString("type", "handshake");
                        writer.WriteString("secret", _adapterSecret);
                        writer.WriteEndObject();
                        await writer.FlushAsync(ct);
                        await _ws.SendAsync(ms.ToArray(), WebSocketMessageType.Text,
                            endOfMessage: true, ct);
                    }

                    _logger.LogInformation("WebSocketBridge: Reconnect succeeded (attempt {Attempt})", attempt + 1);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "WebSocketBridge: Reconnect failed (attempt {Attempt}/{MaxRetries})",
                        attempt + 1, maxRetries);
                }
            }

            // All retries exhausted — permanent failure
            _logger.LogError(
                "WebSocketBridge: All {MaxRetries} reconnect attempts failed. Agent is permanently deaf.",
                maxRetries);
        }
        finally
        {
            // TSK-0112: complete the inbound channel on ALL terminal paths
            // (normal close, cancellation, error, retry exhaustion) so readers
            // are never left suspended on a clean shutdown.
            _inbound.Writer.TryComplete();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[32_768];
        var sb = new StringBuilder(4096);

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

            var ev = ParseEvent(sb.ToString(), _logger);
            if (ev is not null)
                _inbound.Writer.TryWrite(ev);
        }
    }

    // ── JSON parsing ─────────────────────────────────────────────────────────

    /// <summary>
    /// Sprint 3a: Parse JSON into typed event subtypes instead of
    /// <c>WorldEvent(string, Dictionary, DateTimeOffset)</c>.
    /// Sprint 35 P0-A: Added itemCollected → ItemCollectedEvent.
    /// Sprint 35 P0-B: Added mineComplete → MineCompleteEvent.
    /// Sprint 35 P0-C: FlatAreaFoundEvent now includes SearchedRadius.
    /// </summary>
    private static WorldEvent? ParseEvent(string json, ILogger<WebSocketBridge> instanceLogger)
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

                "gameMode" => new GameModeChangedEvent(
                    Mode: GetString(root, "mode") ?? "unknown",
                    Timestamp: now),

                "move" or "moveComplete" => new MoveEvent(
                    Pos: new Position(GetInt(root, "x"), GetInt(root, "y"), GetInt(root, "z")),
                    Timestamp: now),

                "blockMined" => new BlockMinedEvent(
                    Block: GetString(root, "block") ?? "unknown",
                    Count: GetInt(root, "count", 1),
                    Pos: new Position(GetInt(root, "x"), GetInt(root, "y"), GetInt(root, "z")),
                    BlockPosition: new Position(
                        GetInt(root, "blockX", GetInt(root, "x")),
                        GetInt(root, "blockY", GetInt(root, "y")),
                        GetInt(root, "blockZ", GetInt(root, "z"))),
                    Timestamp: now),

                // Sprint 35 P0-A: actual item collected by the bot (playerCollect event in Mineflayer).
                // Provides the true drop name (e.g. "diamond" from diamond_ore, "cobblestone" from stone).
                // Guard in index.js ensures only the bot's own collections reach here.
                "itemCollected" => new ItemCollectedEvent(
                    Item: GetString(root, "item") ?? "unknown",
                    Count: GetInt(root, "count", 1),
                    Timestamp: now),

                // Sprint 35 P0-B: mining loop completed — definitive end-of-mine signal.
                // Sprint 40 P0-B: includes block position from the LAST mined block.
                "mineComplete" => new MineCompleteEvent(
                    Block: GetString(root, "block") ?? "unknown",
                    Mined: GetInt(root, "mined"),
                    TargetCount: GetInt(root, "targetCount"),
                    Timestamp: now,
                    BlockPosition: root.TryGetProperty("blockX", out _)
                        ? new Position(
                            GetInt(root, "blockX"),
                            GetInt(root, "blockY"),
                            GetInt(root, "blockZ"))
                        : new Position(0, 0, 0)),

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
                    Timestamp: now,
                    X: GetIntOrNull(root, "x"),
                    Y: GetIntOrNull(root, "y"),
                    Z: GetIntOrNull(root, "z"),
                    Block: GetString(root, "block"),
                    Material: GetString(root, "material"),
                    Item: GetString(root, "item"),
                    ReasonCode: GetString(root, "reasonCode")),

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

                "status" => ParseStatus(root, now, instanceLogger),

                "blockPlaced" => new BlockPlacedEvent(
                    X: GetInt(root, "x"), Y: GetInt(root, "y"), Z: GetInt(root, "z"),
                    Block: GetString(root, "block") ?? "?",
                    Timestamp: now,
                    CorrelationId: TryGetGuid(root, "correlationId")),

                // Sprint 43 (P0-4): terrain collision skip — completes correlation but does not advance checkpoint.
                "blockPlaceSkipped" => new BlockPlaceSkippedEvent(
                    X: GetInt(root, "x"), Y: GetInt(root, "y"), Z: GetInt(root, "z"),
                    Block: GetString(root, "block") ?? "?",
                    ExistingBlock: GetString(root, "existingBlock") ?? "?",
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

                // Sprint 35 P0-C: now parses SearchedRadius so BuildGoalDecomposer
                // can distinguish "searched small radius" from "searched maximum radius".
                "flatAreaFound" => new FlatAreaFoundEvent(
                    X: GetInt(root, "x"),
                    Y: GetInt(root, "y"),
                    Z: GetInt(root, "z"),
                    Area: GetInt(root, "area"),
                    MinX: GetInt(root, "minX"),
                    MaxX: GetInt(root, "maxX"),
                    MinZ: GetInt(root, "minZ"),
                    MaxZ: GetInt(root, "maxZ"),
                    SearchedRadius: GetInt(root, "searchedRadius", 32),
                    Timestamp: now),

                // Sprint 40 P0-C: mine action aborted by stop signal.
                "mineAborted" => new MineAbortedEvent(
                    Block: GetString(root, "block") ?? "unknown",
                    Mined: GetInt(root, "mined"),
                    TargetCount: GetInt(root, "targetCount", 0),
                    BlockPosition: root.TryGetProperty("blockX", out _)
                        ? new Position(
                            GetInt(root, "blockX"),
                            GetInt(root, "blockY"),
                            GetInt(root, "blockZ"))
                        : null,
                    Timestamp: now),

                // Sprint 40 P0-C: emergency stop acknowledged by the adapter.
                "stopComplete" => new StopCompleteEvent(Timestamp: now),

                // Sprint 40 P0-B: reachable block query result.
                "reachableBlockFound" => new ReachableBlockFoundEvent(
                    Block: GetString(root, "block") ?? "unknown",
                    X: GetInt(root, "x"),
                    Y: GetInt(root, "y"),
                    Z: GetInt(root, "z"),
                    EuclideanDistance: GetDouble(root, "euclideanDistance", 0),
                    PathDistance: GetInt(root, "pathDistance", 0),
                    Timestamp: now),

                // Sprint 55 (TSK-0165): action lifecycle telemetry.
                "actionStarted" => new ActionStartedEvent(
                    Action: GetString(root, "action") ?? "?",
                    CorrelationId: GetString(root, "correlationId"),
                    Timestamp: now),

                "actionProgress" => new ActionProgressEvent(
                    Action: GetString(root, "action") ?? "?",
                    Completed: GetInt(root, "mined", GetInt(root, "completed")),
                    TargetCount: GetInt(root, "targetCount", 1),
                    PercentComplete: GetInt(root, "percentComplete"),
                    CorrelationId: GetString(root, "correlationId"),
                    Timestamp: now),

                // Sprint 55 (TSK-0165): action lifecycle telemetry — terminal states.
                "actionFailed" => new ActionFailedEvent(
                    Action: GetString(root, "action") ?? "?",
                    ReasonCode: GetString(root, "reasonCode") ?? "unknown_error",
                    Detail: GetString(root, "message") ?? GetString(root, "detail") ?? "",
                    CorrelationId: GetString(root, "correlationId"),
                    Timestamp: now),

                "actionCompleted" => new ActionCompletedEvent(
                    Action: GetString(root, "action") ?? "?",
                    CorrelationId: GetString(root, "correlationId"),
                    Timestamp: now),

                // Sprint 55 Wave B: environment query results.
                "blocksQueried" => ParseBlocksQueried(root, now),

                "entitiesQueried" => ParseEntitiesQueried(root, now),

                "entityObserved" => ParseEntityObserved(root, now),

                _ => null, // unknown event type — ignored
            };
        }
        catch (Exception ex)
        {
            instanceLogger.LogWarning(ex, "WebSocketBridge.ParseEvent: {Message}", ex.Message);
            return null;
        }
    }

    private static StatusEvent ParseStatus(JsonElement root, DateTimeOffset now, ILogger<WebSocketBridge> instanceLogger)
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
                catch (Exception ex) { instanceLogger.LogWarning(ex, "WebSocketBridge.ParseEvent malformed inventory: {Message}", ex.Message); }
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
            GameMode: GetString(root, "gameMode"),
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

    /// <summary>
    /// Sprint 41: returns null when the key is missing or not a number (unlike
    /// <see cref="GetInt"/> which returns a default). Used for optional fields
    /// like error event position coordinates.
    /// </summary>
    private static int? GetIntOrNull(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var el)) return null;
        return el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i)
            ? i : null;
    }

    private static double GetDouble(JsonElement root, string key, double defaultValue = 0)
    {
        if (!root.TryGetProperty(key, out var el)) return defaultValue;
        return el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d)
            ? d : defaultValue;
    }

    /// <summary>
    /// TSK-0128: Extracts an optional Guid from a JSON string property.
    /// Returns null when the key is missing, not a string, or not a valid Guid.
    /// </summary>
    private static Guid? TryGetGuid(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var el)) return null;
        if (el.ValueKind != JsonValueKind.String) return null;
        var s = el.GetString();
        return Guid.TryParse(s, out var g) ? g : null;
    }

    // ── Sprint 55 Wave B: Environment query parsers ─────────────────────────

    private static BlocksQueriedEvent ParseBlocksQueried(JsonElement root, DateTimeOffset now)
    {
        var blocks = new List<QueriedBlock>();
        if (root.TryGetProperty("blocks", out var blocksArr) && blocksArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var b in blocksArr.EnumerateArray())
            {
                blocks.Add(new QueriedBlock(
                    X: GetInt(b, "x"),
                    Y: GetInt(b, "y"),
                    Z: GetInt(b, "z"),
                    Name: GetString(b, "name") ?? "unknown",
                    Type: GetInt(b, "type")));
            }
        }

        return new BlocksQueriedEvent(
            Blocks: blocks,
            From: new Position(GetInt(root, "fromX"), GetInt(root, "fromY"), GetInt(root, "fromZ")),
            To: new Position(GetInt(root, "toX"), GetInt(root, "toY"), GetInt(root, "toZ")),
            CorrelationId: GetString(root, "correlationId"),
            Timestamp: now);
    }

    private static EntitiesQueriedEvent ParseEntitiesQueried(JsonElement root, DateTimeOffset now)
    {
        var entities = ParseObservedEntities(root);
        return new EntitiesQueriedEvent(
            Entities: entities,
            Radius: GetInt(root, "radius"),
            EntityTypeFilter: GetString(root, "entityType"),
            BotPosition: new Position(GetInt(root, "botX"), GetInt(root, "botY"), GetInt(root, "botZ")),
            CorrelationId: GetString(root, "correlationId"),
            Timestamp: now);
    }

    private static EntityObservedEvent ParseEntityObserved(JsonElement root, DateTimeOffset now)
    {
        var entities = ParseObservedEntities(root);
        return new EntityObservedEvent(
            Entities: entities,
            BotPosition: new Position(GetInt(root, "botX"), GetInt(root, "botY"), GetInt(root, "botZ")),
            Timestamp: now);
    }

    private static List<ObservedEntity> ParseObservedEntities(JsonElement root)
    {
        var entities = new List<ObservedEntity>();
        if (root.TryGetProperty("entities", out var entitiesArr) && entitiesArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in entitiesArr.EnumerateArray())
            {
                entities.Add(new ObservedEntity(
                    Name: GetString(e, "name") ?? "unknown",
                    Type: GetString(e, "type") ?? "mob",
                    Hostile: e.TryGetProperty("hostile", out var hostileEl) && hostileEl.GetBoolean(),
                    X: GetInt(e, "x"),
                    Y: GetInt(e, "y"),
                    Z: GetInt(e, "z"),
                    Distance: GetDouble(e, "distance"),
                    Health: GetIntOrNull(e, "health"),
                    Username: GetString(e, "username")));
            }
        }
        return entities;
    }

    // ── Dispose ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _receiveCts?.Cancel();
        _receiveCts?.Dispose();
        _ws?.Dispose();
    }
}
