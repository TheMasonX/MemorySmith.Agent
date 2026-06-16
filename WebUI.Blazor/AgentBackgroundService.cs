namespace WebUI.Blazor;

using Agent.Core;
using Agent.Planning;
using Agent.Tools;
using System.Text.Json;
using System.Threading.Channels;

/// <summary>
/// Hosted service that owns the agent loop.
///
/// Phase 1: basic loop.
/// Phase 3: IPlanner integration.
/// Phase 5: chat event routing via <see cref="IChatInterpreter"/> (includes LLM path).
/// Phase 5b: LlmChatInterpreter; player-position-aware chat routing; CancelGoal,
///           SetBuildOrigin, GetPendingActions public API.
/// Phase 6 Sprint 1:
///   1a — Non-blocking LLM: chat events are written to <see cref="_chatChannel"/> and
///        processed by <see cref="ChatConsumerAsync"/> on a dedicated task, so a 5–10s
///        LLM call never stalls health/death/blockMined processing.
///   1b — Reconnect: <see cref="ExecuteAsync"/> retries ConnectAsync with exponential
///        backoff (default 2s/4s/8s/16s/32s, max 5 retries). World state and current
///        goal survive reconnects. Configurable delays for test speed.
/// </summary>
public sealed class AgentBackgroundService(
    IWorldAdapter worldAdapter,
    IToolCaller toolCaller,
    ILogger<AgentBackgroundService> logger,
    IPlanner planner,
    GoalFactory? goalFactory = null,
    IChatInterpreter? chatInterpreter = null,
    string botName = "AgentBot",
    int maxConsecutiveFailures = 3,
    TimeSpan[]? reconnectDelays = null) : BackgroundService
{
    // Sprint 1b: default exponential backoff delays
    private static readonly TimeSpan[] DefaultReconnectDelays =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8),
         TimeSpan.FromSeconds(16), TimeSpan.FromSeconds(32)];

    private readonly TimeSpan[] _reconnectDelays = reconnectDelays ?? DefaultReconnectDelays;

    private readonly ActionQueue _queue = new();
    private readonly WorldStateProjector _projector = new();

    private readonly Channel<string> _gameErrors =
        Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleWriter = true });

    // Sprint 1a: chat events written here by ProcessEventsAsync; ChatConsumerAsync reads them
    private readonly Channel<WorldEvent> _chatChannel =
        Channel.CreateUnbounded<WorldEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private readonly List<ActionData> _pendingActions = [];
    private readonly object _pendingLock = new();

    private WorldState _worldState = new();
    private IGoal? _currentGoal;
    private int _consecutiveFailures;
    private bool _actionDispatchedThisCycle;

    public WorldState WorldState           => _worldState;
    public IGoal?     CurrentGoal          => _currentGoal;
    public int        ConsecutiveFailures  => _consecutiveFailures;

    // ── Public control API ────────────────────────────────────────────────────

    public void Enqueue(ActionData action) => _queue.Enqueue(action);

    public void SetGoal(IGoal goal)
    {
        _currentGoal = goal;
        _queue.Clear();
        _consecutiveFailures = 0;
        lock (_pendingLock) _pendingActions.Clear();
        logger.LogInformation("Goal set: {Goal}", goal.Name);
    }

    public void CancelGoal()
    {
        if (_currentGoal is not null)
            logger.LogInformation("Goal cancelled: {Goal}", _currentGoal.Name);
        _currentGoal = null;
        _consecutiveFailures = 0;
        _queue.Clear();
        lock (_pendingLock) _pendingActions.Clear();
    }

    public void SetBuildOrigin(string blueprintId, int x, int y, int z)
    {
        _worldState = _worldState.With(b =>
        {
            b.SetFact($"build:{blueprintId}:origin:x", x);
            b.SetFact($"build:{blueprintId}:origin:y", y);
            b.SetFact($"build:{blueprintId}:origin:z", z);
        });
        logger.LogInformation("Build origin set for '{Blueprint}': ({X},{Y},{Z})", blueprintId, x, y, z);
    }

    public IReadOnlyList<ActionData> GetPendingActions()
    {
        lock (_pendingLock)
            return [.. _pendingActions];
    }

    // ── BackgroundService ─────────────────────────────────────────────────────

    /// <summary>
    /// Sprint 1b: Retry loop with exponential backoff.
    /// World state and current goal survive reconnects — only the transport is re-opened.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // attempts = delays.Length + 1 (first attempt + one per delay)
        for (int attempt = 0; attempt <= _reconnectDelays.Length; attempt++)
        {
            if (stoppingToken.IsCancellationRequested) return;

            if (attempt > 0)
            {
                var delay = _reconnectDelays[attempt - 1];
                logger.LogWarning("Reconnecting (attempt {N}/{Max}) in {Delay}ms...",
                    attempt + 1, _reconnectDelays.Length + 1, (int)delay.TotalMilliseconds);
                try { await Task.Delay(delay, stoppingToken); }
                catch (OperationCanceledException) { return; }
            }

            logger.LogInformation("AgentBackgroundService starting (attempt {N})...", attempt + 1);

            // Per-connection CTS: cancelled when ProcessEventsAsync faults or stoppingToken fires
            using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            try
            {
                await worldAdapter.ConnectAsync(connectionCts.Token);
                logger.LogInformation("World adapter connected.");

                await Task.WhenAll(
                    // MonitorAndCancelOnFault ensures ProcessEventsAsync fault cancels siblings
                    MonitorAndCancelOnFaultAsync(ProcessEventsAsync(connectionCts.Token), connectionCts),
                    DispatchActionsAsync(connectionCts.Token),
                    ChatConsumerAsync(connectionCts.Token));

                return; // clean exit (stoppingToken cancelled all tasks gracefully)
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return; // external stop — don't retry
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Connection attempt {N} failed.", attempt + 1);
                // fall through to next retry iteration
            }
            finally
            {
                connectionCts.Cancel(); // stop any tasks still running for this attempt
                try { await worldAdapter.DisconnectAsync(CancellationToken.None); }
                catch { /* best-effort cleanup */ }
            }
        }

        logger.LogError("AgentBackgroundService: max reconnect attempts ({Max}) exhausted. Shutting down.",
            _reconnectDelays.Length + 1);
    }

    /// <summary>
    /// Awaits the task. If it faults (not OCE), cancels the CTS so sibling tasks stop.
    /// Normal completion also cancels siblings (ProcessEventsAsync should not return normally
    /// unless the adapter stream ends — treat that as a reconnect trigger too).
    /// </summary>
    private static async Task MonitorAndCancelOnFaultAsync(Task task, CancellationTokenSource cts)
    {
        try { await task; }
        catch (OperationCanceledException) { throw; }
        catch { cts.Cancel(); throw; }
        cts.Cancel(); // stream ended normally — trigger reconnect via sibling cancellation
    }

    // ── Event processing ──────────────────────────────────────────────────────

    private async Task ProcessEventsAsync(CancellationToken ct)
    {
        await foreach (var worldEvent in worldAdapter.ReceiveEventsAsync(ct))
        {
            logger.LogDebug("World event: {Type}", worldEvent.EventType);
            _worldState = _projector.Apply(_worldState, worldEvent);

            switch (worldEvent.EventType)
            {
                case "spawn":
                    logger.LogInformation("Bot spawned at {Pos}", _worldState.Position);
                    break;

                case "blockMined":
                    if (worldEvent.Payload.TryGetValue("block", out var rawBlock) && rawBlock is string blockName)
                    {
                        var itemKey = blockName.Contains(':') ? blockName.Split(':')[1] : blockName;
                        logger.LogInformation("Inventory +1 {Block} → total {Total}",
                            itemKey, _worldState.Inventory.GetValueOrDefault(itemKey));
                    }
                    break;

                case "craftComplete":
                    if (worldEvent.Payload.TryGetValue("item",  out var ci) && ci  is string ciStr &&
                        worldEvent.Payload.TryGetValue("count", out var cc) && cc is int    ccInt)
                        logger.LogInformation("Crafted {Count}x {Item}", ccInt, ciStr);
                    break;

                case "smeltComplete":
                    if (worldEvent.Payload.TryGetValue("item",   out var si) && si is string siStr &&
                        worldEvent.Payload.TryGetValue("result", out var sr) && sr is string srStr &&
                        worldEvent.Payload.TryGetValue("count",  out var sc) && sc is int    scInt)
                        logger.LogInformation("Smelted {Count}x {Input} → {Output}", scInt, siStr, srStr);
                    break;

                case "chat":
                    // Sprint 1a: offload to ChatConsumerAsync — LLM call never blocks event loop
                    _chatChannel.Writer.TryWrite(worldEvent);
                    break;

                default:
                    TryRouteAsError(worldEvent);
                    break;
            }
        }
    }

    // ── Chat consumer — Sprint 1a ─────────────────────────────────────────────

    /// <summary>
    /// Dedicated consumer for chat events. Runs independently of ProcessEventsAsync so
    /// a 5–10s LLM call in HandleChatEventAsync never delays health/blockMined events.
    /// </summary>
    private async Task ChatConsumerAsync(CancellationToken ct)
    {
        await foreach (var chatEvent in _chatChannel.Reader.ReadAllAsync(ct))
        {
            try
            {
                await HandleChatEventAsync(chatEvent, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing chat event in consumer.");
            }
        }
    }

    private async Task HandleChatEventAsync(WorldEvent worldEvent, CancellationToken ct)
    {
        if (chatInterpreter is null) return;

        if (!worldEvent.Payload.TryGetValue("username", out var uObj) || uObj is not string username) return;
        if (!worldEvent.Payload.TryGetValue("message",  out var mObj) || mObj is not string message)  return;

        var onlinePlayers = worldEvent.Payload.TryGetValue("onlinePlayers", out var opObj) && opObj is int op
            ? op : 1;

        logger.LogInformation("[chat] <{Username}> {Message}", username, message);

        var playerPos = ExtractPlayerPosition(worldEvent);

        var interpretation = await chatInterpreter.InterpretAsync(
            username, message, botName, onlinePlayers,
            _worldState.Position, playerPos, _worldState, ct);

        if (interpretation.IntentType == ChatIntentType.NotAddressed)
            return;

        if (!string.IsNullOrEmpty(interpretation.Response))
        {
            _queue.Enqueue(new ActionData
            {
                Tool      = "Chat",
                Arguments = { ["message"] = interpretation.Response }
            });
            chatInterpreter.RecordBotSpoke();
        }

        switch (interpretation.IntentType)
        {
            case ChatIntentType.CancelGoal:
                CancelGoal();
                break;

            case ChatIntentType.QueryStatus:
                _worldState = _worldState.With(b =>
                    b.SetFact("currentGoal", _currentGoal?.Name ?? "idle"));
                break;

            case ChatIntentType.CreateGoal when interpretation.GoalName is not null:
                await TryCreateGoalFromChatAsync(interpretation, ct);
                break;

            case ChatIntentType.NavigateTo when interpretation.GoalName == "MoveTo":
                if (interpretation.GoalParameters is { } nav &&
                    nav.TryGetValue("x", out var nx) && nav.TryGetValue("y", out var ny) &&
                    nav.TryGetValue("z", out var nz))
                {
                    _queue.Enqueue(new ActionData
                    {
                        Tool = "MoveTo",
                        Arguments = { ["x"] = nx, ["y"] = ny, ["z"] = nz }
                    });
                }
                break;
        }
    }

    private async Task TryCreateGoalFromChatAsync(ChatInterpretation interpretation, CancellationToken ct)
    {
        if (goalFactory is null || interpretation.GoalName is null) return;

        try
        {
            var goal = await goalFactory.CreateAsync(interpretation.GoalName, interpretation.GoalParameters, ct);
            if (goal is not null)
            {
                SetGoal(goal);
                logger.LogInformation("Chat created goal: {Goal}", goal.Name);
            }
            else
            {
                logger.LogWarning("Chat goal '{Name}' could not be created.", interpretation.GoalName);
                _queue.Enqueue(new ActionData
                {
                    Tool = "Chat",
                    Arguments = { ["message"] = "Sorry, I don't know how to do that yet." }
                });
                chatInterpreter?.RecordBotSpoke();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating goal from chat: {Name}", interpretation.GoalName);
        }
    }

    private void TryRouteAsError(WorldEvent worldEvent)
    {
        string? errMsg = null;
        if (worldEvent.EventType == "blockNotFound" &&
            worldEvent.Payload.TryGetValue("mined", out var mined) && mined is int mc && mc == 0 &&
            worldEvent.Payload.TryGetValue("block", out var bn) && bn is string bStr)
        {
            errMsg = $"blockNotFound:{bStr}";
            logger.LogWarning("No {Block} found in range — will count as failure.", bStr);
        }
        else if (worldEvent.EventType == "error")
        {
            var act = worldEvent.Payload.TryGetValue("action",  out var a) && a is string sa ? sa : "?";
            var msg = worldEvent.Payload.TryGetValue("message", out var m) && m is string sm ? sm : "unknown";
            errMsg = $"{act}:{msg}";
            logger.LogWarning("Game error [{Action}]: {Message}", act, msg);
        }
        if (errMsg is not null) _gameErrors.Writer.TryWrite(errMsg);
    }

    private static Position? ExtractPlayerPosition(WorldEvent worldEvent)
    {
        if (worldEvent.Payload.TryGetValue("playerX", out var px) && px is int x &&
            worldEvent.Payload.TryGetValue("playerY", out var py) && py is int y &&
            worldEvent.Payload.TryGetValue("playerZ", out var pz) && pz is int z)
            return new Position(x, y, z);
        return null;
    }

    // ── Action dispatch ────────────────────────────────────────────────────────

    private async Task DispatchActionsAsync(CancellationToken ct)
    {
        var maxFailures = maxConsecutiveFailures;
        var planContext = new Dictionary<string, object?>();

        while (!ct.IsCancellationRequested)
        {
            if (_queue.IsEmpty && _currentGoal is not null && !_actionDispatchedThisCycle)
            {
                if (_currentGoal.IsComplete(_worldState))
                {
                    logger.LogInformation("Goal '{Goal}' completed.", _currentGoal.Name);
                    _currentGoal = null; _consecutiveFailures = 0; planContext.Clear();
                    lock (_pendingLock) _pendingActions.Clear();
                    continue;
                }
                if (_currentGoal.HasFailed(_worldState) || _consecutiveFailures >= maxFailures)
                {
                    logger.LogWarning("Goal '{Goal}' failed (failures={N}).", _currentGoal.Name, _consecutiveFailures);
                    _currentGoal = null; _consecutiveFailures = 0; planContext.Clear();
                    lock (_pendingLock) _pendingActions.Clear();
                    continue;
                }
                try
                {
                    var plan = await planner.PlanAsync(_currentGoal, _worldState, ct);
                    foreach (var planAction in plan.Actions)
                        foreach (var kv in planContext)
                            planAction.Context.TryAdd(kv.Key, kv.Value);
                    _queue.EnqueueAll(plan.Actions);
                    _actionDispatchedThisCycle = false;
                    lock (_pendingLock)
                    {
                        _pendingActions.Clear();
                        _pendingActions.AddRange(plan.Actions);
                    }
                    logger.LogInformation("New plan for '{Goal}': {Count} actions.", _currentGoal.Name, plan.Actions.Count);
                    _consecutiveFailures = 0;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Planning failed for goal '{Goal}'.", _currentGoal?.Name);
                    _currentGoal = null;
                }
            }

            var action = _queue.Dequeue();
            if (action is not null)
            {
                _actionDispatchedThisCycle = true;
                lock (_pendingLock)
                {
                    if (_pendingActions.Count > 0) _pendingActions.RemoveAt(0);
                }
                try
                {
                    var argsJson = JsonSerializer.Serialize(action.Arguments);
                    using var doc = JsonDocument.Parse(argsJson);
                    var result = await toolCaller.CallAsync(action.Tool, doc.RootElement, ct);
                    if (result.Success)
                    {
                        logger.LogDebug("Tool {Tool}: {Message}", action.Tool, result.Message);
                        _consecutiveFailures = 0;
                        if (result.Data is not null)
                            foreach (var kv in result.Data)
                                planContext[kv.Key] = kv.Value;
                    }
                    else
                    {
                        logger.LogWarning("Tool {Tool} failed: {Message}", action.Tool, result.Message);
                        _consecutiveFailures++;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Exception dispatching tool {Tool}", action.Tool);
                    _consecutiveFailures++;
                }
            }
            else
            {
                if (_actionDispatchedThisCycle)
                {
                    logger.LogDebug("Plan cycle complete — settling for 300 ms");
                    await Task.Delay(300, ct);
                    _actionDispatchedThisCycle = false;
                    if (_gameErrors.Reader.TryRead(out var errMsg))
                    {
                        _consecutiveFailures++;
                        logger.LogWarning("Game error after cycle (failures={N}/{Max}): {Error}",
                            _consecutiveFailures, maxFailures, errMsg);
                    }
                }
                else
                    await Task.Delay(50, ct);
            }
        }
    }
}
