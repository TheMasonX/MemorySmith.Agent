namespace Agent.Core;

/// <summary>
/// Shared catalog of blocks the bot can mine directly without crafting or smelting.
///
/// Sprint 14 P1a: extracted from separate static sets in HtnTaskLibrary and GoalFactory
/// to eliminate the manual-sync footgun flagged in Sprint 13 council review D1.
/// Both HtnTaskLibrary.DirectMineBlocks (planning) and GoalFactory.BuiltInDirectMineItems
/// (goal creation) now reference this single source of truth.
/// </summary>
public static class CommonMinecraftBlocks
{
    /// <summary>
    /// Blocks the bot can mine directly with no crafting or smelting required.
    /// Union of the former HtnTaskLibrary.DirectMineBlocks and GoalFactory.BuiltInDirectMineItems sets.
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
        // Ores (surface + deepslate)
        "iron_ore",      "deepslate_iron_ore",
        "gold_ore",      "deepslate_gold_ore",
        "coal_ore",      "deepslate_coal_ore",
        "diamond_ore",   "deepslate_diamond_ore",
        "redstone_ore",  "deepslate_redstone_ore",
        "lapis_ore",     "deepslate_lapis_ore",
    };
}
