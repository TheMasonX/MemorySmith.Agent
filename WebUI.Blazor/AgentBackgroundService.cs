namespace WebUI.Blazor;

using Agent.Core;
using Agent.Planning;
using Agent.Tools;
using System.Text.Json;
using System.Threading.Channels;

/// <summary>
/// Hosted service that owns the agent loop.
///
/// Phase 1: basic loop — external Enqueue() feeds actions, dispatched via ToolEngine.
/// Phase 3: IPlanner integration — when the queue is empty and a goal is set,
///          the planner generates the next action sequence automatically.
/// Phase 5: chat event routing — chat WorldEvents are forwarded to ChatInterpreter;
///          valid intents become new goals or enqueue immediate actions.
///          New public API: CancelGoal, SetBuildOrigin, GetPendingActions.
///
/// Lifecycle:
///   1. ConnectAsync to the world adapter.
///   2. ProcessEventsAsync — read WorldEvents, apply WorldStateProjector,
///      route chat events to ChatInterpreter, route error events to the
///      typed _gameErrors channel.
///   3. DispatchActionsAsync — drain ActionQueue, call planner when idle,
///      consume error channel after each plan-cycle settle.
///
/// maxConsecutiveFailures defaults to 3.
/// </summary>
public sealed class AgentBackgroundService(
    IWorldAdapter worldAdapter,
    IToolCaller toolCaller,
    ILogger<AgentBackgroundService> logger,
    IPlanner planner,
    GoalFactory? goalFactory = null,
    ChatInterpreter? chatInterpreter = null,
    string botName = "AgentBot",
    int maxConsecutiveFailures = 3) : BackgroundService
{
    private readonly ActionQueue _queue = new();
    private readonly WorldStateProjector _projector = new();
    private readonly Channel<string> _gameErrors =
        Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleWriter = true });

    // Snapshot of the current plan's actions for UI inspection
    private readonly List<ActionData> _pendingActions = [];
    private readonly object _pendingLock = new();

    private WorldState _worldState = new();
    private IGoal? _currentGoal;
    private int _consecutiveFailures;
    private bool _actionDispatchedThisCycle;

    public WorldState WorldState       => _worldState;
    public IGoal?     CurrentGoal      => _currentGoal;
    public int        ConsecutiveFailures => _consecutiveFailures;

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

    /// <summary>Cancel the current goal and clear the action queue.</summary>
    public void CancelGoal()
    {
        if (_currentGoal is not null)
            logger.LogInformation("Goal cancelled: {Goal}", _currentGoal.Name);
        _currentGoal = null;
        _consecutiveFailures = 0;
        _queue.Clear();
        lock (_pendingLock) _pendingActions.Clear();
    }

    /// <summary>
    /// Set the build origin for the specified blueprint in world-state facts.
    /// The planner reads these facts to compute absolute block coordinates.
    /// </summary>
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

    /// <summary>Returns a snapshot of pending actions for UI display.</summary>
    public IReadOnlyList<ActionData> GetPendingActions()
    {
        lock (_pendingLock)
            return [.. _pendingActions];
    }

    // ── BackgroundService ─────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("AgentBackgroundService starting...");
        try
        {
            await worldAdapter.ConnectAsync(stoppingToken);
            logger.LogInformation("World adapter connected.");
            await Task.WhenAll(
                ProcessEventsAsync(stoppingToken),
                DispatchActionsAsync(stoppingToken));
        }
        catch (OperationCanceledException) { logger.LogInformation("AgentBackgroundService stopping."); }
        catch (Exception ex) { logger.LogError(ex, "AgentBackgroundService encountered a fatal error."); }
        finally
        {
            try { await worldAdapter.DisconnectAsync(stoppingToken); }
            catch { /* best-effort disconnect */ }
        }
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
                    if (worldEvent.Payload.TryGetValue("item",  out var craftItem)  && craftItem  is string ci &&
                        worldEvent.Payload.TryGetValue("count", out var craftCount) && craftCount is int    cc)
                        logger.LogInformation("Crafted {Count}x {Item}", cc, ci);
                    break;

                case "smeltComplete":
                    if (worldEvent.Payload.TryGetValue("item",   out var smeltItem)   && smeltItem   is string si &&
                        worldEvent.Payload.TryGetValue("result", out var smeltResult) && smeltResult is string sr &&
                        worldEvent.Payload.TryGetValue("count",  out var smeltCount)  && smeltCount  is int    sc)
                        logger.LogInformation("Smelted {Count}x {Input} → {Output}", sc, si, sr);
                    break;

                case "chat":
                    await HandleChatEventAsync(worldEvent, ct);
                    break;

                default:
                    TryRouteAsError(worldEvent);
                    break;
            }
        }
    }

    private async Task HandleChatEventAsync(WorldEvent worldEvent, CancellationToken ct)
    {
        if (chatInterpreter is null) return;

        if (!worldEvent.Payload.TryGetValue("username", out var usernameObj) || usernameObj is not string username)
            return;
        if (!worldEvent.Payload.TryGetValue("message",  out var messageObj)  || messageObj  is not string message)
            return;

        var onlinePlayers = worldEvent.Payload.TryGetValue("onlinePlayers", out var opObj) && opObj is int op
            ? op : 1;

        logger.LogInformation("[chat] <{Username}> {Message}", username, message);

        var interpretation = chatInterpreter.Interpret(
            username, message, botName, onlinePlayers, _worldState);

        if (interpretation.IntentType == ChatIntentType.NotAddressed)
            return;

        // Respond in chat if there's a response message
        if (!string.IsNullOrEmpty(interpretation.Response))
        {
            _queue.Enqueue(new ActionData
            {
                Tool = "Chat",
                Arguments = { ["message"] = interpretation.Response }
            });
            chatInterpreter.RecordBotSpoke();
        }

        // Act on the intent
        switch (interpretation.IntentType)
        {
            case ChatIntentType.CancelGoal:
                CancelGoal();
                break;

            case ChatIntentType.QueryStatus:
                // Response already queued above; update world state fact
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
        if (goalFactory is null || interpretation.GoalName is null)
            return;

        try
        {
            var goal = await goalFactory.CreateAsync(
                interpretation.GoalName, interpretation.GoalParameters, ct);

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
                    Arguments = { ["message"] = $"Sorry, I don't know how to do that yet." }
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
            worldEvent.Payload.TryGetValue("mined", out var minedObj) &&
            minedObj is int minedCount && minedCount == 0 &&
            worldEvent.Payload.TryGetValue("block", out var bObj) &&
            bObj is string bNotFound)
        {
            errMsg = $"blockNotFound:{bNotFound}";
            logger.LogWarning("No {Block} found in range — will count as failure.", bNotFound);
        }
        else if (worldEvent.EventType == "error")
        {
            var act = worldEvent.Payload.TryGetValue("action",  out var a) && a is string sa ? sa : "?";
            var msg = worldEvent.Payload.TryGetValue("message", out var m) && m is string sm ? sm : "unknown";
            errMsg = $"{act}:{msg}";
            logger.LogWarning("Game error [{Action}]: {Message}", act, msg);
        }

        if (errMsg is not null)
            _gameErrors.Writer.TryWrite(errMsg);
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
