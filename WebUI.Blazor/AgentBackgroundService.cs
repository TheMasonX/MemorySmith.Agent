namespace WebUI.Blazor;

using Agent.Core;
using Agent.Planning;
using Agent.Tools;
using System.Text.Json;

/// <summary>
/// Hosted service that owns the agent loop.
///
/// Phase 1: basic loop — external Enqueue() feeds actions, dispatched via ToolEngine.
/// Phase 3: IPlanner integration — when the queue is empty and a goal is set,
///          the planner generates the next action sequence automatically.
///
/// Lifecycle:
///   1. ConnectAsync to the world adapter.
///   2. ProcessEventsAsync — read WorldEvents, update WorldState.
///   3. DispatchActionsAsync — drain ActionQueue, call planner when idle.
/// </summary>
public sealed class AgentBackgroundService(
    IWorldAdapter worldAdapter,
    IToolCaller toolCaller,
    ILogger<AgentBackgroundService> logger,
    IPlanner planner) : BackgroundService
{
    private readonly ActionQueue _queue = new();
    private WorldState _worldState = new();
    private IGoal? _currentGoal;
    private int _consecutiveFailures;
    private bool _actionDispatchedThisCycle;

    public WorldState WorldState => _worldState;
    public IGoal? CurrentGoal => _currentGoal;

    /// <summary>Externally enqueue a single action (REST API path).</summary>
    public void Enqueue(ActionData action) => _queue.Enqueue(action);

    /// <summary>
    /// Set a new goal. Clears the current action queue and triggers replanning
    /// on the next dispatch cycle.
    /// </summary>
    public void SetGoal(IGoal goal)
    {
        _currentGoal = goal;
        _queue.Clear();
        _consecutiveFailures = 0;
        logger.LogInformation("Goal set: {Goal}", goal.Name);
    }

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
        catch (OperationCanceledException)
        {
            logger.LogInformation("AgentBackgroundService stopping.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AgentBackgroundService encountered a fatal error.");
        }
        finally
        {
            try { await worldAdapter.DisconnectAsync(stoppingToken); }
            catch { /* best-effort disconnect */ }
        }
    }

    // ── Event processing ─────────────────────────────────────────────────────

    private async Task ProcessEventsAsync(CancellationToken ct)
    {
        await foreach (var worldEvent in worldAdapter.ReceiveEventsAsync(ct))
        {
            logger.LogDebug("World event: {Type}", worldEvent.EventType);

            // Store all payload fields as raw facts for inspection / debugging
            _worldState = _worldState.With(b =>
            {
                foreach (var kv in worldEvent.Payload)
                    b.SetFact($"event:{worldEvent.EventType}:{kv.Key}", kv.Value);
            });

            // Structured updates — each event type updates the canonical WorldState fields
            switch (worldEvent.EventType)
            {
                case "health":
                    ApplyHealthAndFood(worldEvent.Payload);
                    break;

                case "spawn":
                    ApplyPosition(worldEvent.Payload);
                    ApplyHealthAndFood(worldEvent.Payload);
                    logger.LogInformation("Bot spawned at {Pos}", _worldState.Position);
                    break;

                case "move":
                case "moveComplete":
                    ApplyPosition(worldEvent.Payload);
                    break;

                case "status":
                    ApplyPosition(worldEvent.Payload);
                    ApplyHealthAndFood(worldEvent.Payload);
                    ApplyInventorySnapshot(worldEvent.Payload);
                    break;

                case "blockMined":
                    // Each blockMined event = one block collected
                    if (worldEvent.Payload.TryGetValue("block", out var rawBlock) &&
                        rawBlock is string blockName)
                    {
                        var itemKey = blockName.Contains(':') ? blockName.Split(':')[1] : blockName;
                        _worldState = _worldState.With(b => b.AddInventoryItem(itemKey, 1));
                        logger.LogInformation(
                            "Inventory +1 {Block} → total {Total}",
                            itemKey, _worldState.Inventory.GetValueOrDefault(itemKey));
                    }
                    break;

                case "blockNotFound":
                    // Node.js could not find the block type within its search radius.
                    // Only marks a game error when nothing was mined (mined=0 means
                    // total failure; mined>0 means some blocks collected then ran out).
                    if (worldEvent.Payload.TryGetValue("mined", out var minedObj) &&
                        minedObj is int minedCount && minedCount == 0 &&
                        worldEvent.Payload.TryGetValue("block", out var bObj) &&
                        bObj is string bNotFound)
                    {
                        _worldState = _worldState.With(b =>
                            b.SetFact("game.lastError", $"blockNotFound:{bNotFound}"));
                        logger.LogWarning("No {Block} found in range — will count as failure.", bNotFound);
                    }
                    break;

                case "error":
                    // Game-side action failed (e.g. pathfinder timeout, block unreachable).
                    // Set a fact that DispatchActionsAsync will consume after the settle delay
                    // to increment _consecutiveFailures.
                    {
                        var act = worldEvent.Payload.TryGetValue("action",  out var a) && a is string sa ? sa : "?";
                        var msg = worldEvent.Payload.TryGetValue("message", out var m) && m is string sm ? sm : "unknown";
                        _worldState = _worldState.With(b =>
                            b.SetFact("game.lastError", $"{act}:{msg}"));
                        logger.LogWarning("Game error [{Action}]: {Message}", act, msg);
                    }
                    break;
            }
        }
    }

    // ── WorldState update helpers ─────────────────────────────────────────────

    private void ApplyPosition(IReadOnlyDictionary<string, object?> payload)
    {
        if (payload.TryGetValue("x", out var ox) && ox is int px &&
            payload.TryGetValue("y", out var oy) && oy is int py &&
            payload.TryGetValue("z", out var oz) && oz is int pz)
        {
            _worldState = _worldState with { Position = new Position(px, py, pz) };
        }
    }

    private void ApplyHealthAndFood(IReadOnlyDictionary<string, object?> payload)
    {
        if (payload.TryGetValue("hp", out var ohp) && ohp is int hp)
            _worldState = _worldState with { Health = hp };
        if (payload.TryGetValue("food", out var of) && of is int food)
            _worldState = _worldState with { Food = food };
    }

    /// <summary>
    /// Parses the "inventory" field from a status event (a raw JSON object string
    /// from WebSocketBridge) and replaces WorldState.Inventory with a full snapshot.
    /// </summary>
    private void ApplyInventorySnapshot(IReadOnlyDictionary<string, object?> payload)
    {
        if (!payload.TryGetValue("inventory", out var invRaw) || invRaw is not string invJson)
            return;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(invJson);
            var snap = new Dictionary<string, int>();
            foreach (var prop in doc.RootElement.EnumerateObject())
                if (prop.Value.TryGetInt32(out var qty) && qty > 0)
                    snap[prop.Name] = qty;
            _worldState = _worldState.With(b => b.SetInventory(snap));
            logger.LogDebug("Inventory snapshot: {Count} item types", snap.Count);
        }
        catch { /* ignore malformed inventory JSON */ }
    }

    // ── Action dispatch loop ─────────────────────────────────────────────────

    private async Task DispatchActionsAsync(CancellationToken ct)
    {
        const int MaxConsecutiveFailures = 3;

        // Phase 4: shared context bag carried across all actions in the current plan.
        // Tools write here (e.g. SearchMemory writes result coordinates);
        // subsequent tools read here (e.g. MoveToTool reads nearestWoodX/Y/Z).
        var planContext = new Dictionary<string, object?>();

        while (!ct.IsCancellationRequested)
        {
            // When queue is empty and a goal is active — ask the planner
            if (_queue.IsEmpty && _currentGoal is not null)
            {
                if (_currentGoal.IsComplete(_worldState))
                {
                    logger.LogInformation("Goal '{Goal}' completed.", _currentGoal.Name);
                    _currentGoal = null;
                    _consecutiveFailures = 0;
                    planContext.Clear();
                    continue;
                }

                if (_currentGoal.HasFailed(_worldState) ||
                    _consecutiveFailures >= MaxConsecutiveFailures)
                {
                    logger.LogWarning("Goal '{Goal}' failed (failures={N}).",
                        _currentGoal.Name, _consecutiveFailures);
                    _currentGoal = null;
                    _consecutiveFailures = 0;
                    planContext.Clear();
                    continue;
                }

                try
                {
                    var plan = await planner.PlanAsync(_currentGoal, _worldState, ct);

                    // Inject the shared context into each action before enqueuing
                    foreach (var planAction in plan.Actions)
                    {
                        foreach (var kv in planContext)
                            planAction.Context.TryAdd(kv.Key, kv.Value);
                    }

                    _queue.EnqueueAll(plan.Actions);
                    _actionDispatchedThisCycle = false; // reset for the new cycle
                    logger.LogInformation(
                        "New plan for '{Goal}': {Count} actions.",
                        _currentGoal.Name, plan.Actions.Count);
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
                try
                {
                    var argsJson = JsonSerializer.Serialize(action.Arguments);
                    using var doc = JsonDocument.Parse(argsJson);
                    var result = await toolCaller.CallAsync(action.Tool, doc.RootElement, ct);

                    if (result.Success)
                    {
                        // Downgraded to Debug — individual tool dispatches are too noisy at Info
                        logger.LogDebug("Tool {Tool}: {Message}", action.Tool, result.Message);
                        _consecutiveFailures = 0;

                        // Carry tool result data into the shared plan context
                        if (result.Data is not null)
                        {
                            foreach (var kv in result.Data)
                                planContext[kv.Key] = kv.Value;
                        }
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
                // After a full plan cycle drains, pause briefly so in-flight blockMined /
                // status / error events can arrive and update WorldState before we check IsComplete.
                if (_actionDispatchedThisCycle)
                {
                    logger.LogDebug("Plan cycle complete — settling for 300 ms");
                    await Task.Delay(300, ct);
                    _actionDispatchedThisCycle = false;

                    // Consume any game-error fact set by ProcessEventsAsync during the plan cycle.
                    // Doing this AFTER the settle ensures late-arriving error events are captured.
                    if (_worldState.Facts.TryGetValue("game.lastError", out var gameErr) &&
                        gameErr is string errMsg && !string.IsNullOrEmpty(errMsg))
                    {
                        _worldState = _worldState.With(b => b.SetFact("game.lastError", null));
                        _consecutiveFailures++;
                        logger.LogWarning(
                            "Game error after cycle (failures={N}/{Max}): {Error}",
                            _consecutiveFailures, MaxConsecutiveFailures, errMsg);
                    }
                }
                else
                {
                    await Task.Delay(50, ct);
                }
            }
        }
    }
}
