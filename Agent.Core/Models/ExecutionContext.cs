namespace Agent.Core;

/// <summary>
/// Canonical runtime state object that carries goal state, world snapshot,
/// inventory, queue/progress, recovery context, and capabilities through
/// planning, dispatch, evaluation, and replanning.
///
/// Replaces the loose argument passing and repeated state derivation that
/// currently exists in AgentBackgroundService (30+ mutable fields).
///
/// Sprint 57: Introduced as the single synchronized runtime state per
/// the architecture hard requirements and council-approved direction.
/// </summary>
/// <param name="Goal">The active goal, or null when idle.</param>
/// <param name="State">Frozen world-state snapshot for this execution tick.</param>
/// <param name="QueueDepth">Number of pending actions in the dispatch queue.</param>
/// <param name="ConsecutiveFailures">Running count of consecutive action failures.</param>
/// <param name="LastFailureReason">Human-readable reason for the last failure, or null.</param>
/// <param name="Capabilities">What the agent can currently do (game mode, tools, etc.).</param>
/// <param name="RecoveryContext">Structured recovery state (last error, attempt count, cooldown).</param>
public sealed record ExecutionContext(
    IGoal? Goal,
    WorldState State,
    int QueueDepth,
    int ConsecutiveFailures,
    string? LastFailureReason,
    ExecutionCapabilities Capabilities,
    RecoveryContext RecoveryContext)
{
    /// <summary>Convenience: true when there is no active goal.</summary>
    public bool IsIdle => Goal is null;

    /// <summary>Convenience: the active goal's name, or "idle".</summary>
    public string GoalName => Goal?.Name ?? "(idle)";

    /// <summary>True when the state snapshot has fresh inventory.</summary>
    public bool HasFreshInventory => !State.IsInventoryStale;

    /// <summary>Creates an idle context with default state and capabilities.</summary>
    public static ExecutionContext Idle(WorldState state, ExecutionCapabilities capabilities) =>
        new(null, state, 0, 0, null, capabilities, RecoveryContext.None);

    /// <summary>Creates a context for a newly-activated goal.</summary>
    public static ExecutionContext ForGoal(IGoal goal, WorldState state, ExecutionCapabilities capabilities) =>
        new(goal, state, 0, 0, null, capabilities, RecoveryContext.None);

    /// <summary>Returns a copy with the given goal (null = cleared).</summary>
    public ExecutionContext WithGoal(IGoal? goal) =>
        this with { Goal = goal, ConsecutiveFailures = 0, LastFailureReason = null, RecoveryContext = RecoveryContext.None };

    /// <summary>Returns a copy with an incremented failure count and reason.</summary>
    public ExecutionContext WithFailure(string reason) =>
        this with { ConsecutiveFailures = ConsecutiveFailures + 1, LastFailureReason = reason };

    /// <summary>Returns a copy with the given world state snapshot.</summary>
    public ExecutionContext WithState(WorldState state) =>
        this with { State = state };

    /// <summary>Returns a copy with the given recovery context.</summary>
    public ExecutionContext WithRecovery(RecoveryContext rc) =>
        this with { RecoveryContext = rc };
}
