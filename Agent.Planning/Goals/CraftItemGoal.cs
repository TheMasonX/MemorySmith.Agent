namespace Agent.Planning.Goals;

using Agent.Core;

/// <summary>
/// Goal that instructs the bot to craft a specific item.
///
/// Sprint 13: added to give "craft an iron pickaxe" a concrete, plannable goal.
/// <see cref="HtnPlanner"/> routes this to <see cref="HtnTaskLibrary.DecomposeCraftItem"/>
/// which ensures a crafting table is available and emits a <c>CraftItem</c> tool call.
///
/// If the required materials are absent, <c>CraftItemTool</c> returns a failure and
/// <c>TryRecoverFromGameErrorAsync</c> asks the LLM to suggest the appropriate gather goal.
/// </summary>
public sealed class CraftItemGoal(string itemId, int count = 1) : IGoal
{
    public string ItemId => itemId;
    public int    Count  => count;

    public string   Name        => $"CraftItem:{itemId}";
    public string   Description => $"Craft {count}x {itemId.Replace('_', ' ')}.";
    public string[] Phases      => ["Craft"];

    /// <inheritdoc/>
    public string? FailureReason { get; set; }

    /// <summary>
    /// Goal is complete when the inventory holds at least <see cref="Count"/>
    /// units of <see cref="ItemId"/>.
    /// </summary>
    public bool IsComplete(WorldState state) =>
        state.Inventory.GetValueOrDefault(itemId) >= count;

    /// <inheritdoc/>
    public bool HasFailed(WorldState state) =>
        state.Facts.TryGetValue($"goal:CraftItem:{itemId}:failed", out var v) && v is true;
}
