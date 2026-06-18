namespace WebUI.Blazor;

using Agent.Core;
using Agent.Planning;
using Agent.Tools;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;
using System.Threading.Channels;

/// <summary>
/// Hosted service that owns the agent loop.
/// (see class header in original for phase notes)
///
/// Sprint 18:
///   - SendEmergencyStop(): dispatches {action:"stop"} to Node.js on goal set/cancel to
///     abort in-progress mine/wander loops (fire-and-forget: resolves "leo stop" not working).
///   - DispatchActionsAsync: MinReplanIntervalSeconds (2s) guard prevents replanning at CPU
///     speed under the fire-and-forget tool architecture (resolves 3x/sec replan storm).
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

    /// <summary>
    /// Minimum flat area (in cells) for a FlatAreaFoundEvent to trigger auto build-origin
    /// assignment. 25 = a 5x5 footprint — the smallest structure the HTN library can build.
    /// </summary>
    private const int MinUsableFlatArea = 25;

    /// <summary>Seconds of inactivity before a stall warning fires.</summary>
    private const int StallWarningSeconds = 10;

    /// <summary>Minimum seconds between consecutive stall warnings for the same goal.</summary>
    private const int StallWarningSuppressSeconds = 30;

    /// <summary>
    /// Minimum seconds between successive replans.
    /// Sprint 18: tools dispatch fire-and-forget to Node.js; without this guard the planner
    /// runs at CPU speed (every 50–300 ms) before Node.js executes any action.
    /// </summary>
    private const int MinReplanIntervalSeconds = 2;

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

    // Sprint 12: tracks the name of the most-recently abandoned goal.
    private string? _lastAbandonedGoalName;

    // Sprint 13: tracks the goal name for which recovery was last attempted.
    private string? _lastRecoveredGoalName;

    // Sprint 15 P1: stall detection — tracks when an action was last dispatched.
    private DateTimeOffset _lastActionDispatchedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastStallWarnedAt       = DateTimeOffset.MinValue;

    // Sprint 18: minimum replan interval guard — companion to MinReplanIntervalSeconds.
    private DateTimeOffset _lastReplanAt = DateTimeOffset.MinValue;

    public WorldState WorldState           => _worldState;
    public IGoal?     CurrentGoal          => _currentGoal;
    public int        ConsecutiveFailures  => _consecutiveFailures;

    // ── Public control API ────────────────────────────────────────────────────

    public void Enqueue(ActionData action) => _queue.Enqueue(action);

    public void SetGoal(IGoal goal)
    {
        // Sprint 18: abort in-progress Node.js action before changing goal.
        // Tools dispatch fire-and-forget; the adapter may still be mining or wandering
        // from a previous plan. The emergency stop clears its queue and breaks the loop.
        SendEmergencyStop();

        _currentGoal = goal;
        _currentGoal.FailureReason = null;
        _queue.Clear();
        _consecutiveFailures = 0;
        _lastFailureReason = null;
        // Sprint 12: reset so DispatchActionsAsync can plan immediately.
        _actionDispatchedThisCycle = false;
        // Sprint 12: clear recovery guard — this is a fresh goal, not a retry.
        _lastAbandonedGoalName = null;
        // Sprint 13: clear recovery-rate guard so recovery can run for the new goal.
        _lastRecoveredGoalName = null;
        // Sprint 15 P1: reset stall clock for the new goal.
        _lastActionDispatchedAt = DateTimeOffset.UtcNow;
        _lastStallWarnedAt      = DateTimeOffset.MinValue;
        // Sprint 18: reset replan timer so the first plan fires without waiting.
        _lastReplanAt = DateTimeOffset.MinValue;
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
        // Sprint 18: abort in-progress Node.js action immediately on cancel.
        SendEmergencyStop();

        if (_currentGoal is not null)
            logger.LogInformation("Goal cancelled: {Goal}", _currentGoal.Name);
        var previousGoalName = _currentGoal?.Name;
        _currentGoal = null;
        _journal?.Log(new JournalEntry(
            DateTimeOffset.UtcNow, JournalEntryType.GoalCancel, previousGoalName ?? "unknown"));
        _consecutiveFailures = 0;
        _lastRecoveredGoalName = null; // Sprint 13: reset recovery rate-limiter
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

    private static async Task MonitorAndCancelOnFaultAsync(Task task, CancellationTokenSource cts)
    {
        try { await task; }
        catch (OperationCanceledException) { throw; }
        catch { cts.Cancel(); throw; }
        cts.Cancel();
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
                    // Sprint 15 P0: log actual count (was always 1 before the projector fix)
                    logger.LogInformation("Inventory +{Count} {Block} -> total {Total}",
                        e.Count, itemKey, _worldState.Inventory.GetValueOrDefault(itemKey));
                    break;

                case CraftCompleteEvent e:
                    logger.LogInformation("Crafted {Count}x {Item}", e.Count, e.Item);
                    break;

                case SmeltCompleteEvent e:
                    logger.LogInformation("Smelted {Count}x {Input} -> {Output}", e.Count, e.Input, e.Result);
                    break;

                case ChatEvent:
                    // Sprint 1a: offload to ChatConsumerAsync — LLM call never blocks event loop
                    _chatChannel.Writer.TryWrite(worldEvent);
                    break;

                case FlatAreaFoundEvent ffa when ffa.Area >= MinUsableFlatArea:
                    SetBuildOrigin(BuildFactKeys.AutoBlueprintId, ffa.X, ffa.Y, ffa.Z);
                    _journal?.Log(new JournalEntry(
                        DateTimeOffset.UtcNow, JournalEntryType.Observation, "FlatAreaFound",
                        new Dictionary<string, object?>
                        {
                            ["x"] = ffa.X, ["y"] = ffa.Y, ["z"] = ffa.Z, ["area"] = ffa.Area,
                        }));
                    logger.LogInformation(
                        "[findFlatArea] auto-set build origin ({X},{Y},{Z}) area={Area}",
                        ffa.X, ffa.Y, ffa.Z, ffa.Area);
                    break;

                case FlatAreaFoundEvent ffa:
                    logger.LogInformation(
                        "[findFlatArea] scan area={Area} below minimum {Min} — auto-origin not updated",
                        ffa.Area, MinUsableFlatArea);
                    break;

                default:
                    TryRouteAsError(worldEvent);
                    break;
            }

            TryCompleteCurrentGoalFromWorldUpdate();
            _ = PushStatusToDashboardAsync(CancellationToken.None);
        }
    }

    // ── Chat consumer — Sprint 1a ─────────────────────────────────────────────

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
        _ = PushChatToDashboardAsync("player", chat.Username, chat.Message);

        using var thinkingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var thinkingTask = EnqueueThinkingIfSlowAsync(thinkingCts.Token);

        var interpretation = await chatInterpreter.InterpretAsync(
            chat.Username, chat.Message, botName, chat.OnlinePlayers,
            _worldState.Position, chat.PlayerPos, _worldState, ct);

        await thinkingCts.CancelAsync();
        try { await thinkingTask.ConfigureAwait(false); } catch (OperationCanceledException) { }

        // Sprint 11: log the resolved intent for visibility
        if (interpretation.IntentType != ChatIntentType.NotAddressed)
            logger.LogInformation("[chat] <{Username}> -> {Intent}{Goal}",
                chat.Username, interpretation.IntentType,
                interpretation.GoalName is null ? "" : $" ({interpretation.GoalName})");

        if (interpretation.IntentType == ChatIntentType.NotAddressed)
        {
            logger.LogDebug("[chat] not-addressed from <{Username}>: '{Snippet}'",
                chat.Username, chat.Message[..Math.Min(40, chat.Message.Length)]);
            return;
        }

        // Sprint 12: IMPORTANT — save the response BEFORE the switch.
        var pendingResponse = !string.IsNullOrEmpty(interpretation.Response)
            ? interpretation.Response : null;

        chatInterpreter.RecordBotSpoke();

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
                CancelGoal();

                if (pendingResponse is not null)
                {
                    _queue.Enqueue(new ActionData
                    {
                        Tool      = "Chat",
                        Arguments = { ["message"] = pendingResponse }
                    });
                    _ = PushChatToDashboardAsync("bot", botName, pendingResponse);
                    pendingResponse = null;
                }

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

            case ChatIntentType.Chat:
            case ChatIntentType.Unknown:
                break;
        }

        if (pendingResponse is not null)
        {
            _queue.Enqueue(new ActionData
            {
                Tool      = "Chat",
                Arguments = { ["message"] = pendingResponse }
            });
            _ = PushChatToDashboardAsync("bot", botName, pendingResponse);
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
        // Sprint 15 P1 (Sprint 13 D3): clear recovery rate-limiter so the next run
        // of the same goal can trigger recovery fresh if it fails again.
        _lastRecoveredGoalName = null;
        _queue.Clear();
        lock (_pendingLock) _pendingActions.Clear();
        // Sprint 18: emit stop so Node.js also aborts the in-progress mine action.
        SendEmergencyStop();
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
                    _lastAbandonedGoalName = _currentGoal.Name;
                    _journal?.Log(new JournalEntry(
                        DateTimeOffset.UtcNow, JournalEntryType.ReplanTriggered, _currentGoal.Name));
                    logger.LogWarning("Goal '{Goal}' failed (failures={N}, reason={Reason}).",
                        _currentGoal.Name, _consecutiveFailures, reason);
                    _currentGoal = null; _consecutiveFailures = 0; _lastFailureReason = null;
                    planContext.Clear();
                    lock (_pendingLock) _pendingActions.Clear();
                    continue;
                }

                // Sprint 18: minimum replan interval — prevents replanning storm under the
                // fire-and-forget architecture where tools return in <1 ms after sending to Node.js.
                // Without this, the planner runs ~3x/second; with it, at most once per 2 seconds.
                if ((DateTimeOffset.UtcNow - _lastReplanAt) < TimeSpan.FromSeconds(MinReplanIntervalSeconds))
                {
                    await Task.Delay(50, ct);
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
                    _lastReplanAt = DateTimeOffset.UtcNow; // Sprint 18: record plan time
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
                // Sprint 15 P1: record dispatch time for stall detection
                _lastActionDispatchedAt = DateTimeOffset.UtcNow;
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
                        if (IsProgressSignalTool(action.Tool))
                        {
                            _consecutiveFailures = 0;
                            _lastFailureReason = null;
                        }
                        if (result.Data is not null)
                            foreach (var kv in result.Data)
                                planContext[kv.Key] = kv.Value;

                        if (action.Tool.Equals("PlaceBlock", StringComparison.OrdinalIgnoreCase)
                            && action.Context.TryGetValue(
                                BuildFactKeys.PlaceBlockProgressBlueprintId, out var bpId)
                            && action.Context.TryGetValue(
                                BuildFactKeys.PlaceBlockProgressBlockIndex, out var bpIdx))
                        {
                            var progressFactKey = BuildFactKeys.BuildProgressIndex(
                                bpId?.ToString() ?? string.Empty);
                            _worldState = _worldState.With(b => b.SetFact(progressFactKey, bpIdx));
                            logger.LogDebug("[build] checkpoint: {Blueprint} block {Index} placed",
                                bpId, bpIdx);
                        }
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

                        var isImmediateRecovery =
                            errMsg.StartsWith("blockNotFound:", StringComparison.OrdinalIgnoreCase) ||
                            (errMsg.Contains("recipe", StringComparison.OrdinalIgnoreCase) &&
                             errMsg.Contains("missing", StringComparison.OrdinalIgnoreCase));

                        if (_consecutiveFailures >= 2 || isImmediateRecovery)
                            await TryRecoverFromGameErrorAsync(errMsg, ct);
                    }

                    // Sprint 15 P1: stall detection
                    if (_currentGoal is not null)
                    {
                        var stalledFor = DateTimeOffset.UtcNow - _lastActionDispatchedAt;
                        if (stalledFor.TotalSeconds > StallWarningSeconds &&
                            (DateTimeOffset.UtcNow - _lastStallWarnedAt).TotalSeconds > StallWarningSuppressSeconds)
                        {
                            _lastStallWarnedAt = DateTimeOffset.UtcNow;
                            logger.LogWarning(
                                "[stall] No action dispatched in {Elapsed:N0}s with active goal '{Goal}' — agent may be stuck.",
                                (int)stalledFor.TotalSeconds, _currentGoal.Name);
                        }
                    }
                }
                else
                    await Task.Delay(50, ct);
            }
        }
    }

    // ── Emergency stop ────────────────────────────────────────────────────────

    /// <summary>
    /// Dispatches an emergency stop signal to the world adapter, aborting any in-progress
    /// Node.js action (mine loop, pathfinding, findFlatArea scan).
    ///
    /// Fire-and-forget: does not await a response. The C# queue is already cleared by the
    /// caller (SetGoal / CancelGoal / TryCompleteCurrentGoalFromWorldUpdate). This signal
    /// ensures the Node.js side also stops promptly rather than completing the in-flight action.
    ///
    /// The "stop" action is handled in index.js before entering cmdQueue (bypasses the command
    /// queue), so it takes effect immediately regardless of pending commands.
    ///
    /// Sprint 18: resolves "leo stop" not stopping Node.js and goal completion not aborting
    /// in-progress mine loops.
    /// </summary>
    private void SendEmergencyStop()
    {
        try
        {
            _ = worldAdapter.SendActionAsync(
                new ActionData { Tool = "stop" }, CancellationToken.None);
            logger.LogInformation("[stop] emergency stop dispatched to adapter");
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[stop] failed to dispatch emergency stop (adapter may not be connected): {Error}", ex.Message);
        }
    }

    // ── Thinking indicator ─────────────────────────────────────────────────────

    private static readonly string[] _thinkingMessages =
        ["Hmm...", "...", "Let me think...", "*thinks*"];

    private async Task EnqueueThinkingIfSlowAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1.5), ct);
            var msg = _thinkingMessages[Random.Shared.Next(_thinkingMessages.Length)];
            _queue.Enqueue(new ActionData { Tool = "Chat", Arguments = { ["message"] = msg } });
            logger.LogInformation("[chat] thinking indicator sent ('{Msg}') — LLM response pending >1.5s", msg);
        }
        catch (OperationCanceledException) { /* fast path — thinking not needed */ }
    }

    // ── SignalR dashboard push — Sprint 4a ─────────────────────────────────

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

    /// <summary>
    /// Asks the LLM interpreter to suggest an alternative goal when the agent has hit
    /// a game error it cannot self-resolve.
    /// </summary>
    private async Task TryRecoverFromGameErrorAsync(string errMsg, CancellationToken ct)
    {
        if (chatInterpreter is null || _currentGoal is null) return;

        if (string.Equals(_currentGoal.Name, _lastRecoveredGoalName, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug(
                "[recovery] already attempted recovery for '{Goal}' — skipping duplicate LLM call",
                _currentGoal.Name);
            return;
        }
        _lastRecoveredGoalName = _currentGoal.Name;

        _journal?.Log(new JournalEntry(
            DateTimeOffset.UtcNow, JournalEntryType.ErrorRecovery, errMsg,
            new Dictionary<string, object?> { ["goal"] = _currentGoal.Name }));

        try
        {
            var invSummary = _worldState.Inventory.Count > 0
                ? string.Join(", ", _worldState.Inventory
                    .OrderByDescending(kv => kv.Value).Take(5)
                    .Select(kv => $"{kv.Value}x{kv.Key}"))
                : "empty";

            var prompt =
                $"recover from runtime error while executing goal {_currentGoal.Name}: {errMsg}. " +
                $"Bot inventory: {invSummary}. " +
                "Available actions: gather, navigate, status, craft, smelt. " +
                "If gather target is unavailable, choose an alternative gather goal or navigate action. " +
                "If recipe is missing, check whether a crafting table or raw material is needed first.";

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
                if (GoalNamesMatch(interpretation.GoalName, _currentGoal?.Name))
                {
                    logger.LogDebug(
                        "[recovery] LLM suggested current goal '{Goal}' — no change needed",
                        interpretation.GoalName);
                    return;
                }
                if (GoalNamesMatch(interpretation.GoalName, _lastAbandonedGoalName))
                {
                    logger.LogWarning(
                        "[recovery] LLM suggested '{Goal}' — same item as recently abandoned; skipping to break loop",
                        interpretation.GoalName);
                    return;
                }

                logger.LogInformation("Recovery interpreter suggested goal: {Goal}", interpretation.GoalName);
                await TryCreateGoalFromChatAsync(interpretation, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Recovery interpreter failed for error: {Error}", errMsg);
        }
    }

    /// <summary>Compares two goal name strings by their item-ID suffix (everything after ':').</summary>
    private static bool GoalNamesMatch(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        var itemA = a.Contains(':') ? a[(a.IndexOf(':') + 1)..] : a;
        var itemB = b.Contains(':') ? b[(b.IndexOf(':') + 1)..] : b;
        return string.Equals(itemA, itemB, StringComparison.OrdinalIgnoreCase);
    }
}
