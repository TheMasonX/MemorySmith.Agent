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

    // CHANGED: include targetCount in key to prevent cross-goal collision
    public bool HasFailed(WorldState state) =>
        state.Facts.TryGetValue($"goal:{Name}:{targetCount}:failed", out var v) && v is true;
}
