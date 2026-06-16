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
///
/// Lifecycle:
///   1. ConnectAsync to the world adapter.
///   2. ProcessEventsAsync — read WorldEvents, apply <see cref="WorldStateProjector"/>,
///      route error events to the typed <see cref="_gameErrors"/> channel.
///   3. DispatchActionsAsync — drain ActionQueue, call planner when idle,
///      consume error channel after each plan-cycle settle.
///
/// <paramref name="maxConsecutiveFailures"/> defaults to 3. Override in tests to observe
/// failure behaviour after fewer errors without waiting for multiple plan cycles.
/// </summary>
public sealed class AgentBackgroundService(
    IWorldAdapter worldAdapter,
    IToolCaller toolCaller,
    ILogger<AgentBackgroundService> logger,
    IPlanner planner,
    int maxConsecutiveFailures = 3) : BackgroundService
{
    private readonly ActionQueue _queue = new();

    // Pure projector: maps (WorldState, WorldEvent) → WorldState.
    private readonly WorldStateProjector _projector = new();

    // Typed error channel replaces the stringly-typed game.lastError WorldState fact.
    // ProcessEventsAsync writes; DispatchActionsAsync reads after the settle delay.
    private readonly Channel<string> _gameErrors =
        Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleWriter = true });

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

            // Delegate all state projection to the pure WorldStateProjector.
            _worldState = _projector.Apply(_worldState, worldEvent);

            // Supplemental logging for notable state transitions
            if (worldEvent.EventType == "spawn")
                logger.LogInformation("Bot spawned at {Pos}", _worldState.Position);

            if (worldEvent.EventType == "blockMined" &&
                worldEvent.Payload.TryGetValue("block", out var rawBlock) &&
                rawBlock is string blockName)
            {
                var itemKey = blockName.Contains(':') ? blockName.Split(':')[1] : blockName;
                logger.LogInformation(
                    "Inventory +1 {Block} → total {Total}",
                    itemKey, _worldState.Inventory.GetValueOrDefault(itemKey));
            }

            // Route game errors to the typed error channel.
            // DispatchActionsAsync reads from this channel after the settle delay,
            // replacing the old stringly-typed game.lastError WorldState fact.
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
    }

    // ── Action dispatch loop ─────────────────────────────────────────────────

    private async Task DispatchActionsAsync(CancellationToken ct)
    {
        // Captured from the constructor parameter so it can be overridden in tests.
        var maxFailures = maxConsecutiveFailures;

        // Phase 4: shared context bag carried across all actions in the current plan.
        // Tools write here (e.g. SearchMemory writes result coordinates);
        // subsequent tools read here (e.g. MoveToTool reads nearestWoodX/Y/Z).
        var planContext = new Dictionary<string, object?>();

        while (!ct.IsCancellationRequested)
        {
            // When queue is empty, a goal is active, and a plan cycle has NOT just completed —
            // ask the planner for the next action sequence.
            // The !_actionDispatchedThisCycle guard is critical: when a plan cycle just
            // completed (_actionDispatchedThisCycle = true), we must fall through to the
            // else branch below so the settle delay fires and the error channel is read
            // BEFORE starting the next plan. Without this guard, re-planning happens
            // in the same iteration as the settle window is entered, and the channel
            // read never runs.
            if (_queue.IsEmpty && _currentGoal is not null && !_actionDispatchedThisCycle)
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
                    _consecutiveFailures >= maxFailures)
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

                    // Consume any game errors signalled by ProcessEventsAsync during the plan cycle.
                    // The typed Channel replaces the old stringly-typed game.lastError WorldState fact.
                    // Doing this AFTER the settle ensures late-arriving error events are captured.
                    if (_gameErrors.Reader.TryRead(out var errMsg))
                    {
                        _consecutiveFailures++;
                        logger.LogWarning(
                            "Game error after cycle (failures={N}/{Max}): {Error}",
                            _consecutiveFailures, maxFailures, errMsg);
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
