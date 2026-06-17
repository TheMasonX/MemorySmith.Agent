namespace WebUI.Blazor;

using Agent.Core;
using Agent.Planning;
using Agent.Tools;
using Microsoft.AspNetCore.SignalR;
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
    IHubContext<AgentHub>? hubContext = null,
    GoalFactory? goalFactory = null,
    IChatInterpreter? chatInterpreter = null,
    string botName = "AgentBot",
    int maxConsecutiveFailures = 3,
    IAgentJournal? journal = null,
    TimeSpan[]? reconnectDelays = null) : BackgroundService
{
    // Sprint 1b: default exponential backoff delays
    private static readonly TimeSpan[] DefaultReconnectDelays =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8),
         TimeSpan.FromSeconds(16), TimeSpan.FromSeconds(32)];

    /// <summary>Per-action timeout in seconds. 0 = no timeout.</summary>
    private const int DefaultActionTimeoutSeconds = 30;

    private readonly TimeSpan[] _reconnectDelays = reconnectDelays ?? DefaultReconnectDelays;

    private readonly ActionQueue _queue = new();
    private readonly WorldStateProjector _projector = new();
    private readonly IAgentJournal? _journal = journal;

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
    private FailureReason? _lastFailureReason;
    private bool _actionDispatchedThisCycle;
    // Sprint 4a: connection status for SignalR push; D2: "reconnecting" broadcast
    private string _connectionStatus = "disconnected";

    public WorldState WorldState           => _worldState;
    public IGoal?     CurrentGoal          => _currentGoal;
    public int        ConsecutiveFailures  => _consecutiveFailures;

    // ── Public control API ────────────────────────────────────────────────────

    public void Enqueue(ActionData action) => _queue.Enqueue(action);

    public void SetGoal(IGoal goal)
    {
        _currentGoal = goal;
        _currentGoal.FailureReason = null;
        _queue.Clear();
        _consecutiveFailures = 0;
        _lastFailureReason = null;
        lock (_pendingLock) _pendingActions.Clear();
        logger.LogInformation("Goal set: {Goal}", goal.Name);
        _journal?.Log(new JournalEntry(
            DateTimeOffset.UtcNow, JournalEntryType.GoalSet, goal.Name,
            new Dictionary<string, object?> { ["description"] = goal.Description }));
        // Sprint 4b: push goal change to dashboard
        _ = PushGoalToDashboardAsync();
    }

    public void CancelGoal()
    {
        if (_currentGoal is not null)
            logger.LogInformation("Goal cancelled: {Goal}", _currentGoal.Name);
        var previousGoalName = _currentGoal?.Name;
        _currentGoal = null;
        _journal?.Log(new JournalEntry(
            DateTimeOffset.UtcNow, JournalEntryType.GoalCancel, previousGoalName ?? "unknown"));
        _consecutiveFailures = 0;
        _queue.Clear();
        lock (_pendingLock) _pendingActions.Clear();
        // Sprint 4b: push goal clear to dashboard
        _ = PushGoalToDashboardAsync();
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
    /// Max 5 attempts (1 initial + 4 retries per DefaultReconnectDelays length).
    /// World state and current goal survive reconnects — only the transport is re-opened.
    /// Sprint 4a / D2: broadcasts "reconnecting" status to dashboard during backoff.
    /// </summary>
    private const int MaxConnectionAttempts = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (int attempt = 0; attempt < MaxConnectionAttempts; attempt++)
        {
            if (stoppingToken.IsCancellationRequested) return;

            if (attempt > 0)
            {
                var delay = attempt - 1 < _reconnectDelays.Length
                    ? _reconnectDelays[attempt - 1] : TimeSpan.FromSeconds(64);
                _connectionStatus = "reconnecting";
                _ = PushStatusToDashboardAsync(stoppingToken);
                logger.LogWarning("Reconnecting (attempt {N}/{Max}) in {Delay}ms...",
                    attempt + 1, MaxConnectionAttempts, (int)delay.TotalMilliseconds);
                try { await Task.Delay(delay, stoppingToken); }
                catch (OperationCanceledException) { return; }
            }

            logger.LogInformation("AgentBackgroundService starting (attempt {N})...", attempt + 1);

            using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            try
            {
                await worldAdapter.ConnectAsync(connectionCts.Token);
                _connectionStatus = "connected";
                logger.LogInformation("World adapter connected.");
                _journal?.Log(new JournalEntry(
                    DateTimeOffset.UtcNow, JournalEntryType.AgentStarted, "Agent connected"));

                await Task.WhenAll(
                    MonitorAndCancelOnFaultAsync(ProcessEventsAsync(connectionCts.Token), connectionCts),
                    DispatchActionsAsync(connectionCts.Token),
                    ChatConsumerAsync(connectionCts.Token));

                return; // clean exit
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return; // external stop — don't retry
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Connection attempt {N} failed.", attempt + 1);
            }
            finally
            {
                connectionCts.Cancel();
                try { await worldAdapter.DisconnectAsync(CancellationToken.None); }
                catch { /* best-effort cleanup */ }
            }
        }

        _connectionStatus = "disconnected";
        _ = PushStatusToDashboardAsync(stoppingToken);
        _journal?.Log(new JournalEntry(
            DateTimeOffset.UtcNow, JournalEntryType.AgentStopped,
            $"Max reconnect attempts ({MaxConnectionAttempts}) exhausted"));
        logger.LogError("AgentBackgroundService: max reconnect attempts ({Max}) exhausted. Shutting down.",
            MaxConnectionAttempts);
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
            logger.LogDebug("World event: {Type}", worldEvent.GetType().Name);
            _worldState = _projector.Apply(_worldState, worldEvent);

            switch (worldEvent)
            {
                case SpawnEvent:
                    logger.LogInformation("Bot spawned at {Pos}", _worldState.Position);
                    break;

                case BlockMinedEvent e:
                    var itemKey = e.Block.Contains(':') ? e.Block.Split(':')[1] : e.Block;
                    logger.LogInformation("Inventory +1 {Block} → total {Total}",
                        itemKey, _worldState.Inventory.GetValueOrDefault(itemKey));
                    break;

                case CraftCompleteEvent e:
                    logger.LogInformation("Crafted {Count}x {Item}", e.Count, e.Item);
                    break;

                case SmeltCompleteEvent e:
                    logger.LogInformation("Smelted {Count}x {Input} → {Output}", e.Count, e.Input, e.Result);
                    break;

                case ChatEvent:
                    // Sprint 1a: offload to ChatConsumerAsync — LLM call never blocks event loop
                    _chatChannel.Writer.TryWrite(worldEvent);
                    break;

                default:
                    TryRouteAsError(worldEvent);
                    break;
            }

            // If world events satisfy the active goal, stop immediately instead of
            // draining stale queued actions (prevents post-completion over-collection).
            TryCompleteCurrentGoalFromWorldUpdate();

            // Sprint 4a: push status to dashboard after every event tick (fire-and-forget)
            _ = PushStatusToDashboardAsync(CancellationToken.None);
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
        if (worldEvent is not ChatEvent chat) return;

        logger.LogInformation("[chat] <{Username}> {Message}", chat.Username, chat.Message);

        // Sprint 4b: push player message to dashboard immediately
        _ = PushChatToDashboardAsync("player", chat.Username, chat.Message);

        var interpretation = await chatInterpreter.InterpretAsync(
            chat.Username, chat.Message, botName, chat.OnlinePlayers,
            _worldState.Position, chat.PlayerPos, _worldState, ct);

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
            // Sprint 4b: push bot response to dashboard
            _ = PushChatToDashboardAsync("bot", botName, interpretation.Response);
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

            case ChatIntentType.NavigateTo:
                // Player-issued navigation commands should interrupt gather loops.
                CancelGoal();

                if (interpretation.GoalName == "MoveTo"
                    && interpretation.GoalParameters is { } nav
                    && nav.TryGetValue("x", out var nx) && nav.TryGetValue("y", out var ny)
                    && nav.TryGetValue("z", out var nz))
                {
                    _queue.Enqueue(new ActionData
                    {
                        Tool = "MoveTo",
                        Arguments = { ["x"] = nx, ["y"] = ny, ["z"] = nz }
                    });
                }
                else if (interpretation.GoalParameters is { } follow
                    && follow.TryGetValue("target", out var target)
                    && string.Equals(target?.ToString(), "player", StringComparison.OrdinalIgnoreCase)
                    && chat.PlayerPos is { } playerPos)
                {
                    _queue.Enqueue(new ActionData
                    {
                        Tool = "MoveTo",
                        Arguments =
                        {
                            ["x"] = playerPos.X,
                            ["y"] = playerPos.Y,
                            ["z"] = playerPos.Z,
                        }
                    });
                }
                break;
        }
    }

    private void TryCompleteCurrentGoalFromWorldUpdate()
    {
        if (_currentGoal is null) return;
        if (!_currentGoal.IsComplete(_worldState)) return;

        logger.LogInformation("Goal '{Goal}' completed.", _currentGoal.Name);
        _currentGoal = null;
        _consecutiveFailures = 0;
        _lastFailureReason = null;
        _queue.Clear();
        lock (_pendingLock) _pendingActions.Clear();
        _ = PushGoalToDashboardAsync();
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
        if (worldEvent is BlockNotFoundEvent bnf && bnf.MinedCount == 0)
        {
            errMsg = $"blockNotFound:{bnf.Block}";
            logger.LogWarning("No {Block} found in range — will count as failure.", bnf.Block);
        }
        else if (worldEvent is ErrorEvent err)
        {
            errMsg = $"{err.Action}:{err.Message}";
            logger.LogWarning("Game error [{Action}]: {Message}", err.Action, err.Message);
        }
        if (errMsg is not null) _gameErrors.Writer.TryWrite(errMsg);
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
                    var reason = _currentGoal.HasFailed(_worldState)
                        ? _lastFailureReason?.ToString() ?? FailureReason.Unknown.ToString()
                        : (_lastFailureReason ?? FailureReason.ConsecutiveFailures).ToString();
                    _currentGoal.FailureReason = reason;
                    _journal?.Log(new JournalEntry(
                        DateTimeOffset.UtcNow, JournalEntryType.ReplanTriggered, _currentGoal.Name));
                    logger.LogWarning("Goal '{Goal}' failed (failures={N}, reason={Reason}).",
                        _currentGoal.Name, _consecutiveFailures, reason);
                    _currentGoal = null; _consecutiveFailures = 0; _lastFailureReason = null;
                    planContext.Clear();
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
                    _journal?.Log(new JournalEntry(
                        DateTimeOffset.UtcNow, JournalEntryType.PlanCreated, _currentGoal.Name,
                        new Dictionary<string, object?> { ["actionCount"] = plan.Actions.Count }));
                }
                catch (Exception ex)
                {
                    if (_currentGoal is not null)
                        _currentGoal.FailureReason = FailureReason.NoValidActions.ToString();
                    logger.LogWarning(ex, "Planning failed for goal '{Goal}'.", _currentGoal?.Name);
                    _currentGoal = null;
                    _lastFailureReason = null;
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

                    using var timeoutCts = new CancellationTokenSource(
                        TimeSpan.FromSeconds(DefaultActionTimeoutSeconds));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                        ct, timeoutCts.Token);

                    _journal?.Log(new JournalEntry(
                        DateTimeOffset.UtcNow, JournalEntryType.ActionDispatched, action.Tool));
                    var result = await toolCaller.CallAsync(
                        action.Tool, doc.RootElement, linkedCts.Token);
                    if (result.Success)
                    {
                        logger.LogDebug("Tool {Tool}: {Message}", action.Tool, result.Message);
                        _journal?.Log(new JournalEntry(
                            DateTimeOffset.UtcNow, JournalEntryType.ActionCompleted, action.Tool));
                        // Reset failure streak only on actions that indicate meaningful progress.
                        if (IsProgressSignalTool(action.Tool))
                        {
                            _consecutiveFailures = 0;
                            _lastFailureReason = null;
                        }
                        if (result.Data is not null)
                            foreach (var kv in result.Data)
                                planContext[kv.Key] = kv.Value;
                    }
                    else
                    {
                        logger.LogWarning("Tool {Tool} failed: {Message}", action.Tool, result.Message);
                        _journal?.Log(new JournalEntry(
                            DateTimeOffset.UtcNow, JournalEntryType.ActionFailed, action.Tool,
                            new Dictionary<string, object?> { ["error"] = result.Message ?? "" }));
                        _consecutiveFailures++;
                        _lastFailureReason ??= MapErrorToFailureReason(result.Message);
                    }
                }
                catch (OperationCanceledException oce)
                    when (!ct.IsCancellationRequested)
                {
                    // Only the per-action timeout fired — parent loop is still alive
                    logger.LogWarning(oce,
                        "Tool {Tool} timed out after {Seconds}s (failure {N}/{Max})",
                        action.Tool, DefaultActionTimeoutSeconds,
                        _consecutiveFailures + 1, maxConsecutiveFailures);
                    _journal?.Log(new JournalEntry(
                        DateTimeOffset.UtcNow, JournalEntryType.ActionFailed, action.Tool,
                        new Dictionary<string, object?> { ["error"] = "timed out" }));
                    _consecutiveFailures++;
                    _lastFailureReason = FailureReason.ToolTimeout;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Exception dispatching tool {Tool}", action.Tool);
                    _journal?.Log(new JournalEntry(
                        DateTimeOffset.UtcNow, JournalEntryType.ActionFailed, action.Tool,
                        new Dictionary<string, object?> { ["error"] = ex.Message }));
                    _consecutiveFailures++;
                    _lastFailureReason ??= FailureReason.Unknown;
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
                        _lastFailureReason ??= MapErrorToFailureReason(errMsg);
                        logger.LogWarning("Game error after cycle (failures={N}/{Max}): {Error}",
                            _consecutiveFailures, maxFailures, errMsg);

                        // Recovery seam: for repeated game errors, ask the LLM interpreter
                        // for an alternate action/goal when available.
                        if (_consecutiveFailures >= 2)
                            await TryRecoverFromGameErrorAsync(errMsg, ct);
                    }
                }
                else
                    await Task.Delay(50, ct);
            }
        }
    }

    // ── SignalR dashboard push — Sprint 4a ─────────────────────────────────

    /// <summary>
    /// Fires a status snapshot to all connected dashboard clients. Never throws —
    /// a disconnected SignalR hub is a best-effort path, not a fatal error.
    /// </summary>
    private async Task PushStatusToDashboardAsync(CancellationToken ct)
    {
        if (hubContext is null) return;
        try
        {
            var inv = _worldState.Inventory ?? new Dictionary<string, int>();
            var update = new AgentStatusUpdate(
                Status: _connectionStatus == "reconnecting" ? "reconnecting"
                    : _currentGoal is not null ? "active" : "idle",
                Goal: _currentGoal?.Name,
                GoalDescription: _currentGoal?.Description,
                Health: _worldState.Health,
                Food: _worldState.Food,
                X: _worldState.Position.X,
                Y: _worldState.Position.Y,
                Z: _worldState.Position.Z,
                QueuedActions: _queue.Count,
                ConsecutiveFailures: _consecutiveFailures,
                Inventory: inv
            );
            await hubContext.Clients.Group("dashboard").SendAsync("StatusUpdated", update, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "SignalR status push failed (best-effort).");
        }
    }

    // Sprint 4b: push chat messages and goal changes to dashboard via SignalR

    private async Task PushChatToDashboardAsync(string type, string? who, string text)
    {
        if (hubContext is null) return;
        try
        {
            await hubContext.Clients.Group("dashboard")
                .SendAsync("ChatMessage", new { type, who, text });
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "SignalR chat push failed (best-effort).");
        }
    }

    private async Task PushGoalToDashboardAsync()
    {
        if (hubContext is null) return;
        try
        {
            await hubContext.Clients.Group("dashboard").SendAsync("GoalUpdate", new
            {
                goal = _currentGoal?.Name,
                description = _currentGoal?.Description
            });
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "SignalR goal push failed (best-effort).");
        }
    }

    // ── Failure reason mapping ─────────────────────────────────────────────

    /// <summary>
    /// Maps an error string (from a tool result or game event) to a
    /// <see cref="FailureReason"/>. Uses prefix matching so that messages like
    /// "blockNotFound:oak_log" map to <see cref="FailureReason.TargetUnreachable"/>.
    /// </summary>
    private static FailureReason MapErrorToFailureReason(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return FailureReason.Unknown;

        if (error.StartsWith("blockNotFound:", StringComparison.OrdinalIgnoreCase))
            return FailureReason.TargetUnreachable;

        if (error.Contains("inventory full", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("inventory is full", StringComparison.OrdinalIgnoreCase))
            return FailureReason.InventoryFull;

        if (error.Contains("recipe", StringComparison.OrdinalIgnoreCase) &&
            (error.Contains("missing", StringComparison.OrdinalIgnoreCase) ||
             error.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
             error.Contains("not found", StringComparison.OrdinalIgnoreCase)))
            return FailureReason.RecipeMissing;

        if (error.Contains("unreachable", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("cannot reach", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("obstructed", StringComparison.OrdinalIgnoreCase))
            return FailureReason.TargetUnreachable;

        if (error.Contains("found within", StringComparison.OrdinalIgnoreCase) &&
            error.Contains("No ", StringComparison.OrdinalIgnoreCase))
            return FailureReason.TargetUnreachable;

        return FailureReason.Unknown;
    }

    private static bool IsProgressSignalTool(string toolName) =>
        toolName.Equals("MineBlock", StringComparison.OrdinalIgnoreCase)
        || toolName.Equals("MoveTo", StringComparison.OrdinalIgnoreCase)
        || toolName.Equals("PlaceBlock", StringComparison.OrdinalIgnoreCase)
        || toolName.Equals("CraftItem", StringComparison.OrdinalIgnoreCase)
        || toolName.Equals("SmeltItem", StringComparison.OrdinalIgnoreCase)
        || toolName.Equals("FindFlatArea", StringComparison.OrdinalIgnoreCase)
        || toolName.Equals("Wander", StringComparison.OrdinalIgnoreCase);

    private async Task TryRecoverFromGameErrorAsync(string errMsg, CancellationToken ct)
    {
        if (chatInterpreter is null || _currentGoal is null) return;

        try
        {
            var prompt =
                $"recover from runtime error while executing goal {_currentGoal.Name}: {errMsg}. " +
                "If gather target is unavailable, choose an alternative gather goal or navigate action.";

            var interpretation = await chatInterpreter.InterpretAsync(
                username: "system",
                message: prompt,
                botName: botName,
                onlinePlayers: 1,
                botPosition: _worldState.Position,
                playerPosition: _worldState.Position,
                state: _worldState,
                ct: ct);

            if (interpretation.IntentType == ChatIntentType.CreateGoal && interpretation.GoalName is not null)
            {
                logger.LogInformation("Recovery interpreter suggested goal: {Goal}", interpretation.GoalName);
                await TryCreateGoalFromChatAsync(interpretation, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Recovery interpreter failed for error: {Error}", errMsg);
        }
    }
}
