namespace Agent.Core;

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
/// Example for MineBlock(oak_log, 5) success:
///   GoalId       = &lt;active goal GUID&gt;
///   ToolName     = "MineBlock"
///   Success      = true
///   Summary      = "Mined 5 oak_log at (100,64,200)"
///   Effects      = [ {ItemCollected, oak_log, 5}, {PositionChanged} ]
/// </summary>
public record ActionOutcome(
    Guid GoalId,
    string ToolName,
    bool Success,
    string ObservationSummary,
    IReadOnlyList<StructuredEffect> Effects,
    DateTimeOffset Timestamp) : IObservationSummary
{
    /// <summary>
    /// Sprint 37 P0-A: Implements IObservationSummary.Summary.
    /// Maps ObservationSummary → Summary so ActionOutcome can be passed directly
    /// to any IObservationSummary consumer (e.g. the LLM evaluator).
    /// </summary>
    string IObservationSummary.Summary => ObservationSummary;

    /// <summary>Creates a successful outcome with a single ItemCollected effect.</summary>
    public static ActionOutcome Collected(Guid goalId, string tool, string item, int count) =>
        new(goalId, tool, true,
            $"Collected {count}x {item}",
            [new StructuredEffect("ItemCollected", item, count)],
            DateTimeOffset.UtcNow);

    /// <summary>Creates a failure outcome with the given error message.</summary>
    public static ActionOutcome Failed(Guid goalId, string tool, string reason) =>
        new(goalId, tool, false, reason, [], DateTimeOffset.UtcNow);

    /// <summary>Creates a simple success outcome with no structured effects.</summary>
    public static ActionOutcome Succeeded(Guid goalId, string tool, string summary) =>
        new(goalId, tool, true, summary, [], DateTimeOffset.UtcNow);
}
