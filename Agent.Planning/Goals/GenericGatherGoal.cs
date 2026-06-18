namespace Agent.Planning.Goals;

using Agent.Core;

/// <summary>
/// Gathers a target number of units of any item described by an <see cref="ItemSpec"/>.
///
/// Replaces the hard-coded <see cref="GatherWoodGoal"/> logic for all vanilla and modded
/// gather tasks. <see cref="GatherWoodGoal"/> continues to exist as a backward-compatible
/// factory entry (see GoalFactory / TSK-0010 design doc).
///
/// Implements <see cref="IItemSpecGoal"/> so the planner can dispatch to the correct
/// decomposer via a single interface check rather than a concrete type check (D2, TSK-0011).
///
/// Phases: FindSource → Mine → Collect
///   FindSource — search MemorySmith wiki for known source locations.
///   Mine       — mine the source blocks listed in <see cref="ItemSpec.SourceBlocks"/>.
///   Collect    — confirm status and gather drops.
///
/// IsComplete behaviour:
///   <see cref="ItemSpec.RequiresSmelting"/> = false:
///     sum of all SourceBlock inventory entries ≥ targetCount.
///   <see cref="ItemSpec.RequiresSmelting"/> = true:
///     inventory[ItemId] ≥ targetCount (checks the smelted product).
///     NOTE: Phase 4a does not drive the smelting chain — the bot can only
///     complete this goal if the smelted product is already in inventory.
///     Full smelting via FurnaceTool is Phase 4b.
///
/// HasFailed: world-state fact "goal:Gather:{ItemId}:failed" = true
///   (set by AgentBackgroundService after consecutive tool failures).
/// </summary>
public sealed class GenericGatherGoal(ItemSpec item, int targetCount) : IGoal, IItemSpecGoal
{
    /// <summary>The ItemSpec that describes what to gather and how.</summary>
    public ItemSpec Spec => item;

    /// <summary>
    /// The target quantity to gather.
    /// Sprint 18: exposed as a public property so <see cref="GatherGoalDecomposer"/>
    /// can pass the correct count to <c>HtnTaskLibrary.GatherItemDecompose</c> rather
    /// than always defaulting to 10 (the bug that caused "get 1 dirt" to mine 10).
    /// </summary>
    public int TargetCount => targetCount;

    /// <inheritdoc/>
    public string Name => $"Gather:{item.ItemId}";

    /// <inheritdoc/>
    public string Description => $"Gather at least {targetCount} {item.DisplayName}.";

    /// <inheritdoc/>
    public string[] Phases => ["FindSource", "Mine", "Collect"];

    /// <inheritdoc/>
    public string? FailureReason { get; set; }

    /// <inheritdoc/>
    public bool IsComplete(WorldState state)
    {
        if (item.RequiresSmelting)
        {
            // Check for the smelted product in inventory.
            return state.Inventory.GetValueOrDefault(item.ItemId) >= targetCount;
        }

        // Sum all matching source-block inventory keys.
        // Handles items with multiple block variants (oak_log, birch_log, spruce_log...).
        // Strip any "minecraft:" namespace prefix before looking up inventory keys,
        // since inventory entries are stored without the namespace.
        int total = 0;
        foreach (var block in item.SourceBlocks)
        {
            var colonIdx = block.IndexOf(':');
            var key = colonIdx >= 0 ? block[(colonIdx + 1)..] : block;
            total += state.Inventory.GetValueOrDefault(key);
        }

        return total >= targetCount;
    }

    /// <inheritdoc/>
    public bool HasFailed(WorldState state) =>
        state.Facts.TryGetValue($"goal:{Name}:failed", out var v) && v is true;
}
