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
///
/// Sprint 22 P0: IsComplete now guards on <see cref="WorldState.IsInventoryStale"/>,
/// mirroring the GenericGatherGoal fix from Sprint 21. Prevents false-completion after
/// an admin /clear command that the bot did not observe via GetStatus.
/// </summary>
public sealed class CraftItemGoal(string itemId, int count = 1) : IGoal
{
    public string ItemId => itemId;
    public int    Count  => count;

    public string   Name        => $"CraftItem:{itemId}";
    public string   Description => $"Craft {count}x {itemId.Replace('_', ' ')}.";
    public string[] Phases      => ["Craft"];

    /// <summary>Sprint 39: stable per-instance ID so ActionOutcome.GoalId is unique across goals.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <inheritdoc/>
    public string? FailureReason { get; set; }

    /// <summary>
    /// Goal is complete when the inventory holds at least <see cref="Count"/>
    /// units of <see cref="ItemId"/>.
    ///
    /// Sprint 22 P0: returns false when inventory is stale (SetGoal marks it stale;
    /// WorldStateProjector.ApplyStatus clears it on fresh StatusEvent from GetStatus).
    /// </summary>
    public bool IsComplete(WorldState state)
    {
        // Sprint 22 P0: inventory freshness gate — mirrors GenericGatherGoal (Sprint 21 P0-A).
        if (state.IsInventoryStale)
            return false;

        return state.Inventory.GetValueOrDefault(itemId) >= count;
    }

    /// <inheritdoc/>
    public bool HasFailed(WorldState state) =>
        state.Facts.TryGetValue($"goal:CraftItem:{itemId}:failed", out var v) && IsTruthy(v);

    private static bool IsTruthy(object? value) => value switch
    {
        bool b => b,
        string s when bool.TryParse(s, out var parsed) => parsed,
        _ => false,
    };
}
