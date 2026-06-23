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
/// </summary>
public sealed class SmeltGoal(string inputItem, int count = 1) : IGoal
{
    public string InputItem => inputItem;
    public int    Count     => count;

    /// <summary>
    /// Output item: derived from the smeltable input. Returns known outputs for recognized
    /// smeltable ores/items; returns InputItem as-is for all others (identity passthrough).
    ///
    /// NOTE: The wild-card "*_ore → *_ingot" pattern is intentionally avoided because it
    /// produces invalid item IDs for non-smeltable ores (redstone_ore → "redstone_ingot",
    /// emerald_ore → "emerald_ingot", etc.). Only explicitly listed mappings are valid.
    /// Extend this list when new smeltable items are added.
    /// </summary>
    public string OutputItem => InputItem switch
    {
        "iron_ore" or "raw_iron"        => "iron_ingot",
        "gold_ore" or "raw_gold"        => "gold_ingot",
        "copper_ore" or "raw_copper"    => "copper_ingot",
        "nether_gold_ore"               => "gold_ingot",
        "ancient_debris"                => "netherite_scrap",
        _                               => InputItem,
    };

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

    /// <inheritdoc/>
    public bool HasFailed(WorldState state) =>
        state.Facts.TryGetValue($"goal:SmeltItem:{inputItem}:failed", out var v) && IsTruthy(v);

    private static bool IsTruthy(object? value) => value switch
    {
        bool b => b,
        string s when bool.TryParse(s, out var parsed) => parsed,
        _ => false,
    };
}
