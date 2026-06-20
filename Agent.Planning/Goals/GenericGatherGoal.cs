namespace Agent.Planning.Goals;

using Agent.Core;

/// <summary>
/// Gathers a target number of units of any item described by an <see cref="ItemSpec"/>.
/// [rest of existing XML doc]
/// </summary>
public sealed class GenericGatherGoal(ItemSpec item, int targetCount) : IGoal, IItemSpecGoal
{
    public ItemSpec Spec => item;
    public int TargetCount => targetCount;
    public string Name => $"Gather:{item.ItemId}";
    public string Description => $"Gather at least {targetCount} {item.DisplayName}.";
    public string[] Phases => ["FindSource", "Mine", "Collect"];
    public string? FailureReason { get; set; }

    public bool IsComplete(WorldState state)
    {
        if (state.IsInventoryStale)
            return false;

        if (item.RequiresSmelting)
            return state.Inventory.GetValueOrDefault(item.ItemId) >= targetCount;

        int total = 0;
        foreach (var block in item.SourceBlocks)
        {
            var colonIdx = block.IndexOf(':');
            var key = colonIdx >= 0 ? block[(colonIdx + 1)..] : block;
            total += state.Inventory.GetValueOrDefault(key);
        }
        return total >= targetCount;
    }

    /// <summary>
    /// Returns true when the world-state fact
    /// <c>goal:Gather:{itemId}:{targetCount}:failed</c> is set to <c>true</c>.
    ///
    /// Sprint 28 P0-C: key includes targetCount to prevent cross-goal collision
    /// between gather-N and gather-M for the same item.
    ///
    /// Sprint 30 P2-D (DEF-DOC-1): the fact key format is documented here so callers
    /// adding a write site can find the expected format without checking commit history.
    /// Key format: <c>goal:Gather:{itemId}:{targetCount}:failed</c>
    ///
    /// Sprint 30 P2-B (DEF-DOC-3): this fact is ONLY READ in the current production path
    /// — <see cref="AgentBackgroundService"/> tracks failures via a consecutive-failure
    /// counter, not a world-state fact. <see cref="HasFailed"/> always returns false
    /// until a write site is added using the exact key format documented above.
    /// </summary>
    // CHANGED: include targetCount in key to prevent cross-goal collision
    public bool HasFailed(WorldState state) =>
        state.Facts.TryGetValue($"goal:{Name}:{targetCount}:failed", out var v) && v is true;
}
