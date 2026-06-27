namespace Agent.Planning;

/// <summary>
/// Shared alias dictionaries for item, blueprint, and craft name resolution.
///
/// Consolidates previously-duplicated dictionaries in <see cref="ChatInterpreter"/>
/// and <see cref="IntentManager"/> into a single source of truth (TSK-0099).
///
/// Both classes reference these dictionaries directly. IntentManager has additional
/// LLM-focused aliases (wool→white_wool, planks→oak_planks, etc.) that are merged
/// into <see cref="ItemAliases"/> alongside the player-shorthand mappings from ChatInterpreter.
/// </summary>
public static class AliasRegistry
{
    /// <summary>
    /// Maps common player shorthand and LLM-output names to canonical Minecraft item IDs.
    ///
    /// Sources (TSK-0099):
    /// - ChatInterpreter.ItemAliases: player shorthand (wood→oak_log, cobble→cobblestone, etc.)
    /// - IntentManager.ItemAliases: LLM output normalization (wool→white_wool, planks→oak_planks, etc.)
    ///
    /// For conflicting entries (iron, gold, copper), the IntentManager values (iron_ore, gold_ore,
    /// copper_ore) are used because they're the correct block IDs for gather-goal creation.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ItemAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // ── Wood (ChatInterpreter) ────────────────────────────────────────
            ["wood"]        = "oak_log",
            ["log"]         = "oak_log",
            ["logs"]        = "oak_log",
            ["oak"]         = "oak_log",
            ["birch"]       = "birch_log",
            ["spruce"]      = "spruce_log",
            ["pine"]        = "spruce_log",
            ["dark oak"]    = "dark_oak_log",
            ["jungle"]      = "jungle_log",
            ["acacia"]      = "acacia_log",
            ["cherry"]      = "cherry_log",
            ["mangrove"]    = "mangrove_log",

            // ── Stone (ChatInterpreter) ───────────────────────────────────────
            ["cobble"]      = "cobblestone",
            ["rock"]        = "cobblestone",
            ["rocks"]       = "cobblestone",
            ["stone"]       = "stone",

            // ── Ores and drops (ChatInterpreter + IntentManager merged) ───────
            ["coal"]        = "coal",
            ["diamond"]     = "diamond",
            ["diamonds"]    = "diamond",
            ["emerald"]     = "emerald",
            ["emeralds"]    = "emerald",
            ["redstone"]    = "redstone",
            ["lapis"]       = "lapis_lazuli",

            // Conflicts: IntentManager values (block IDs for gather-goal creation)
            ["iron"]        = "iron_ore",
            ["gold"]        = "gold_ore",
            ["copper"]      = "copper_ore",

            // ── Terrain (ChatInterpreter) ─────────────────────────────────────
            ["dirt"]        = "dirt",
            ["sand"]        = "sand",
            ["gravel"]      = "gravel",
            ["clay"]        = "clay",
            ["snow"]        = "snow_block",

            // ── IntentManager additions (LLM output normalization) ────────────
            ["wool"]        = "white_wool",
            ["planks"]      = "oak_planks",
            ["wood planks"] = "oak_planks",
            ["wood plank"]  = "oak_planks",
            ["plank"]       = "oak_planks",
            ["stick"]       = "stick",
            ["glass"]       = "glass",
            ["glass pane"]  = "glass_pane",
            ["chest"]       = "chest",
            ["netherite"]   = "ancient_debris",
        };

    /// <summary>
    /// Maps common player-facing blueprint names to canonical blueprint IDs.
    /// Shared by both <see cref="ChatInterpreter"/> and <see cref="IntentManager"/>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> BlueprintAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["house"]           = "small-house",
            ["small house"]     = "small-house",
            ["cabin"]           = "small-house",
            ["shelter"]         = "small-house",
            ["hut"]             = "small-house",
            ["home"]            = "small-house",
            ["shack"]           = "small-house",
        };

    /// <summary>
    /// Maps common craft shorthand to canonical Minecraft item IDs.
    /// Used by <see cref="ChatInterpreter.ResolveCraftItem"/> for craft intent normalization.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> CraftAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["plank"]           = "oak_planks",
            ["planks"]          = "oak_planks",
            ["oak plank"]       = "oak_planks",
            ["oak planks"]      = "oak_planks",
            ["stick"]           = "stick",
            ["sticks"]          = "stick",
            ["chest"]           = "chest",
            ["table"]           = "crafting_table",
            ["crafting table"]  = "crafting_table",
            ["workbench"]       = "crafting_table",
            ["furnace"]         = "furnace",
            ["torch"]           = "torch",
            ["torches"]         = "torch",
            ["pickaxe"]         = "wooden_pickaxe",
            ["axe"]             = "wooden_axe",
            ["shovel"]          = "wooden_shovel",
            ["sword"]           = "wooden_sword",
            ["iron pickaxe"]    = "iron_pickaxe",
            ["iron axe"]        = "iron_axe",
            ["iron shovel"]     = "iron_shovel",
            ["iron sword"]      = "iron_sword",
            ["iron helmet"]     = "iron_helmet",
            ["iron chestplate"] = "iron_chestplate",
            ["iron leggings"]   = "iron_leggings",
            ["iron boots"]      = "iron_boots",
            ["bread"]           = "bread",
            ["bowl"]            = "bowl",
            ["sign"]            = "oak_sign",
            ["ladder"]          = "ladder",
            ["fence"]           = "oak_fence",
            ["door"]            = "oak_door",
            ["trapdoor"]        = "oak_trapdoor",
            ["slab"]            = "oak_slab",
            ["stairs"]          = "oak_stairs",
        };
}
