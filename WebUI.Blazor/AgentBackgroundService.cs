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
///        processed by <see cref="ChatConsumerAsync"/> on a dedicated task, so a 5-10s
///        LLM call never stalls health/death/blockMined processing.
///   1b — Reconnect: <see cref="ExecuteAsync"/> retries ConnectAsync with exponential
///        backoff (default 2s/4s/8s/16s/32s, max 5 retries). World state and current
///        goal survive reconnects. Configurable delays for test speed.
/// Sprint 8:
///   - TryRecoverFromGameErrorAsync: richer prompt (inventory + available actions);
///     immediate trigger for blockNotFound/recipeMissing; ErrorRecovery journal log.
/// Sprint 9:
///   - FlatAreaFoundEvent: when Area >= MinUsableFlatArea, auto-sets build origin.
/// Sprint 11:
///   - EnqueueThinkingIfSlowAsync logs when indicator fires.
///   - HandleChatEventAsync logs resolved intent after interpretation.
/// Sprint 12:
///   - ActionQueue switched to ConcurrentQueue — fixes infinite planning loop caused
///     by non-thread-safe Queue being accessed from two concurrent async tasks.
///   - HandleChatEventAsync defers response enqueue to AFTER the switch — fixes the
///     bug where SetGoal/CancelGoal cleared the queued chat response before dispatch.
///   - SetGoal resets _actionDispatchedThisCycle so DispatchActionsAsync can plan
///     immediately rather than waiting for the 300ms settle window.
///   - _lastAbandonedGoalName guards TryRecoverFromGameErrorAsync from re-setting the
///     same goal that just failed, breaking the infinite recovery loop.
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
    // TryRecoverFromGameErrorAsync checks this to avoid re-setting the same goal
    // that just failed, which would create an infinite recovery loop.
    private string? _lastAbandonedGoalName;

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
        // Sprint 12: reset so DispatchActionsAsync can plan immediately without
        // waiting for the 300ms post-cycle settle delay.
        _actionDispatchedThisCycle = false;
        // Sprint 12: clear recovery guard — this is a fresh goal, not a retry.
        _lastAbandonedGoalName = null;
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
                    logger.LogInformation("Inventory +1 {Block} -> total {Total}",
                        itemKey, _worldState.Inventory.GetValueOrDefault(itemKey));
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
        // SetGoal (called via CreateGoal path) and CancelGoal both call _queue.Clear(),
        // which would wipe a chat response enqueued before the switch.
        // We enqueue it AFTER the switch so goal changes cannot clear it.
        var pendingResponse = !string.IsNullOrEmpty(interpretation.Response)
            ? interpretation.Response : null;

        // Always mark that the bot processed this addressed message so the
        // conversation window stays open for follow-ups.
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
                // Player navigation should interrupt any active gather loop.
                CancelGoal();

                // Sprint 12: enqueue chat response BEFORE the movement action so the
                // player sees "On my way!" / "Heading to..." immediately, not after arrival.
                if (pendingResponse is not null)
                {
                    _queue.Enqueue(new ActionData
                    {
                        Tool      = "Chat",
                        Arguments = { ["message"] = pendingResponse }
                    });
                    _ = PushChatToDashboardAsync("bot", botName, pendingResponse);
                    pendingResponse = null; // consumed here — skip the post-switch enqueue
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

        // Sprint 12: enqueue response AFTER the switch so it survives SetGoal/CancelGoal clears.
        // For CreateGoal: this runs after SetGoal, so the response is at the HEAD of the queue
        // and DispatchActionsAsync says it BEFORE executing any plan actions.
        // For NavigateTo: already consumed above (pendingResponse = null).
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
                    // Sprint 12: record before clearing, so TryRecoverFromGameErrorAsync
                    // can avoid re-suggesting this same goal.
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
                }
                else
                    await Task.Delay(50, ct);
            }
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
    ///
    /// Sprint 12: guards against the infinite recovery loop where the LLM suggests
    /// the same goal that just failed. If <see cref="_lastAbandonedGoalName"/> matches
    /// the suggestion, the suggestion is discarded and a warning is logged.
    /// </summary>
    private async Task TryRecoverFromGameErrorAsync(string errMsg, CancellationToken ct)
    {
        if (chatInterpreter is null || _currentGoal is null) return;

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
                // Sprint 12: don't reset to the same goal that just failed — prevents infinite loop.
                if (string.Equals(interpretation.GoalName, _lastAbandonedGoalName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning(
                        "[recovery] LLM suggested '{Goal}' — same as recently abandoned goal; skipping to break loop",
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
}
