namespace WebUI.Blazor;

using Agent.Construction;
using Agent.Core;
using Agent.Core.Runtime;
using Agent.Planning;
using Agent.Planning.Goals;
using Agent.Tools;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Diagnostics;
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
///
/// Sprint 25:
///   - P0-D: ConcurrentDictionary&lt;Guid, PendingAction&gt; tracks dispatched action lifecycle.
///     Replaces implicit "dispatched = done" assumption. Timeout sweep catches orphans.
///
/// Sprint 36:
///   - P0-A: TryInterruptOnDamageAsync: ClearAndEnqueueAsync replaces the separate
///     SendEmergencyStop() + ClearAndEnqueue() pattern to ensure stop reaches JS before
///     the queue is cleared and new actions are dispatched.
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
    Logging.IChatLogger? chatLogger = null,
    TimeSpan[]? reconnectDelays = null,
    IReplanGovernor? replanGovernor = null,
    ITimeProvider? timeProvider = null,
    // Sprint 39 P1-C: IntentManager maps IntentDraft → GoalRequest for HandleChatEventAsync.
    IntentManager? intentManager = null,
    // Sprint 39 P1: LLM evaluator for observation-driven replanning.
    ILlmEvaluator? llmEvaluator = null,
    // Sprint 39 P2: AgentRuntime encapsulates the six decomposed manager components.
    AgentRuntime? agentRuntime = null) : BackgroundService
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
    /// Sprint 22 P0: health (hearts) below which the agent issues an emergency GetStatus
    /// to assess drowning or critical-damage state. 6 = 3 hearts.
    /// </summary>
    private const int HealthCriticalThreshold = 6;

    /// <summary>
    /// Minimum seconds between successive replans.
    /// Sprint 18: tools dispatch fire-and-forget to Node.js; without this guard the planner
    /// runs at CPU speed (every 50–300 ms) before Node.js executes any action.
    /// </summary>
    private const int MinReplanIntervalSeconds = 2;

    /// <summary>
    /// Sprint 23 P0-A: minimum seconds between successive damage-interrupt triggers.
    /// Prevents queue flooding when the bot takes repeated rapid damage (e.g. drowning, lava).
    /// </summary>
    private const int DamageInterruptCooldownSeconds = 3;

    private const string EmergencyStopActionName = "StopNow";

    /// <summary>
    /// Sprint 23 P1-B: minimum seconds between passive health-critical GetStatus enqueues.
    /// Distinct from <see cref="DamageInterruptCooldownSeconds"/>: this gate applies to the
    /// non-interrupt path (health below threshold without a detected delta event).
    /// </summary>
    private const int HealthCheckCooldownSeconds = 2;

    private readonly TimeSpan[] _reconnectDelays = reconnectDelays ?? DefaultReconnectDelays;

    private readonly ActionQueue _queue = new();
    private readonly WorldStateProjector _projector = new();
    private readonly IAgentJournal? _journal = journal;
    private readonly Logging.IChatLogger? _chatLogger = chatLogger;
    private readonly ITimeProvider _timeProvider = timeProvider ?? SystemTimeProvider.Instance;
    // Sprint 39 P1-C: maps IntentDraft → GoalRequest in HandleChatEventAsync.
    private readonly IntentManager? _intentManager = intentManager;
    // Sprint 39 P1+P2: LLM evaluator and AgentRuntime fields.
    private readonly ILlmEvaluator? _llmEvaluator = llmEvaluator;
    private readonly AgentRuntime?  _agentRuntime  = agentRuntime;

    private readonly Channel<string> _gameErrors =
        Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleWriter = true });

    // Sprint 1a: chat events written here by ProcessEventsAsync; ChatConsumerAsync reads them
    private readonly Channel<WorldEvent> _chatChannel =
        Channel.CreateUnbounded<WorldEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private readonly List<ActionData> _pendingActions = [];
    private readonly object _pendingLock = new();

    // Sprint 25 P0-D: action correlation — tracks dispatched actions awaiting adapter result events.
    // Key = correlationId (Guid generated at dispatch time). Value = PendingAction with lifecycle state.
    private readonly ConcurrentDictionary<Guid, PendingAction> _correlatedActions = new();

    // Sprint 38 P3 / Sprint 39 D-S38-04: accumulate ActionOutcomes per dispatch cycle for
    // observation-driven replanning. ConcurrentQueue is safe for concurrent Enqueue from
    // DispatchActionsAsync and Clear from SetGoal/plan generation without an extra lock.
    // Cleared when a new plan is generated or when a new goal is set.
    // Read by ILlmEvaluator (Sprint 39).
    private readonly ConcurrentQueue<ActionOutcome> _cycleOutcomes = new();

    private WorldState _worldState = new();
    private IGoal? _currentGoal;
    private int _consecutiveFailures;
    private FailureReason? _lastFailureReason;
    private bool _actionDispatchedThisCycle;
    // Sprint 20: inventory sum snapshot for progress-based stagnation detection.
    private int _cycleInventorySnapshot = -1;
    // Sprint 4a: connection status for SignalR push; D2: "reconnecting" broadcast
    private string _connectionStatus = "disconnected";

    // Sprint 12: tracks the name of the most-recently abandoned goal.
    private string? _lastAbandonedGoalName;

    // Sprint 13: tracks the goal name for which recovery was last attempted.
    private string? _lastRecoveredGoalName;

    // Sprint 15 P1: stall detection — tracks when an action was last dispatched.
    private DateTimeOffset _lastActionDispatchedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastStallWarnedAt = DateTimeOffset.MinValue;

    // Sprint 18: minimum replan interval guard — companion to MinReplanIntervalSeconds.
    private DateTimeOffset _lastReplanAt = DateTimeOffset.MinValue;

    // Sprint 23 P0-A: previous health for damage-delta computation.
    // -1 = uninitialized (first HealthEvent after SetGoal or startup sets the baseline without triggering an interrupt).
    private int _previousHealth = -1;
    // Sprint 23 P0-A: rate-limit timestamp for the damage interrupt path.
    private DateTimeOffset _lastDamageInterruptAt = DateTimeOffset.MinValue;
    // Sprint 23 P1-B: rate-limit timestamp for the passive health-critical GetStatus enqueue.
    // Updated both by the passive check and by the damage interrupt path (D-6 resolution).
    private DateTimeOffset _lastHealthStatusEnqueuedAt = DateTimeOffset.MinValue;

    // Sprint 37: tracks consecutive FindFlatArea scans that returned area=0.
    // Reset to 0 whenever a valid flat area (area >= MinUsableFlatArea) is found,
    // or when a new goal is set.
    private int _consecutiveZeroAreaScans;

    public WorldState WorldState => _worldState;
    public IGoal? CurrentGoal => _currentGoal;
    public int ConsecutiveFailures => _consecutiveFailures;

    // ── Public control API ────────────────────────────────────────────────────

    public void Enqueue(ActionData action) => _queue.Enqueue(action);

    public void SetGoal(IGoal goal)
    {
        // Sprint 18 note: SendEmergencyStop() is intentionally NOT called here.
        // SetGoal() is called both by user-goal-change paths AND directly from
        // TryCreateGoalFromChatAsync (new goal without explicit cancel). Calling stop
        // in SetGoal() interferes with integration tests that use adapter.SentActions
        // as a proxy signal. Emergency stop fires from CancelGoal() and
        // TryCompleteCurrentGoalFromWorldUpdate() where it is unambiguously correct.

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
        _lastActionDispatchedAt = _timeProvider.UtcNow;
        _lastStallWarnedAt = DateTimeOffset.MinValue;
        // Sprint 18: reset replan timer so the first plan fires without waiting.
        _lastReplanAt = DateTimeOffset.MinValue;
        // Sprint 19: reset governor for the new goal.
        replanGovernor?.Reset();
        // Sprint 20: reset inventory snapshot so the first cycle measures from scratch.
        _cycleInventorySnapshot = -1;
        // Sprint 21 P0-A: mark inventory as potentially stale so GenericGatherGoal.IsComplete
        // won't false-complete on stale inventory (e.g. after admin /clear). Cleared by
        // WorldStateProjector.ApplyStatus when a fresh StatusEvent arrives via GetStatus.
        _worldState = _worldState.With(b => b.SetInventoryStale(true));
        // Sprint 23 P0-A/P1-B: reset health-damage tracking for new goal.
        // D-7 resolution: -1 forces re-initialization on the first HealthEvent so we never
        // compute a spurious damage delta from stale inter-goal state.
        _previousHealth = -1;
        _lastDamageInterruptAt = DateTimeOffset.MinValue;
        _lastHealthStatusEnqueuedAt = DateTimeOffset.MinValue;
        lock (_pendingLock) _pendingActions.Clear();
        // Sprint 25 P0-D: clear correlation tracking for new goal.
        _correlatedActions.Clear();
        // Sprint 39 D-S38-01: clear cycle outcomes so the ILlmEvaluator only sees outcomes
        // from the active goal, not carry-over from a previous one.
        _cycleOutcomes.Clear();
        // Sprint 37: reset zero-area scan counter for new goal.
        _consecutiveZeroAreaScans = 0;
        // Sprint 40 P0-B: creative mode provisioning.
        // When the world is in creative mode and the goal requires items (IItemSpecGoal),
        // enqueue a /give command via Chat to provision the needed items instead of
        // relying on the removed IsCreativeMode auto-complete in GenericGatherGoal.
        if (_worldState.IsCreativeMode && goal is IItemSpecGoal itemGoal)
        {
            var have = _worldState.Inventory.GetValueOrDefault(itemGoal.Spec.ItemId);
            var need = Math.Max(0, itemGoal.TargetCount - have);
            if (need > 0)
            {
                var giveCmd = $"/give @p {itemGoal.Spec.ItemId} {need}";
                _queue.Enqueue(new ActionData
                {
                    Tool = "Chat",
                    Arguments = { ["message"] = giveCmd }
                });
                logger.LogInformation(
                    "[creative] provisioning {Need}x {Item} via '{GiveCmd}' at pos=({PosX},{PosY},{PosZ})",
                    need, itemGoal.Spec.ItemId, giveCmd,
                    _worldState.Position.X, _worldState.Position.Y, _worldState.Position.Z);
                _journal?.Log(new JournalEntry(
                    _timeProvider.UtcNow, JournalEntryType.ActionDispatched, "CreativeGive",
                    new Dictionary<string, object?>
                    {
                        ["item"] = itemGoal.Spec.ItemId,
                        ["count"] = need,
                    }));
                // Also enqueue a GetStatus so inventory is refreshed before planning
                _queue.Enqueue(new ActionData { Tool = "GetStatus" });
            }
        }

        // TSK-0021: report task-relevant inventory instead of generic top-5 summary.
        var taskInv = SummarizeTaskRelevantInventory(goal);
        logger.LogInformation("[goal] set: {Goal} — {Description} | inventory: [{Inventory}] pos=({PosX},{PosY},{PosZ})",
            goal.Name, goal.Description, taskInv,
            _worldState.Position.X, _worldState.Position.Y, _worldState.Position.Z);
        _journal?.Log(new JournalEntry(
            _timeProvider.UtcNow, JournalEntryType.GoalSet, goal.Name,
            new Dictionary<string, object?> { ["description"] = goal.Description }));
        // Sprint 4b: push goal change to dashboard
        _ = PushGoalToDashboardAsync();
    }

    public void CancelGoal()
    {
        // Sprint 18: abort in-progress Node.js action immediately on cancel.
        SendEmergencyStop();

        if (_currentGoal is not null)
            logger.LogInformation("[goal] cancelled: {Goal} | pending actions: {PendingCount}",
                _currentGoal.Name, _queue.Count);
        var previousGoalName = _currentGoal?.Name;
        _currentGoal = null;
        _journal?.Log(new JournalEntry(
            _timeProvider.UtcNow, JournalEntryType.GoalCancel, previousGoalName ?? "unknown"));
        _consecutiveFailures = 0;
        _lastRecoveredGoalName = null; // Sprint 13: reset recovery rate-limiter
        // Sprint 19: reset governor on cancel.
        replanGovernor?.Reset();
        _queue.Clear();
        lock (_pendingLock) _pendingActions.Clear();
        // Sprint 25 P0-D: clear correlation tracking on cancel.
        _correlatedActions.Clear();
        // Sprint 4b: push goal clear to dashboard
        _ = PushGoalToDashboardAsync();
    }

    public void SetBuildOrigin(string blueprintId, int x, int y, int z)
    {
        _worldState = _worldState.With(b =>
        {
            b.SetFact($"build:{blueprintId}:origin:x", x.ToString(), FactSource.Observed);
            b.SetFact($"build:{blueprintId}:origin:y", y.ToString(), FactSource.Observed);
            b.SetFact($"build:{blueprintId}:origin:z", z.ToString(), FactSource.Observed);
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
                    _timeProvider.UtcNow, JournalEntryType.AgentStarted, "Agent connected"));

                // Announce presence in Minecraft chat so players know the bot is online.
                _queue.Enqueue(new ActionData
                {
                    Tool = "Chat",
                    Arguments = { ["message"] = $"{botName} has connected to the server." }
                });

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
            _timeProvider.UtcNow, JournalEntryType.AgentStopped,
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

            // Sprint 23 P0-A: real-time damage interrupt.
            // Detect health drops by comparing against _previousHealth (set at end of each event).
            // On drop: synthesize DamageTakenEvent, apply to projector (stores facts), and attempt
            // interrupt if the active goal's DamageInterruptThresholdHp allows it.
            //
            // Sprint 23 P1-B: passive health-critical check with rate-limit guard (replaces the
            // Sprint 22 P0 unbounded GetStatus enqueue). Fires when health is below threshold
            // but no damage delta was detected (e.g. first StatusEvent after respawn shows low HP).
            var currentHealthNow = _worldState.Health;
            if (_previousHealth > 0 && currentHealthNow > 0 && currentHealthNow < _previousHealth)
            {
                // Health dropped — synthesize DamageTakenEvent for fact storage and interrupt.
                var delta = currentHealthNow - _previousHealth; // negative
                var damageTaken = new DamageTakenEvent(
                    PreviousHealth: _previousHealth,
                    Health: currentHealthNow,
                    Delta: delta,
                    Food: _worldState.Food,
                    Timestamp: _timeProvider.UtcNow);
                _worldState = _projector.Apply(_worldState, damageTaken);
                await TryInterruptOnDamageAsync(damageTaken, ct);
            }
            else if (currentHealthNow is > 0 and < HealthCriticalThreshold && _currentGoal is not null)
            {
                // Passive check: health below critical threshold without a detected drop.
                // Sprint 23 P1-B: rate-limit to avoid flooding the queue during continuous low-health.
                var elapsedPassive = _timeProvider.UtcNow - _lastHealthStatusEnqueuedAt;
                if (elapsedPassive.TotalSeconds >= HealthCheckCooldownSeconds)
                {
                    logger.LogInformation(
                        "[health] below critical threshold ({Health}/20) — queuing GetStatus (passive check)",
                        currentHealthNow);
                    _queue.Enqueue(new ActionData { Tool = "GetStatus" });
                    _lastHealthStatusEnqueuedAt = _timeProvider.UtcNow;
                }
            }

            // Update previous-health baseline for delta computation on the next event.
            // D-7 resolution: guard currentHealthNow > 0 so a death event (hp=0) does not
            // become the baseline — it would make a respawn look like +20 HP gain, never damage.
            if (currentHealthNow > 0)
                _previousHealth = currentHealthNow;

            switch (worldEvent)
            {
                case SpawnEvent:
                    logger.LogInformation("Bot spawned at {Pos}", _worldState.Position);
                    break;

                case StatusEvent:
                    // Sprint 25 P0-D: GetStatus/Status result received.
                    CompleteCorrelatedActionByTool("GetStatus");
                    CompleteCorrelatedActionByTool("Status");
                    break;

                case BlockMinedEvent e:
                    var itemKey = e.Block.Contains(':') ? e.Block.Split(':')[1] : e.Block;
                    // Sprint 40 P0-B: include position in block-mined log for debugging
                    logger.LogInformation(
                        "Inventory +{Count} {Block} -> total {Total} at ({PosX},{PosY},{PosZ})",
                        e.Count, itemKey, _worldState.Inventory.GetValueOrDefault(itemKey),
                        e.Pos.X, e.Pos.Y, e.Pos.Z);
                    // Sprint 37 (Issue A): a block was mined — inventory is no longer stale.
                    // This replaces the stale-flag-clearing role formerly served by GetStatus
                    // in the gather plan. Without this, GenericGatherGoal.IsComplete defers
                    // indefinitely when no GetStatus action is dispatched.
                    if (_worldState.IsInventoryStale)
                        _worldState = _worldState.With(b => b.SetInventoryStale(false));
                    // Sprint 37 (Issue B): complete MineBlock correlation on each mined block.
                    // Check for pending correlation BEFORE completing so the diagnostic is accurate.
                    var hadPendingMineBlock = _correlatedActions.Values.Any(a =>
                        a.State == ActionLifecycle.Dispatched &&
                        a.ToolName.Equals("MineBlock", StringComparison.OrdinalIgnoreCase));
                    CompleteCorrelatedActionByTool("MineBlock");
                    if (!hadPendingMineBlock)
                        logger.LogDebug(
                            "[correlation] BlockMinedEvent for {Block} arrived — no pending MineBlock " +
                            "(normal: already completed by previous block, or event for abandoned goal)",
                            itemKey);
                    break;

                case CraftCompleteEvent e:
                    logger.LogInformation("Crafted {Count}x {Item}", e.Count, e.Item);
                    CompleteCorrelatedActionByTool("CraftItem");
                    break;

                case SmeltCompleteEvent e:
                    logger.LogInformation("Smelted {Count}x {Input} -> {Output}", e.Count, e.Input, e.Result);
                    CompleteCorrelatedActionByTool("SmeltItem");
                    break;

                case ChatEvent:
                    // Sprint 1a: offload to ChatConsumerAsync — LLM call never blocks event loop
                    _chatChannel.Writer.TryWrite(worldEvent);
                    break;

                case FlatAreaFoundEvent ffa when ffa.Area >= MinUsableFlatArea:
                    _consecutiveZeroAreaScans = 0;
                    SetBuildOrigin(BuildFactKeys.AutoBlueprintId, ffa.X, ffa.Y, ffa.Z);
                    _journal?.Log(new JournalEntry(
                        _timeProvider.UtcNow, JournalEntryType.Observation, "FlatAreaFound",
                        new Dictionary<string, object?>
                        {
                            ["x"] = ffa.X,
                            ["y"] = ffa.Y,
                            ["z"] = ffa.Z,
                            ["area"] = ffa.Area,
                        }));
                    logger.LogInformation(
                        "[findFlatArea] auto-set build origin ({X},{Y},{Z}) area={Area}",
                        ffa.X, ffa.Y, ffa.Z, ffa.Area);
                    CompleteCorrelatedActionByTool("FindFlatArea");
                    break;

                case FlatAreaFoundEvent ffa:
                    _consecutiveZeroAreaScans++;
                    if (_consecutiveZeroAreaScans >= 2 && ffa.Area == 0)
                    {
                        // Sprint 37: after 2 consecutive scans with area=0, use bot's current
                        // position as fallback build origin. This prevents the scanner from
                        // expanding radius to find far-away locations when the bot can just
                        // build where it's standing.
                        var pos = _worldState.Position;
                        SetBuildOrigin(BuildFactKeys.AutoBlueprintId, pos.X, pos.Y - 1, pos.Z);
                        logger.LogWarning(
                            "[findFlatArea] 2 consecutive zero-area scans — falling back to bot position " +
                            "({X},{Y},{Z}) area=0 (fallback)",
                            pos.X, pos.Y - 1, pos.Z);
                        _journal?.Log(new JournalEntry(
                            _timeProvider.UtcNow, JournalEntryType.Observation, "FlatAreaFoundFallback",
                            new Dictionary<string, object?>
                            {
                                ["x"] = pos.X,
                                ["y"] = pos.Y - 1,
                                ["z"] = pos.Z,
                                ["area"] = 0,
                            }));
                    }
                    else
                    {
                        logger.LogInformation(
                            "[findFlatArea] scan area={Area} below minimum {Min} — auto-origin not updated (consecutiveZero={Count})",
                            ffa.Area, MinUsableFlatArea, _consecutiveZeroAreaScans);
                    }
                    CompleteCorrelatedActionByTool("FindFlatArea");
                    break;

                // Sprint 25 P0-D: result events that signal action completion.
                case MoveEvent:
                    CompleteCorrelatedActionByTool("MoveTo");
                    break;

                case WanderCompleteEvent:
                    CompleteCorrelatedActionByTool("Wander");
                    break;

                case WanderFailedEvent:
                    FailCorrelatedActionByTool("Wander");
                    break;

                // Sprint 40 P0-B: reachable block query result.
                case ReachableBlockFoundEvent:
                    CompleteCorrelatedActionByTool("FindReachableBlock");
                    break;

                default:
                    TryRouteAsError(worldEvent);
                    break;
            }

            TryCompleteCurrentGoalFromWorldUpdate();
            _ = PushStatusToDashboardAsync(CancellationToken.None);
        }
    }

    // ── Damage interrupt — Sprint 23 P0-A ────────────────────────────────────

    /// <summary>
    /// Attempts to trigger a real-time damage interrupt when the bot's health drops.
    ///
    /// Logic:
    /// 1. No active goal → skip (nothing to interrupt).
    /// 2. Goal's <see cref="IGoal.DamageInterruptThresholdHp"/> == 0 → suppress (combat mode).
    /// 3. Goal's threshold (or system default <see cref="HealthCriticalThreshold"/>) not exceeded → skip.
    /// 4. Rate-limit check: only fire once per <see cref="DamageInterruptCooldownSeconds"/>.
    /// 5. Interrupt: send emergency stop to Node.js + atomically clear queue + enqueue GetStatus.
    ///
    /// B-3 resolution: <see cref="ActionQueue.ClearAndEnqueue"/> is atomic via an internal lock.
    /// B-4 resolution: structured LogWarning on trigger, LogDebug on suppression.
    /// D-6 resolution: both <c>_lastDamageInterruptAt</c> and <c>_lastHealthStatusEnqueuedAt</c>
    ///   are updated so the passive health-check gate does not fire immediately after an interrupt.
    /// </summary>
    private async Task TryInterruptOnDamageAsync(DamageTakenEvent damage, CancellationToken ct = default)
    {
        if (_currentGoal is null) return;

        // B-2 resolution: use threshold from goal (null = system default, 0 = combat/never).
        var threshold = _currentGoal.DamageInterruptThresholdHp ?? HealthCriticalThreshold;
        if (threshold == 0)
        {
            logger.LogDebug(
                "[damage] goal '{Goal}' has DamageInterruptThresholdHp=0 (combat mode) — interrupt suppressed",
                _currentGoal.Name);
            return;
        }

        var damageMagnitude = Math.Abs(damage.Delta);
        if (damageMagnitude < threshold)
        {
            logger.LogDebug(
                "[damage] health drop magnitude {Magnitude} below threshold {Threshold} — interrupt not triggered",
                damageMagnitude, threshold);
            return;
        }

        // Rate-limit: suppress if an interrupt already fired within DamageInterruptCooldownSeconds.
        var elapsed = _timeProvider.UtcNow - _lastDamageInterruptAt;
        if (elapsed.TotalSeconds < DamageInterruptCooldownSeconds)
        {
            logger.LogDebug(
                "[damage] INTERRUPT suppressed by rate limit: prev={PrevHp} curr={CurrHp} delta={Delta} timeSinceLast={Elapsed:F1}s limitSeconds={Limit}",
                damage.PreviousHealth, damage.Health, damage.Delta,
                elapsed.TotalSeconds, DamageInterruptCooldownSeconds);
            return;
        }

        // Trigger interrupt — B-4 logging: structured warning with full context.
        var queueDepthBeforeClear = _queue.Count;
        logger.LogWarning(
            "[damage] INTERRUPT triggered: prev={PrevHp} curr={CurrHp} delta={Delta} goal='{Goal}' threshold={Threshold} queueDepthBeforeClear={QueueDepth}",
            damage.PreviousHealth, damage.Health, damage.Delta,
            _currentGoal.Name, threshold, queueDepthBeforeClear);

        // Sprint 36 P0-A: ClearAndEnqueueAsync sends stop BEFORE clearing the queue.
        // Previously SendEmergencyStop() was fire-and-forget — the JS adapter could receive
        // new GetStatus actions before the stop, leaving the old mine/wander loop running.
        // ClearAndEnqueueAsync awaits the stop callback before the lock-protected
        // clear+enqueue, ensuring JS receives the stop before any new actions are dispatched.
        await _queue.ClearAndEnqueueAsync(
            new ActionData { Tool = "GetStatus" },
            () => worldAdapter.SendActionAsync(
                new ActionData { Tool = EmergencyStopActionName }, CancellationToken.None));
        _lastDamageInterruptAt = _timeProvider.UtcNow;
        _lastHealthStatusEnqueuedAt = _timeProvider.UtcNow; // D-6: sync passive check gate

        _journal?.Log(new JournalEntry(
            _timeProvider.UtcNow, JournalEntryType.ActionFailed, "DamageInterrupt",
            new Dictionary<string, object?>
            {
                ["previousHealth"] = damage.PreviousHealth,
                ["health"]         = damage.Health,
                ["delta"]          = damage.Delta,
                ["goal"]           = _currentGoal.Name,
                ["threshold"]      = threshold,
            }));
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

    // Sprint 39 P1-C: HandleChatEventAsync now consumes IntentDraft? (was ChatInterpretation).
    // null = not addressed. Intent is a string field; goal creation routes through IntentManager.
    private async Task HandleChatEventAsync(WorldEvent worldEvent, CancellationToken ct)
    {
        if (chatInterpreter is null) return;
        if (worldEvent is not ChatEvent chat) return;

        logger.LogInformation("[chat] <{Username}> {Message}", chat.Username, chat.Message);
        _ = PushChatToDashboardAsync("player", chat.Username, chat.Message);
        _chatLogger?.LogInbound(chat.Username, chat.Message);

        using var thinkingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var thinkingTask = EnqueueThinkingIfSlowAsync(thinkingCts.Token);

        var intent = await chatInterpreter.InterpretAsync(
            chat.Username, chat.Message, botName, chat.OnlinePlayers,
            _worldState.Position, chat.PlayerPos, _worldState, ct);

        await thinkingCts.CancelAsync();
        try { await thinkingTask.ConfigureAwait(false); } catch (OperationCanceledException) { }

        // Sprint 11: log the resolved intent for visibility
        if (intent is not null)
            logger.LogInformation("[chat] <{Username}> -> {Intent}",
                chat.Username, intent.Intent);

        if (intent is null)
        {
            logger.LogDebug("[chat] not-addressed from <{Username}>: '{Snippet}'",
                chat.Username, chat.Message[..Math.Min(40, chat.Message.Length)]);
            return;
        }

        // Sprint 12: IMPORTANT — save the response BEFORE the switch.
        var pendingResponse = !string.IsNullOrEmpty(intent.Response)
            ? intent.Response : null;

        chatInterpreter.RecordBotSpoke();

        switch (intent.Intent.ToLowerInvariant())
        {
            case "cancel":
                CancelGoal();
                break;

            case "status" or "help":
                _worldState = _worldState.With(b =>
                    b.SetFact("currentGoal", _currentGoal?.Name ?? "idle", FactSource.Observed));
                if (pendingResponse is not null)
                    logger.LogInformation("[chat] bot: {Response}", pendingResponse);
                break;

            case "continue" or "resume":
                // Sprint 40 P0-B: Continue/resume intent — rehydrate stalled execution.
                // Clears the governor stall, refreshes inventory, and forces a replan.
                if (_currentGoal is not null)
                {
                    logger.LogInformation(
                        "[continue] rehydrating goal '{Goal}' — clearing governor stall, queueing GetStatus",
                        _currentGoal.Name);
                    replanGovernor?.Reset();
                    _lastReplanAt = DateTimeOffset.MinValue; // force immediate replan
                    _consecutiveFailures = 0;
                    _lastFailureReason = null;
                    _queue.Enqueue(new ActionData { Tool = "GetStatus" });
                    _journal?.Log(new JournalEntry(
                        _timeProvider.UtcNow, JournalEntryType.GoalSet, _currentGoal.Name,
                        new Dictionary<string, object?> { ["action"] = "continue" }));
                }
                else
                {
                    logger.LogInformation("[continue] no active goal to continue");
                }
                if (pendingResponse is not null)
                    logger.LogInformation("[chat] bot: {Response}", pendingResponse);
                break;

            case "gather" or "build" or "craft":
                if (pendingResponse is not null)
                    logger.LogInformation("[chat] bot: {Response}", pendingResponse);
                if (_intentManager is not null)
                {
                    var goalRequest = _intentManager.BuildGoalRequest(intent);
                    if (goalRequest is not null)
                        await TryCreateGoalFromChatAsync(goalRequest, ct);
                }
                break;

            case "navigate":
                if (pendingResponse is not null)
                    logger.LogInformation("[chat] bot: {Response}", pendingResponse);
                CancelGoal();

                if (pendingResponse is not null)
                {
                    _queue.Enqueue(new ActionData
                    {
                        Tool = "Chat",
                        Arguments = { ["message"] = pendingResponse }
                    });
                    _ = PushChatToDashboardAsync("bot", botName, pendingResponse);
                    pendingResponse = null;
                }

                // Explicit coords → MoveTo; null coords → follow player to their position
                if (intent.X is { } nx && intent.Y is { } ny && intent.Z is { } nz)
                {
                    _queue.Enqueue(new ActionData
                    {
                        Tool = "MoveTo",
                        Arguments = { ["x"] = nx, ["y"] = ny, ["z"] = nz }
                    });
                }
                else if (chat.PlayerPos is { } playerPos)
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

            case "conversation":
                logger.LogInformation("[chat] conversational response for <{Username}>: '{Response}'",
                    chat.Username, pendingResponse?.Length > 80 ? pendingResponse[..80] : pendingResponse);
                break;
            case "clarify":
                // Low-confidence — bot sends the clarification question (in pendingResponse)
                break;
        }

        if (pendingResponse is not null)
        {
            _queue.Enqueue(new ActionData
            {
                Tool = "Chat",
                Arguments = { ["message"] = pendingResponse }
            });
            _ = PushChatToDashboardAsync("bot", botName, pendingResponse);
        }
    }

    private void TryCompleteCurrentGoalFromWorldUpdate()
    {
        if (_currentGoal is null) return;
        if (!_currentGoal.IsComplete(_worldState)) return;

        // Sprint 37: diagnostic logging for premature goal completion via event path.
        // Sprint 40 P0-B: include position for debugging.
        if (_currentGoal is IItemSpecGoal isg)
            logger.LogInformation(
                "[goal] event-completed: {Goal} (gameMode={GameMode}, stale={Stale}, " +
                "inventory=[{Inventory}], target={Target}) pos=({PosX},{PosY},{PosZ})",
                _currentGoal.Name, _worldState.GameMode, _worldState.IsInventoryStale,
                SummarizeTaskRelevantInventory(_currentGoal), isg.TargetCount,
                _worldState.Position.X, _worldState.Position.Y, _worldState.Position.Z);

        logger.LogInformation("[goal] completed: {Goal} | inventory: [{Inventory}]",
            _currentGoal.Name, SummarizeTaskRelevantInventory(_currentGoal));
        _currentGoal = null;
        _consecutiveFailures = 0;
        _lastFailureReason = null;
        // Sprint 15 P1 (Sprint 13 D3): clear recovery rate-limiter so the next run
        // of the same goal can trigger recovery fresh if it fails again.
        _lastRecoveredGoalName = null;
        _queue.Clear();
        lock (_pendingLock) _pendingActions.Clear();
        // Sprint 25 P0-D: clear correlation tracking on goal completion.
        _correlatedActions.Clear();
        // Sprint 18: emit stop so Node.js also aborts the in-progress mine action.
        SendEmergencyStop();
        _ = PushGoalToDashboardAsync();
    }

    // Sprint 39 P1-C: accepts GoalRequest (was ChatInterpretation) — caller resolves via IntentManager.
    private async Task TryCreateGoalFromChatAsync(GoalRequest goalRequest, CancellationToken ct)
    {
        if (goalFactory is null) return;

        try
        {
            var goal = await goalFactory.CreateAsync(goalRequest.GoalName, goalRequest.Parameters, ct);
            if (goal is not null)
            {
                SetGoal(goal);
                logger.LogInformation("Chat created goal: {Goal}", goal.Name);
            }
            else
            {
                logger.LogWarning("Chat goal '{Name}' could not be created.", goalRequest.GoalName);
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
            logger.LogError(ex, "Error creating goal from chat: {Name}", goalRequest.GoalName);
        }
    }

    private void TryRouteAsError(WorldEvent worldEvent)
    {
        string? errMsg = null;
        if (worldEvent is BlockNotFoundEvent bnf && bnf.MinedCount == 0)
        {
            errMsg = $"blockNotFound:{bnf.Block}";
            logger.LogWarning("No {Block} found in range — will count as failure.", bnf.Block);
            // TSK-0021: track per-block failure count for progressive wander radius.
            var countKey = $"event:BlockNotFound:Count:{bnf.Block}";
            var prevCount = _worldState.Facts.TryGetValue(countKey, out var pc) && pc is int pci
                ? pci : 0;
            _worldState = _worldState.With(b =>
                b.SetFact(countKey, (prevCount + 1).ToString(), FactSource.Observed));
        }
        else if (worldEvent is ErrorEvent err)
        {
            errMsg = $"{err.Action}:{err.Message}";
            logger.LogWarning("Game error [{Action}]: {Message}", err.Action, err.Message);
        }
        if (errMsg is not null)
        {
            _gameErrors.Writer.TryWrite(errMsg);
            // Sprint 25 P0-D: error events indicate action failure — find and transition.
            // Sprint 36: map Node.js wire action names to C# tool names for proper
            // correlation. Node.js sends lowercase action names (e.g. "mine", "move")
            // while the C# tool registry uses PascalCase (e.g. "MineBlock", "MoveTo").
            if (worldEvent is BlockNotFoundEvent)
                FailCorrelatedActionByTool("MineBlock");
            else if (worldEvent is ErrorEvent errEv)
                FailCorrelatedActionByTool(MapNodeActionToToolName(errEv.Action));
        }
    }

    /// <summary>
    /// Maps Node.js wire-protocol action names to C# tool names for action correlation.
    /// Node.js sends lowercase action names in error events (e.g. "mine", "move"),
    /// but the C# tool registry and PendingAction store PascalCase names (e.g. "MineBlock", "MoveTo").
    /// </summary>
    private static string MapNodeActionToToolName(string nodeAction) => nodeAction.ToLowerInvariant() switch
    {
        "mine"               => "MineBlock",
        "move"               => "MoveTo",
        "place"              => "PlaceBlock",
        "wander"             => "Wander",
        "chat"               => "Chat",
        "findflatarea"       => "FindFlatArea",
        "findreachableblock" => "FindReachableBlock",
        "craft"              => "CraftItem",
        "smelt"              => "SmeltItem",
        "getstatus"          => "GetStatus",
        "status"             => "GetStatus",
        _                    => nodeAction, // fallback: pass through unchanged
    };

    // ── Action dispatch ────────────────────────────────────────────────────────

    private async Task DispatchActionsAsync(CancellationToken ct)
    {
        var maxFailures = maxConsecutiveFailures;
        var planContext = new Dictionary<string, object?>();

        while (!ct.IsCancellationRequested)
        {
            if (_queue.IsEmpty && _currentGoal is not null && !_actionDispatchedThisCycle)
            {
                   // Sprint 22 D-1: staleness debug log at IsComplete call site.
                // Fires only when stale (SetGoal → before first GetStatus arrives),
                // so this is infrequent and won't flood logs during normal operation.
                if (_worldState.IsInventoryStale)
                    logger.LogDebug(
                        "[goal] {Goal}: IsComplete deferred — inventory stale (awaiting GetStatus)",
                        _currentGoal.Name);

             if (_currentGoal.IsComplete(_worldState))
                {
                    // Sprint 37: diagnostic logging for gather goal completion — log game mode,
                    // stale flag, and actual inventory so we can debug why goals complete early.
                    if (_currentGoal is IItemSpecGoal && _worldState.IsInventoryStale)
                        logger.LogWarning(
                            "[goal] {Goal} completed while inventory STALE (gameMode={GameMode}, " +
                            "inventory=[{Inventory}]) — may be premature",
                            _currentGoal.Name, _worldState.GameMode,
                            SummarizeTaskRelevantInventory(_currentGoal));
                    else if (_currentGoal is IItemSpecGoal itemGoal2)
                        logger.LogInformation(
                            "[goal] {Goal} completed (gameMode={GameMode}, stale={Stale}, " +
                            "inventory=[{Inventory}], target={Target})",
                            _currentGoal.Name, _worldState.GameMode, _worldState.IsInventoryStale,
                            SummarizeTaskRelevantInventory(_currentGoal), itemGoal2.TargetCount);

                    logger.LogInformation("[goal] completed: {Goal} | inventory: [{Inventory}]",
                        _currentGoal.Name, SummarizeTaskRelevantInventory(_currentGoal));
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
                        _timeProvider.UtcNow, JournalEntryType.ReplanTriggered, _currentGoal.Name));
                    logger.LogWarning("[goal] failed: {Goal} (failures={FailureCount}, reason={Reason}) | inventory: [{Inventory}]",
                        _currentGoal.Name, _consecutiveFailures, reason, SummarizeTaskRelevantInventory(_currentGoal));
                    _currentGoal = null; _consecutiveFailures = 0; _lastFailureReason = null;
                    planContext.Clear();
                    lock (_pendingLock) _pendingActions.Clear();
                    continue;
                }

                // Sprint 21 P0-B: pre-plan governor check. When already STALLED, skip PlanAsync
                // entirely and wait 10s before rechecking. Previously PlanAsync was called every
                // 2s even during STALL (governor only checked *after* the plan was created), which
                // wasted planner CPU and produced misleading plan-sequence log lines.
                //
                // Sprint 40 P0-B: TryAutoRecover checks the 60s recovery timeout so the
                // governor can exit STALLED state even in the pre-plan check path. Previously
                // only Evaluate() could auto-recover, but it was never called during the
                // pre-plan stall check — causing the agent to stall indefinitely.
                if (replanGovernor?.IsStalled == true)
                {
                    // Try auto-recovery based on elapsed timeout
                    if (replanGovernor.TryAutoRecover())
                    {
                        logger.LogInformation(
                            "[governor] auto-recovered from STALLED after timeout — allowing replan");
                    }
                    else
                    {
                        _lastReplanAt = _timeProvider.UtcNow;
                        logger.LogDebug(
                            "[governor] STALLED — skipping PlanAsync, waiting 10s before retry check");
                        await Task.Delay(TimeSpan.FromSeconds(10), ct);
                        continue;
                    }
                }

                // Sprint 40 P0-B: stale-inventory pre-plan guard.
                // When inventory is stale for an inventory-dependent goal (IItemSpecGoal),
                // enqueue a GetStatus to refresh inventory before planning. Only enqueue
                // if no GetStatus is already in-flight. Does NOT apply to goals that don't
                // care about inventory (SimpleGoal, etc.).
                // IMPORTANT: Do NOT update _lastReplanAt here — this is not a replan event,
                // and updating it would trigger the 2-second MinReplanInterval delay.
                if (_currentGoal is IItemSpecGoal &&
                    _worldState.IsInventoryStale &&
                    !HasPendingActionOfTool("GetStatus") &&
                    !HasPendingActionOfTool("Status"))
                {
                    logger.LogInformation(
                        "[goal] {Goal}: inventory stale — deferring plan, queueing GetStatus | " +
                        "inventory: [{Inventory}] pos=({PosX},{PosY},{PosZ})",
                        _currentGoal.Name, SummarizeTaskRelevantInventory(_currentGoal),
                        _worldState.Position.X, _worldState.Position.Y, _worldState.Position.Z);
                    _queue.Enqueue(new ActionData { Tool = "GetStatus" });
                    await Task.Delay(100, ct);
                    continue;
                }

                // Sprint 18: minimum replan interval — prevents replanning storm under the
                // fire-and-forget architecture where tools return in <1 ms after sending to Node.js.
                // Without this, the planner runs ~3x/second; with it, at most once per 2 seconds.
                if ((_timeProvider.UtcNow - _lastReplanAt) < TimeSpan.FromSeconds(MinReplanIntervalSeconds))
                {
                    await Task.Delay(50, ct);
                    continue;
                }

                try
                {
                    var plan = await planner.PlanAsync(_currentGoal, _worldState, ct);

                    // Sprint 19: governor evaluates plan fingerprint before enqueueing.
                    // Fingerprint = goal key + action type sequence (excludes parameters
                    // since Wander coordinates change each cycle).
                    var actionSequence = string.Join(",", plan.Actions.Select(a => a.Tool));
                    var fingerprint = $"{_currentGoal.Name}:{actionSequence}";
                    if (replanGovernor is not null)
                    {
                        var verdict = replanGovernor.Evaluate(fingerprint);
                        if (verdict == ReplanVerdict.Stalled)
                        {
                            _lastReplanAt = _timeProvider.UtcNow;
                            logger.LogWarning(
                                "[governor] STALLED: goal '{Goal}' — plan repeated with no inventory change (Σ={InvSum}). Auto-retry in {TimeoutSec}s.",
                                _currentGoal.Name, _worldState.Inventory.Values.Sum(), 60);
                            continue;
                        }
                    }

                    foreach (var planAction in plan.Actions)
                        foreach (var kv in planContext)
                            planAction.Context.TryAdd(kv.Key, kv.Value);
                    _queue.EnqueueAll(plan.Actions);
                    _actionDispatchedThisCycle = false;
                    // Sprint 38 P3: new plan generated — clear cycle outcomes accumulator so
                    // the ILlmEvaluator only sees outcomes from this plan cycle (Sprint 39).
                    _cycleOutcomes.Clear();
                    lock (_pendingLock)
                    {
                        _pendingActions.Clear();
                        _pendingActions.AddRange(plan.Actions);
                    }
                    _lastReplanAt = _timeProvider.UtcNow; // Sprint 18: record plan time
                    // Sprint 19: log action sequence for tracing plan structure
                    // TSK-0019: RLE-compress consecutive repeated actions (e.g. PlaceBlock×63)
                    var displaySequence = RleCompressActions(plan.Actions.Select(a => a.Tool));
                    logger.LogInformation(
                        "[plan] {Goal}: {ActionCount} actions [{ActionSequence}] | " +
                        "inventory: [{Inventory}] pos=({PosX},{PosY},{PosZ})",
                        _currentGoal.Name, plan.Actions.Count, displaySequence,
                        SummarizeTaskRelevantInventory(_currentGoal),
                        _worldState.Position.X, _worldState.Position.Y, _worldState.Position.Z);
                    _journal?.Log(new JournalEntry(
                        _timeProvider.UtcNow, JournalEntryType.PlanCreated, _currentGoal.Name,
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
                // Sprint 36: skip dispatching a MineBlock action when there is already
                // one in-flight (Dispatched state). Under fire-and-forget dispatch, the
                // C# planner replans every 2s and re-pushes the same MineBlock actions,
                // flooding the Node.js command queue faster than the bot can dig.
                // This prevents redundant commands while still allowing the in-flight
                // action to report progress via blockMined events.
                if (IsFireAndForgetTool(action.Tool) && HasPendingActionOfTool(action.Tool))
                {
                    logger.LogDebug("[dispatch] {Tool} skipped — already in-flight", action.Tool);
                    continue;
                }

                _actionDispatchedThisCycle = true;
                // Sprint 15 P1: record dispatch time for stall detection
                _lastActionDispatchedAt = _timeProvider.UtcNow;
                lock (_pendingLock)
                {
                    if (_pendingActions.Count > 0) _pendingActions.RemoveAt(0);
                }
                Guid correlationId = Guid.Empty;
                try
                {
                    var argsJson = JsonSerializer.Serialize(action.Arguments);
                    using var doc = JsonDocument.Parse(argsJson);

                    using var timeoutCts = new CancellationTokenSource(
                        TimeSpan.FromSeconds(DefaultActionTimeoutSeconds));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                        ct, timeoutCts.Token);

                    // Sprint 25 P0-D: generate correlationId for action lifecycle tracking.
                    correlationId = Guid.NewGuid();
                    action.Context["correlationId"] = correlationId.ToString();
                    var pending = new PendingAction(correlationId, action.Tool,
                        _timeProvider.UtcNow, ActionLifecycle.Dispatched);
                    _correlatedActions[correlationId] = pending;

                    // Sprint 19: log args at Debug level (file only) for diagnostics
                    logger.LogDebug("[dispatch] {Tool} args: {Args} correlationId={CorrelationId}",
                        action.Tool, argsJson, correlationId);
                    _journal?.Log(new JournalEntry(
                        _timeProvider.UtcNow, JournalEntryType.ActionDispatched, action.Tool,
                        new Dictionary<string, object?> { ["correlationId"] = correlationId.ToString() }));
                    var sw = Stopwatch.StartNew();
                    // Sprint 37 P0-B: use CallWithOutcomeAsync to get structured ActionOutcome.
                    // Sprint 38 P2: use _currentGoal?.Id (default Guid.Empty) for ActionOutcome tracking.
                    var (result, outcome) = await toolCaller.CallWithOutcomeAsync(
                        _currentGoal?.Id ?? Guid.Empty, action.Tool, doc.RootElement, linkedCts.Token);
                    sw.Stop();
                    // Sprint 37 P0-B: log structured outcome. Replaces the per-path ActionCompleted /
                    // ActionFailed journal entries below (now removed).
                    // Sprint 37 P2-B / Sprint 38 P3: accumulate outcomes per dispatch cycle for
                    // observation-driven replanning: Plan → Execute → ActionOutcome → LLM Evaluate → Replan?
                    _journal?.LogOutcome(outcome);
                    _cycleOutcomes.Enqueue(outcome);
                    // Sprint 39 P1: observation-driven replanning — evaluate accumulated outcomes after each dispatch.
                    if (_llmEvaluator is not null && _currentGoal is not null)
                    {
                        var evalSnapshot = _cycleOutcomes.ToArray();
                        bool shouldReplan;
                        try
                        {
                            shouldReplan = await _llmEvaluator.EvaluateAsync(
                                _currentGoal, evalSnapshot, _worldState, ct);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception evalEx)
                        {
                            logger.LogWarning(evalEx,
                                "[evaluator] threw during EvaluateAsync — skipping replan for goal {Goal}",
                                _currentGoal.Name);
                            shouldReplan = false;
                        }
                        if (shouldReplan)
                        {
                            logger.LogInformation(
                                "[evaluator] LLM recommends replan for goal {Goal} after {Count} outcomes — breaking action loop",
                                _currentGoal.Name, evalSnapshot.Length);
                            break; // exit action dispatch loop → outer while calls PlanAsync again
                        }
                    }
                    if (result.Success)
                    {
                        // Sprint 19: elevated to Info with timing for runtime visibility
                        // Sprint 40 P0-B: include bot position for debugging
                        logger.LogInformation(
                            "[action] {Tool} OK ({ElapsedMs}ms) pos=({PosX},{PosY},{PosZ})",
                            action.Tool, sw.ElapsedMilliseconds,
                            _worldState.Position.X, _worldState.Position.Y, _worldState.Position.Z);

                        // Sprint 36: log outbound chat to disk (opt-out).
                        if (action.Tool.Equals("Chat", StringComparison.OrdinalIgnoreCase)
                            && action.Arguments.TryGetValue("message", out var msg)
                            && msg is not null)
                        {
                            _chatLogger?.LogOutbound(botName, msg.ToString() ?? string.Empty, correlationId.ToString());
                        }
                        // Sprint 37 P0-B: ActionCompleted journal entry moved to _journal?.LogOutcome(outcome) above.
                        // Sprint 25 P0-D: For fire-and-forget tools, CallAsync success means
                        // "dispatched to adapter" not "done". PendingAction stays in Dispatched
                        // state until a result event arrives in ProcessEventsAsync.
                        // Non-fire-and-forget tools (Chat, SearchMemory, GetPage, CreatePage)
                        // complete synchronously, so transition them immediately.
                        if (!IsFireAndForgetTool(action.Tool))
                            TransitionCorrelatedAction(correlationId, ActionLifecycle.Completed);
                        if (IsProgressSignalTool(action.Tool))
                        {
                            _consecutiveFailures = 0;
                            _lastFailureReason = null;
                            // Sprint 20: RecordProgress() moved to cycle-settle below.
                            // Per-tool calls caused 0ms fire-and-forget tools to mask stagnation.
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
                            _worldState = _worldState.With(b => b.SetFact(
                                progressFactKey,
                                bpIdx?.ToString() ?? string.Empty,
                                FactSource.Observed));
                            logger.LogDebug("[build] checkpoint: {Blueprint} block {Index} placed",
                                bpId, bpIdx);
                        }
                    }
                    else
                    {
                        logger.LogWarning(
                            "[action] {Tool} FAIL ({ElapsedMs}ms): {Message} pos=({PosX},{PosY},{PosZ})",
                            action.Tool, sw.ElapsedMilliseconds, result.Message,
                            _worldState.Position.X, _worldState.Position.Y, _worldState.Position.Z);
                        // Sprint 37 P0-B: ActionFailed journal entry moved to _journal?.LogOutcome(outcome) above.
                        if (!IsNonGoalFailureAction(action.Tool))
                        {
                            _consecutiveFailures++;
                            _lastFailureReason ??= MapErrorToFailureReason(result.Message);
                        }
                        // Sprint 25 P0-D: tool returned failure result — transition immediately.
                        TransitionCorrelatedAction(correlationId, ActionLifecycle.Failed);
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
                        _timeProvider.UtcNow, JournalEntryType.ActionFailed, action.Tool,
                        new Dictionary<string, object?> { ["error"] = "timed out" }));
                    if (!IsNonGoalFailureAction(action.Tool))
                    {
                        _consecutiveFailures++;
                        _lastFailureReason = FailureReason.ToolTimeout;
                    }
                    // Sprint 25 P0-D: timeout — transition to TimedOut.
                    TransitionCorrelatedAction(correlationId, ActionLifecycle.TimedOut);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Exception dispatching tool {Tool}", action.Tool);
                    _journal?.Log(new JournalEntry(
                        _timeProvider.UtcNow, JournalEntryType.ActionFailed, action.Tool,
                        new Dictionary<string, object?> { ["error"] = ex.Message }));
                    if (!IsNonGoalFailureAction(action.Tool))
                    {
                        _consecutiveFailures++;
                        _lastFailureReason ??= FailureReason.Unknown;
                    }
                    // Sprint 25 P0-D: exception — transition to Failed.
                    TransitionCorrelatedAction(correlationId, ActionLifecycle.Failed);
                }
            }
            else
            {
                if (_actionDispatchedThisCycle)
                {
                    logger.LogDebug("Plan cycle complete — settling for 300 ms");
                    await Task.Delay(300, ct);
                    _actionDispatchedThisCycle = false;
                    // Sprint 20: compare inventory sum before/after cycle to detect real game progress.
                    // blockMined events have up to 300ms to arrive before this check.
                    var currentInventorySum = _worldState.Inventory.Values.Sum();
                    if (_cycleInventorySnapshot >= 0 && currentInventorySum != _cycleInventorySnapshot)
                    {
                        replanGovernor?.RecordProgress();
                        logger.LogInformation(
                            "[governor] progress detected — inventory Σ {Before}→{After} " +
                            "(stagnation counter reset) pos=({PosX},{PosY},{PosZ})",
                            _cycleInventorySnapshot, currentInventorySum,
                            _worldState.Position.X, _worldState.Position.Y, _worldState.Position.Z);
                    }
                    _cycleInventorySnapshot = currentInventorySum;
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
                        var stalledFor = _timeProvider.UtcNow - _lastActionDispatchedAt;
                        if (stalledFor.TotalSeconds > StallWarningSeconds &&
                            (_timeProvider.UtcNow - _lastStallWarnedAt).TotalSeconds > StallWarningSuppressSeconds)
                        {
                            _lastStallWarnedAt = _timeProvider.UtcNow;
                            logger.LogWarning(
                                "[stall] No action dispatched in {Elapsed:N0}s with active goal '{Goal}' — agent may be stuck.",
                                (int)stalledFor.TotalSeconds, _currentGoal.Name);
                        }
                    }

                    // Sprint 25 P0-D: sweep for timed-out PendingActions.
                    // Any PendingAction in Dispatched state older than DefaultActionTimeoutSeconds
                    // is transitioned to TimedOut. This catches orphaned actions when the adapter
                    // crashes or events are lost.
                    SweepTimedOutActions();
                }
                else
                    await Task.Delay(50, ct);
            }
        }
    }


    // ── Sprint 25 P0-D: Action correlation helpers ────────────────────────────

    /// <summary>
    /// Transitions a correlated action to a new lifecycle state.
    /// No-op if the correlationId is not found (idempotent).
    /// </summary>
    private void TransitionCorrelatedAction(Guid correlationId, ActionLifecycle newState)
    {
        // BLK-1 fix (Sprint 25 council): CAS loop for atomic read-modify-write.
        // ProcessEventsAsync and DispatchActionsAsync run concurrently, so a naive
        // TryGetValue + indexed assignment is a race. TryUpdate atomically replaces the
        // value only if it matches the comparison value (record value-equality).
        while (_correlatedActions.TryGetValue(correlationId, out var current))
        {
            var updated = current.WithState(newState);
            if (_correlatedActions.TryUpdate(correlationId, updated, current))
            {
                logger.LogDebug("[correlation] {Tool} {Id} -> {State}",
                    current.ToolName, correlationId.ToString()[..8], newState);
                return;
            }
            // Another thread updated first — spin and retry with the fresh value.
        }
        // Key not present — no-op (action already removed by goal reset or cancel).
    }

    /// <summary>
    /// Finds the most recent PendingAction in Dispatched state matching the given tool name
    /// and transitions it to Completed. Used by ProcessEventsAsync when a result event arrives.
    /// </summary>
    private void CompleteCorrelatedActionByTool(string toolName)
    {
        foreach (var kv in _correlatedActions)
        {
            if (kv.Value.State == ActionLifecycle.Dispatched &&
                kv.Value.ToolName.Equals(toolName, StringComparison.OrdinalIgnoreCase))
            {
                TransitionCorrelatedAction(kv.Key, ActionLifecycle.Completed);
                return; // only transition the first match
            }
        }
    }

    /// <summary>
    /// Returns true when there is at least one PendingAction in Dispatched state
    /// matching the given tool name. Used by the dispatch loop to skip redundant
    /// MineBlock/GetStatus commands that are already in-flight on the Node.js side.
    /// </summary>
    private bool HasPendingActionOfTool(string toolName)
    {
        foreach (var kv in _correlatedActions)
        {
            if (kv.Value.State == ActionLifecycle.Dispatched &&
                kv.Value.ToolName.Equals(toolName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Finds the most recent PendingAction in Dispatched state matching the given tool name
    /// and transitions it to Failed.
    /// </summary>
    private void FailCorrelatedActionByTool(string toolName)
    {
        foreach (var kv in _correlatedActions)
        {
            if (kv.Value.State == ActionLifecycle.Dispatched &&
                kv.Value.ToolName.Equals(toolName, StringComparison.OrdinalIgnoreCase))
            {
                TransitionCorrelatedAction(kv.Key, ActionLifecycle.Failed);
                return;
            }
        }
    }

    /// <summary>
    /// Sweeps all PendingActions in Dispatched state that have exceeded the action timeout.
    /// Transitions them to TimedOut and logs a warning. Called from the idle branch of
    /// DispatchActionsAsync to catch orphaned actions when the adapter crashes or events are lost.
    /// </summary>
    private void SweepTimedOutActions()
    {
        var cutoff = _timeProvider.UtcNow.AddSeconds(-DefaultActionTimeoutSeconds);
        foreach (var kv in _correlatedActions)
        {
            if (kv.Value.State == ActionLifecycle.Dispatched && kv.Value.DispatchedAt < cutoff)
            {
                var timedOut = kv.Value.WithState(ActionLifecycle.TimedOut);
                // BLK-1 fix: use TryUpdate so a concurrent result-event transition
                // (Dispatched → Completed) wins over the sweep (Dispatched → TimedOut).
                if (_correlatedActions.TryUpdate(kv.Key, timedOut, kv.Value))
                {
                    logger.LogWarning(
                        "[correlation] {Tool} {Id} TIMED OUT after {Elapsed}s — no result event received",
                        kv.Value.ToolName, kv.Key.ToString()[..8],
                        (_timeProvider.UtcNow - kv.Value.DispatchedAt).TotalSeconds);
                }
                // If TryUpdate returns false, another thread already transitioned the action
                // (e.g., result event arrived just before the sweep) — that's correct behavior.
            }
        }
    }

    /// <summary>
    /// Returns true for tools that dispatch fire-and-forget to the Node.js adapter
    /// and receive their result asynchronously via world events.
    /// False for tools that complete synchronously within CallAsync (Chat, SearchMemory, etc.).
    /// </summary>
    private static bool IsFireAndForgetTool(string toolName) =>
        toolName.Equals("MoveTo", StringComparison.OrdinalIgnoreCase)
        || toolName.Equals("MineBlock", StringComparison.OrdinalIgnoreCase)
        || toolName.Equals("PlaceBlock", StringComparison.OrdinalIgnoreCase)
        || toolName.Equals("GetStatus", StringComparison.OrdinalIgnoreCase)
        || toolName.Equals("Status", StringComparison.OrdinalIgnoreCase)
        || toolName.Equals("Wander", StringComparison.OrdinalIgnoreCase)
        || toolName.Equals("CraftItem", StringComparison.OrdinalIgnoreCase)
        || toolName.Equals("SmeltItem", StringComparison.OrdinalIgnoreCase)
        || toolName.Equals("FindFlatArea", StringComparison.OrdinalIgnoreCase)
        // Sprint 40 P0-B: reachable block query is fire-and-forget (result arrives via event).
        || toolName.Equals("FindReachableBlock", StringComparison.OrdinalIgnoreCase);

    // ── Inventory summary (Sprint 19) ────────────────────────────────────────

    /// <summary>
    /// Returns a concise inventory summary string for log messages.
    /// Shows the top <paramref name="maxItems"/> items by count.
    /// </summary>
    private string SummarizeInventory(int maxItems = 5)
    {
        if (_worldState.Inventory.Count == 0) return "empty";
        return string.Join(", ", _worldState.Inventory
            .OrderByDescending(kv => kv.Value)
            .Take(maxItems)
            .Select(kv => $"{kv.Value}x {kv.Key}"));
    }

    // ── Plan display RLE (TSK-0019) ───────────────────────────────────────────

    /// <summary>
    /// Compresses consecutive repeated action names into "ActionName×N" format
    /// for compact plan display. E.g. "PlaceBlock → PlaceBlock → PlaceBlock"
    /// becomes "PlaceBlock×3".
    /// </summary>
    private static string RleCompressActions(IEnumerable<string> tools)
    {
        var sb = new System.Text.StringBuilder();
        string? current = null;
        int count = 0;
        foreach (var tool in tools)
        {
            if (tool == current)
            {
                count++;
            }
            else
            {
                if (current is not null)
                {
                    if (sb.Length > 0) sb.Append(" → ");
                    sb.Append(current);
                    if (count > 1) sb.Append('×').Append(count);
                }
                current = tool;
                count = 1;
            }
        }
        if (current is not null)
        {
            if (sb.Length > 0) sb.Append(" → ");
            sb.Append(current);
            if (count > 1) sb.Append('×').Append(count);
        }
        return sb.ToString();
    }

    // ── Task-relevant inventory summary (TSK-0021) ────────────────────────────

    /// <summary>
    /// Returns an inventory summary focused on items relevant to the current goal.
    /// For BuildGoal: shows how many of each blueprint material are already in inventory.
    /// For GenericGatherGoal: shows current count of the target item.
    /// For CraftItemGoal: shows the item being crafted and its prerequisites.
    /// Fallback: uses the generic <see cref="SummarizeInventory"/>.
    /// </summary>
    private string SummarizeTaskRelevantInventory(IGoal goal)
    {
        if (goal is BuildGoal bg)
        {
            var parts = new List<string>();
            foreach (var mat in bg.Blueprint.Materials.OrderByDescending(m => m.Quantity))
            {
                var have = _worldState.Inventory.GetValueOrDefault(mat.Block);
                parts.Add($"{mat.Block}: {have}/{mat.Quantity}");
            }
            return parts.Count > 0 ? string.Join(", ", parts) : "empty";
        }

        if (goal is IItemSpecGoal itemGoal)
        {
            var spec = itemGoal.Spec;
            var total = 0;
            foreach (var block in spec.SourceBlocks)
            {
                var colonIdx = block.IndexOf(':');
                var key = colonIdx >= 0 ? block[(colonIdx + 1)..] : block;
                total += _worldState.Inventory.GetValueOrDefault(key);
            }
            return $"{spec.ItemId}: {total}/{itemGoal.TargetCount}";
        }

        if (goal is CraftItemGoal ciGoal)
        {
            var have = _worldState.Inventory.GetValueOrDefault(ciGoal.ItemId);
            return $"{ciGoal.ItemId}: {have}/{ciGoal.Count}";
        }

        return _worldState.Inventory.Count == 0 ? "empty" : SummarizeInventory();
    }

    private static bool IsNonGoalFailureAction(string toolName) =>
        string.Equals(toolName, "Chat", StringComparison.OrdinalIgnoreCase);

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
                new ActionData { Tool = EmergencyStopActionName }, CancellationToken.None);
            logger.LogInformation("[stop] emergency stop dispatched to adapter via {Action}", EmergencyStopActionName);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[stop] failed to dispatch emergency stop (adapter may not be connected): {Error}", ex.Message);
        }
    }

    // ── Thinking indicator ─────────────────────────────────────────────────────
    // Sprint 34: increased delay to 5s to avoid sending chat indicators for
    // fast-path pattern matches and to prevent server anti-spam kicks
    // (multiplayer.disconnect.chat_validation_failed). Only one indicator per
    // interpretation cycle.

    private static readonly string[] _thinkingMessages =
        ["Hmm...", "...", "Let me think...", "*thinks*"];

    private async Task EnqueueThinkingIfSlowAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            var msg = _thinkingMessages[Random.Shared.Next(_thinkingMessages.Length)];
            _queue.Enqueue(new ActionData { Tool = "Chat", Arguments = { ["message"] = msg } });
            logger.LogInformation("[chat] thinking indicator sent ('{Msg}') — LLM response pending >5s", msg);
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
            _timeProvider.UtcNow, JournalEntryType.ErrorRecovery, errMsg,
            new Dictionary<string, object?> { ["goal"] = _currentGoal.Name }));

        try
        {
            // Sprint 19: refactored to use shared SummarizeInventory helper
            var invSummary = SummarizeTaskRelevantInventory(_currentGoal);

            var prompt =
                $"recover from runtime error while executing goal {_currentGoal.Name}: {errMsg}. " +
                $"Bot inventory: {invSummary}. " +
                "Available actions: gather, navigate, status, craft, smelt. " +
                "If gather target is unavailable, choose an alternative gather goal or navigate action. " +
                "If recipe is missing, check whether a crafting table or raw material is needed first.";

            // Sprint 39 P1-C: InterpretAsync now returns IntentDraft?; use IntentManager for goal mapping.
            var intent = await chatInterpreter.InterpretAsync(
                username: "system",
                message: prompt,
                botName: botName,
                onlinePlayers: 1,
                botPosition: _worldState.Position,
                playerPosition: _worldState.Position,
                state: _worldState,
                ct: ct);

            if (intent is not null
                && intent.Intent is "gather" or "craft" or "build"
                && _intentManager is not null)
            {
                var goalRequest = _intentManager.BuildGoalRequest(intent);
                if (goalRequest is not null)
                {
                    if (GoalNamesMatch(goalRequest.GoalName, _currentGoal?.Name))
                    {
                        logger.LogDebug(
                            "[recovery] LLM suggested current goal '{Goal}' — no change needed",
                            goalRequest.GoalName);
                        return;
                    }
                    if (GoalNamesMatch(goalRequest.GoalName, _lastAbandonedGoalName))
                    {
                        logger.LogWarning(
                            "[recovery] LLM suggested '{Goal}' — same item as recently abandoned; skipping to break loop",
                            goalRequest.GoalName);
                        return;
                    }

                    logger.LogInformation("Recovery interpreter suggested goal: {Goal}", goalRequest.GoalName);
                    await TryCreateGoalFromChatAsync(goalRequest, ct);
                }
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

    // Sprint 25 P0-D: log abandoned PendingActions on shutdown.
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        var abandoned = _correlatedActions.Values
            .Where(pa => pa.State == ActionLifecycle.Dispatched)
            .ToList();
        if (abandoned.Count > 0)
        {
            logger.LogWarning(
                "[shutdown] {Count} PendingAction(s) still in Dispatched state at shutdown:",
                abandoned.Count);
            foreach (var pa in abandoned)
            {
                logger.LogWarning(
                    "[shutdown]   {Tool} {Id} dispatched at {DispatchedAt} — abandoned",
                    pa.ToolName, pa.CorrelationId.ToString()[..8], pa.DispatchedAt);
            }
        }
        await base.StopAsync(cancellationToken);
    }
}
