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

            _worldState = _worldState.With(b =>
            {
                foreach (var kv in worldEvent.Payload)
                    b.SetFact($"event:{worldEvent.EventType}:{kv.Key}", kv.Value);
            });

            // Update health from health events
            if (worldEvent.EventType == "health" &&
                worldEvent.Payload.TryGetValue("hp", out var hp) && hp is int healthVal)
            {
                _worldState = _worldState with { Health = healthVal };
            }
        }
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
                try
                {
                    var argsJson = JsonSerializer.Serialize(action.Arguments);
                    using var doc = JsonDocument.Parse(argsJson);
                    var result = await toolCaller.CallAsync(action.Tool, doc.RootElement, ct);

                    if (result.Success)
                    {
                        logger.LogInformation("Tool {Tool}: {Message}", action.Tool, result.Message);
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
                await Task.Delay(50, ct);
            }
        }
    }
}
