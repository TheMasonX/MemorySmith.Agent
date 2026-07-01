namespace Agent.Planning.Goals;

using Agent.Core;

/// <summary>
/// Sprint 44 (TSK-0079): Goal that instructs the bot to smelt a specific item.
/// Produces <c>SmeltItem</c> actions (not <c>CraftItem</c>) via
/// <see cref="SmeltGoalDecomposer"/> → <see cref="HtnTaskLibrary.DecomposeSmeltItem"/>.
///
/// Previously, any "smelt" intent was routed through <c>CraftItemGoal</c> →
/// <c>CraftItemGoalDecomposer</c> → <c>CraftItemTool</c>, which never exercised
/// the adapter's <c>case 'smelt':</c> handler. This is the fix for the 7-sprint-old
/// smelt→CraftItem routing bug.
///
/// TSK-0082: OutputItem now delegates to the shared <see cref="SmeltableMapping"/>.
/// </summary>
public sealed class SmeltGoal(string inputItem, int count = 1) : IGoal, IGoalPrecondition
{
    public string InputItem => inputItem;
    public int    Count     => count;

    /// <summary>
    /// Output item: derived from the smeltable input via the shared
    /// <see cref="SmeltableMapping.GetOutput"/> method.
    /// Returns known outputs for recognized smeltable ores/items;
    /// returns InputItem as-is for all others (identity passthrough).
    /// </summary>
    public string OutputItem => SmeltableMapping.GetOutput(inputItem);

    public string   Name        => $"SmeltItem:{inputItem}";
    public string   Description => $"Smelt {count}x {inputItem.Replace('_', ' ')} into {OutputItem.Replace('_', ' ')}.";
    public string[] Phases      => ["Smelt"];

    /// <summary>Stable per-instance ID for ActionOutcome tracking.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <inheritdoc/>
    public string? FailureReason { get; set; }

    /// <summary>
    /// Goal is complete when the inventory holds at least <see cref="Count"/>
    /// units of <see cref="OutputItem"/>.
    /// </summary>
    public bool IsComplete(WorldState state)
    {
        if (state.IsInventoryStale) return false;
        return state.Inventory.GetValueOrDefault(OutputItem) >= count;
    }

    /// <summary>
    /// TSK-0085: Always returns false. The fact key <c>goal:SmeltItem:{inputItem}:failed</c>
    /// is never written by any code path — AgentBackgroundService uses a consecutive-failure
    /// counter for abort detection instead. This method exists to satisfy the <see cref="IGoal"/>
    /// interface contract. If a failure-write site is added later, restore the fact-key lookup.
    /// </summary>
    public bool HasFailed(WorldState state) => false;

    // ── IGoalPrecondition ────────────────────────────────────────────────────

    /// <summary>
    /// Sprint 58 (TSK-0310): Creative mode → always feasible (items can be spawned).
    /// Survival → requires fresh inventory to check input material availability.
    /// </summary>
    bool IGoalPrecondition.CanAttempt(ExecutionContext context, out string? blockingReason)
    {
        if (context.Capabilities.CanSpawnItems)
        {
            blockingReason = null;
            return true;
        }

        if (!context.HasFreshInventory)
        {
            blockingReason = "Inventory is stale — wait for GetStatus refresh before smelting.";
            return false;
        }

        blockingReason = null;
        return true;
    }
}
