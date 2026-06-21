namespace Agent.Core;

/// <summary>
/// Shared catalog of blocks and raw item drops the bot can obtain by mining directly
/// (no crafting or smelting required).
///
/// Sprint 14 P1a: extracted from separate static sets in HtnTaskLibrary and GoalFactory
/// to eliminate the manual-sync footgun flagged in Sprint 13 council review D1.
/// Both HtnTaskLibrary.DirectMineBlocks (planning) and GoalFactory.BuiltInDirectMineItems
/// (goal creation) now reference this single source of truth.
///
/// Sprint 17 P0: expanded to include raw ore drops where the dropped item name differs
/// from the mined block name (e.g. mining "diamond_ore" yields "diamond", not "diamond_ore").
/// ClassifySpec in LocalKnowledgeResolver checks this set to correctly classify those items
/// as DirectMineable rather than Craftable.
/// </summary>
public static class CommonMinecraftBlocks
{
    /// <summary>
    /// Blocks and raw item drops the bot can obtain by mining directly (no crafting or smelting).
    /// Union of the former HtnTaskLibrary.DirectMineBlocks and GoalFactory.BuiltInDirectMineItems sets,
    /// plus raw ore drops where the item name differs from the ore block name.
    /// </summary>
    public static readonly HashSet<string> DirectMineBlocks = new(StringComparer.OrdinalIgnoreCase)
    {
        // Earth / terrain
        "dirt", "sand", "gravel", "clay", "snow", "snow_block",
        // Stone
        "cobblestone", "stone",
        // Wood (all overworld log types)
        "oak_log", "birch_log", "spruce_log", "dark_oak_log",
        "jungle_log", "acacia_log", "cherry_log", "mangrove_log",
        // Ores (surface + deepslate — block names)
        "iron_ore",      "deepslate_iron_ore",
        "gold_ore",      "deepslate_gold_ore",
        "coal_ore",      "deepslate_coal_ore",
        "diamond_ore",   "deepslate_diamond_ore",
        "redstone_ore",  "deepslate_redstone_ore",
        "lapis_ore",     "deepslate_lapis_ore",
        "emerald_ore",   "deepslate_emerald_ore",
        // Raw ore drops (item ID differs from block ID — needed for ClassifySpec correctness)
        // Mining diamond_ore yields "diamond"; mining coal_ore yields "coal"; etc.
        "diamond", "coal", "emerald", "redstone", "lapis_lazuli",
    };
}
