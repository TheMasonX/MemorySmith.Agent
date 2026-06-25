namespace Agent.Planning;

/// <summary>
/// TSK-0082: Shared static smeltable item mappings to eliminate OutputItem drift.
///
/// Consolidates all smeltable item conversion logic in one place instead of
/// duplicating it across SmeltGoal, HtnTaskLibrary.DecomposeSmeltItem, and
/// HtnTaskLibrary.IsMineableBlock.
///
/// Rules:
/// - Input → Output: maps smeltable input to its smelted product (e.g. iron_ore → iron_ingot)
/// - Output → Input (reverse): maps smelted product back to its raw ore (e.g. iron_ingot → iron_ore)
/// - IsMineableBlock: returns true for blocks that can be mined as smeltable input
///
/// The wild-card "*_ore → *_ingot" pattern is intentionally avoided because it
/// produces invalid item IDs for non-smeltable ores (redstone_ore → "redstone_ingot",
/// emerald_ore → "emerald_ingot", etc.). Only explicitly listed mappings are valid.
/// Extend these dictionaries when new smeltable items are added.
/// </summary>
public static class SmeltableMapping
{
    /// <summary>
    /// Maps smeltable input items to their smelted output.
    /// Used by SmeltGoal.OutputItem to determine completion condition.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> InputToOutput =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["iron_ore"]        = "iron_ingot",
            ["raw_iron"]        = "iron_ingot",
            ["gold_ore"]        = "gold_ingot",
            ["raw_gold"]        = "gold_ingot",
            ["nether_gold_ore"] = "gold_ingot",
            ["copper_ore"]      = "copper_ingot",
            ["raw_copper"]      = "copper_ingot",
            ["ancient_debris"]  = "netherite_scrap",
        };

    /// <summary>
    /// Reverse mapping: smelted output → raw input block to mine.
    /// Used by DecomposeSmeltItem to determine what block to pre-gather.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> OutputToInput =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["iron_ingot"]      = "iron_ore",
            ["gold_ingot"]      = "gold_ore",
            ["copper_ingot"]    = "copper_ore",
            ["netherite_scrap"] = "ancient_debris",
        };

    /// <summary>
    /// Maps raw item drops to the ore block that must be mined to obtain them.
    /// AUD-48-001: separates smelt-input items from mineable source blocks so
    /// DecomposeSmeltItem never emits MineBlock(raw_iron) — an invalid action.
    /// Checked before OutputToInput in <see cref="GetInputBlock"/>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> RawInputToBlock =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["raw_iron"]   = "iron_ore",
            ["raw_gold"]   = "gold_ore",
            ["raw_copper"] = "copper_ore",
        };

    /// <summary>
    /// Set of all mineable smeltable ore blocks (including deepslate variants).
    /// Used by DecomposeSmeltItem.IsMineableBlock to decide whether a MineBlock
    /// action should be emitted.
    ///
    /// AUD-48-001: raw item drops (raw_iron, raw_gold, raw_copper) removed from
    /// this set. They are items, not blocks, and cannot be valid MineBlock targets.
    /// Raw items are resolved to their ore block equivalents via
    /// <see cref="RawInputToBlock"/> in <see cref="GetInputBlock"/>.
    /// </summary>
    public static readonly IReadOnlySet<string> SmeltableMineableBlocks =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "iron_ore", "deepslate_iron_ore",
            "gold_ore", "deepslate_gold_ore",
            "copper_ore", "deepslate_copper_ore",
            "ancient_debris", "nether_gold_ore",
            "coal_ore", "deepslate_coal_ore",
        };

    /// <summary>
    /// Returns the smelted output for a given input item, or the input itself if unknown.
    /// </summary>
    public static string GetOutput(string inputItem) =>
        InputToOutput.TryGetValue(inputItem, out var output) ? output : inputItem;

    /// <summary>
    /// Returns the mineable source block for a given item.
    /// Resolution order:
    ///   1. RawInputToBlock — raw item drops (raw_iron → iron_ore)  [AUD-48-001]
    ///   2. OutputToInput   — smelted output (iron_ingot → iron_ore)
    ///   3. Identity        — already an ore block or unknown
    /// </summary>
    public static string GetInputBlock(string item) =>
        RawInputToBlock.TryGetValue(item, out var rawBlock) ? rawBlock
        : OutputToInput.TryGetValue(item, out var oreBlock) ? oreBlock
        : item;

    /// <summary>
    /// Returns true if the given block is a known mineable smeltable ore or raw material.
    /// </summary>
    public static bool IsSmeltableMineableBlock(string block) =>
        SmeltableMineableBlocks.Contains(block);
}
