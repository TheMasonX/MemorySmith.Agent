namespace Agent.Core;

/// <summary>
/// Sprint 40 P0-B: Rich outcome status replacing the bare <c>bool Success</c>.
/// Provides semantic detail about WHY an action succeeded or failed,
/// enabling the LLM evaluator and replan governor to make better decisions.
/// </summary>
public enum OutcomeType
{
    /// <summary>Action completed successfully and produced the expected result.</summary>
    Completed,

    /// <summary>Action completed but produced NO measurable progress (block mined but item
    /// not collected, crafted but wrong output, etc.). Distinct from Failed — the tool
    /// call itself succeeded, but the intended outcome wasn't achieved.</summary>
    NoProgress,

    /// <summary>Action failed due to an error or exception.</summary>
    Failed,

    /// <summary>Action could not proceed because a prerequisite was not met
    /// (no reachable block, missing tool, insufficient items).</summary>
    Blocked,

    /// <summary>Target was unreachable — pathfinding could not find a route.</summary>
    Unreachable,

    /// <summary>Action timed out before completing.</summary>
    TimedOut,
}

/// <summary>
/// Sprint 36 P2-B stub: contract for passing structured observation context
/// to the LLM evaluator. Full implementation in Sprint 37 when ActionOutcome
/// is wired into the DispatchActionsAsync observation-driven replanning loop.
/// Consumes: ActionOutcome.ObservationSummary as the default Summary text.
/// </summary>
public interface IObservationSummary
{
    string Summary { get; }
}

/// <summary>
/// A typed, structured effect produced by a single tool execution.
/// Used by <see cref="ActionOutcome.Effects"/> to describe what changed in the world.
///
/// Type vocabulary (Sprint 35 initial set; Sprint 36 expands):
///   ItemCollected    — item landed in inventory (from playerCollect or craft/smelt output)
///   ItemConsumed     — item removed from inventory (placed, used as ingredient)
///   ItemCrafted       — item crafted at table/furnace (Sprint 36)
///   PositionChanged    — bot moved to a new location
///   BlockPlaced        — block placed in the world
///   BlockMined        — block removed from the world
///   StatusRefreshed   — full bot status refreshed (HP, food, inventory)
///   MemorySearched    — MemorySmith search executed
///   MemoryPageCreated — new MemorySmith page created
/// </summary>
public record StructuredEffect(
    string Type,
    string? Item = null,
    int? Count = null,
    string? Detail = null);

/// <summary>
/// Universal result artifact for every <see cref="Agent.Tools.ToolDispatcher.CallAsync"/> execution.
///
/// Sprint 35: introduces ActionOutcome as the shared artifact consumed by:
///   - AgentJournal (records what happened)
///   - AgentBackgroundService (WorldState update hints)
/// Sprint 36: LLM evaluation loop receives ActionOutcome[] as observation context:
///   Plan → Execute → ActionOutcome → LLM Evaluate → Replan? → Execute
/// This is the transition from command-driven bot to genuine agent runtime.
///
/// Sprint 37 P0-A: ActionOutcome now implements IObservationSummary so it can be
/// passed directly to any consumer that expects structured observation context
/// (e.g. the LLM evaluator in the observation-driven replanning loop).
///
/// Sprint 40 P0-B: Replaced <c>bool Success</c> with <see cref="OutcomeType"/> enum
/// providing rich status (Completed, NoProgress, Failed, Blocked, Unreachable, TimedOut).
/// Factory helpers updated to set the appropriate OutcomeType.
/// <c>Success</c> property is now computed: true for Completed only.
///
/// Example for MineBlock(oak_log, 5) success:
///   GoalId       = &lt;active goal GUID&gt;
///   ToolName     = "MineBlock"
///   Outcome      = OutcomeType.Completed
///   Summary      = "Mined 5 oak_log at (100,64,200)"
///   Effects      = [ {ItemCollected, oak_log, 5}, {PositionChanged} ]
/// </summary>
public record ActionOutcome(
    Guid GoalId,
    string ToolName,
    OutcomeType Outcome,
    string ObservationSummary,
    IReadOnlyList<StructuredEffect> Effects,
    DateTimeOffset Timestamp) : IObservationSummary
{
    /// <summary>Convenience: true when Outcome is Completed.</summary>
    public bool Success => Outcome == OutcomeType.Completed;

    /// <summary>
    /// Sprint 37 P0-A: Implements IObservationSummary.Summary.
    /// Maps ObservationSummary → Summary so ActionOutcome can be passed directly
    /// to any IObservationSummary consumer (e.g. the LLM evaluator).
    /// </summary>
    string IObservationSummary.Summary => ObservationSummary;

    /// <summary>Creates a completed outcome with a single ItemCollected effect.</summary>
    public static ActionOutcome Collected(Guid goalId, string tool, string item, int count) =>
        new(goalId, tool, OutcomeType.Completed,
            $"Collected {count}x {item}",
            [new StructuredEffect("ItemCollected", item, count)],
            DateTimeOffset.UtcNow);

    /// <summary>Creates a failure outcome with the given error message.</summary>
    public static ActionOutcome Failed(Guid goalId, string tool, string reason) =>
        new(goalId, tool, OutcomeType.Failed, reason, [], DateTimeOffset.UtcNow);

    /// <summary>Creates a simple success outcome with no structured effects.</summary>
    public static ActionOutcome Succeeded(Guid goalId, string tool, string summary) =>
        new(goalId, tool, OutcomeType.Completed, summary, [], DateTimeOffset.UtcNow);

    /// <summary>Creates a NoProgress outcome — tool call succeeded but no progress made.</summary>
    public static ActionOutcome NoProgress(Guid goalId, string tool, string detail) =>
        new(goalId, tool, OutcomeType.NoProgress, detail, [], DateTimeOffset.UtcNow);

    /// <summary>Creates a Blocked outcome — prerequisite not met.</summary>
    public static ActionOutcome Blocked(Guid goalId, string tool, string reason) =>
        new(goalId, tool, OutcomeType.Blocked, reason, [], DateTimeOffset.UtcNow);

    /// <summary>Creates an Unreachable outcome — target not reachable.</summary>
    public static ActionOutcome Unreachable(Guid goalId, string tool, string detail) =>
        new(goalId, tool, OutcomeType.Unreachable, detail, [], DateTimeOffset.UtcNow);

    /// <summary>Creates a TimedOut outcome.</summary>
    public static ActionOutcome TimedOut(Guid goalId, string tool, string detail) =>
        new(goalId, tool, OutcomeType.TimedOut, detail, [], DateTimeOffset.UtcNow);
}
