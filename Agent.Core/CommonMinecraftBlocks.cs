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
///
/// Sprint 35 P2-B: expanded to include copper ore variants (all overworld + deepslate)
/// and additional deepslate variants not previously listed.
///
/// TSK-0108: SelfDroppingBlocks and BlockToItemDrop extracted from WorldStateProjector
/// so WorldModel.PredictMine uses the same drop mapping for inventory prediction.
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
        // Sprint 35 P2-B: copper ore variants
        "copper_ore",    "deepslate_copper_ore",
        // Sprint 43 (P1-1): wool blocks — mineable in creative, or require shears in survival
        "white_wool", "orange_wool", "magenta_wool", "light_blue_wool",
        "yellow_wool", "lime_wool", "pink_wool", "gray_wool",
        "light_gray_wool", "cyan_wool", "purple_wool", "blue_wool",
        "brown_wool", "green_wool", "red_wool", "black_wool",
        // Raw ore drops (item ID differs from block ID — needed for ClassifySpec correctness)
        // Mining diamond_ore yields "diamond"; mining coal_ore yields "coal"; etc.
        "diamond", "coal", "emerald", "redstone", "lapis_lazuli",
        // Sprint 35 P2-B: raw copper drop
        "raw_copper", "raw_iron", "raw_gold",
    };

    /// <summary>
    /// Blocks that drop themselves when mined without silk touch.
    /// Shared between WorldStateProjector (projection) and WorldModel (prediction).
    /// TSK-0108: extracted to single source of truth.
    /// </summary>
    public static readonly HashSet<string> SelfDroppingBlocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "dirt", "grass_block", "podzol", "mycelium", "coarse_dirt", "rooted_dirt",
        "sand", "red_sand", "suspicious_sand", "suspicious_gravel",
        "gravel", "clay",
        "cobblestone", "mossy_cobblestone",
        "stone",  // drops cobblestone — handled by BlockToItemDrop map
        "netherrack", "end_stone",
        "snow_block", "ice", "packed_ice", "blue_ice",
        // Logs (all overworld types)
        "oak_log", "birch_log", "spruce_log", "dark_oak_log",
        "jungle_log", "acacia_log", "cherry_log", "mangrove_log",
        // Planks
        "oak_planks", "birch_planks", "spruce_planks", "dark_oak_planks",
        "jungle_planks", "acacia_planks", "cherry_planks", "mangrove_planks",
        // Stone variants
        "granite", "diorite", "andesite", "tuff", "calcite", "dripstone_block",
        "deepslate", "cobbled_deepslate",
        // Other
        "obsidian", "crying_obsidian",
        "sandstone", "red_sandstone",
        "terracotta", "white_terracotta", "bricks",
        "nether_brick", "soul_sand", "soul_soil",
    };

    /// <summary>
    /// Maps block names to their dropped item names when the block does NOT drop itself.
    /// Key = block name, Value = item name that drops.
    /// Shared between WorldStateProjector (projection) and WorldModel (prediction).
    /// TSK-0108: extracted to single source of truth.
    /// </summary>
    public static readonly Dictionary<string, string> BlockToItemDrop = new(StringComparer.OrdinalIgnoreCase)
    {
        // Stone -> cobblestone
        ["stone"] = "cobblestone",
        ["stone_slab"] = "stone_slab",
        // Grass/mycelium -> dirt
        ["grass_block"] = "dirt",
        ["mycelium"] = "dirt",
        // Ores (drop raw materials, not the ore block)
        ["diamond_ore"] = "diamond",
        ["deepslate_diamond_ore"] = "diamond",
        ["coal_ore"] = "coal",
        ["deepslate_coal_ore"] = "coal",
        ["emerald_ore"] = "emerald",
        ["deepslate_emerald_ore"] = "emerald",
        ["redstone_ore"] = "redstone",
        ["deepslate_redstone_ore"] = "redstone",
        ["lapis_ore"] = "lapis_lazuli",
        ["deepslate_lapis_ore"] = "lapis_lazuli",
        ["iron_ore"] = "raw_iron",
        ["deepslate_iron_ore"] = "raw_iron",
        ["copper_ore"] = "raw_copper",
        ["deepslate_copper_ore"] = "raw_copper",
        ["gold_ore"] = "raw_gold",
        ["deepslate_gold_ore"] = "raw_gold",
        ["nether_gold_ore"] = "gold_nugget",
        // Netherite
        ["ancient_debris"] = "netherite_scrap",
        // Glass (silk touch only normally, but handle gracefully)
        ["glass"] = "glass",
    };

    /// <summary>
    /// Resolves the item drop name for a mined block, using the shared BlockToItemDrop
    /// mapping with SelfDroppingBlocks fallback.
    /// Returns the block name itself as a best-effort fallback for unknown blocks.
    /// </summary>
    public static string ResolveBlockDrop(string blockName)
    {
        var normalized = blockName.Contains(':') ? blockName.Split(':', 2)[1] : blockName;
        if (BlockToItemDrop.TryGetValue(normalized, out var mappedDrop))
            return mappedDrop;
        if (SelfDroppingBlocks.Contains(normalized))
            return normalized;
        return normalized; // best-effort fallback
    }

    // ── Crafting data tables (extracted from HtnTaskLibrary, Sprint 52) ──────

    /// <summary>Vanilla: 1 log → 4 planks.</summary>
    public const int PlanksPerLog = 4;

    /// <summary>Vanilla: 1 stick + 1 coal → 4 torches.</summary>
    public const int TorchesPerCraft = 4;

    /// <summary>
    /// Maps plank item IDs to their source log item IDs for raw-material prerequisite
    /// gathering before crafting.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> PlankToLogMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["oak_planks"]       = "oak_log",
        ["birch_planks"]     = "birch_log",
        ["spruce_planks"]    = "spruce_log",
        ["dark_oak_planks"]  = "dark_oak_log",
        ["jungle_planks"]    = "jungle_log",
        ["acacia_planks"]    = "acacia_log",
        ["mangrove_planks"]  = "mangrove_log",
        ["cherry_planks"]    = "cherry_log",
    };

    /// <summary>Items that require a crafting table (3×3 grid).</summary>
    public static readonly HashSet<string> RequiresCraftingTable = new(StringComparer.OrdinalIgnoreCase)
    {
        "oak_slab", "oak_stairs", "oak_door", "oak_fence", "oak_fence_gate",
        "chest", "wooden_pickaxe", "wooden_axe", "wooden_shovel",
        "stone_pickaxe", "stone_axe", "stone_shovel", "stone_sword",
        "iron_pickaxe", "iron_axe", "iron_shovel", "iron_sword", "iron_hoe",
        "iron_helmet", "iron_chestplate", "iron_leggings", "iron_boots",
    };

    /// <summary>Iron ingots required to craft each iron tool or armour piece.</summary>
    public static readonly IReadOnlyDictionary<string, int> IronIngotRequirements =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["iron_pickaxe"]    = 3,
        ["iron_axe"]        = 3,
        ["iron_shovel"]     = 1,
        ["iron_sword"]      = 2,
        ["iron_hoe"]        = 2,
        ["iron_helmet"]     = 5,
        ["iron_chestplate"] = 8,
        ["iron_leggings"]   = 7,
        ["iron_boots"]      = 4,
    };

    /// <summary>Cobblestone required to craft each stone tool.</summary>
    public static readonly IReadOnlyDictionary<string, int> CobblestoneRequirements =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["stone_pickaxe"] = 3,
        ["stone_axe"]     = 3,
        ["stone_shovel"]  = 1,
        ["stone_sword"]   = 2,
        ["stone_hoe"]     = 2,
    };

    /// <summary>Blocks that require a pickaxe to mine efficiently.</summary>
    public static readonly HashSet<string> RequiresPickaxeBlocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "cobblestone", "stone", "andesite", "diorite", "granite",
        "iron_ore", "deepslate_iron_ore", "coal_ore", "deepslate_coal_ore",
        "gold_ore", "deepslate_gold_ore",
        "diamond_ore", "deepslate_diamond_ore",
        "copper_ore", "deepslate_copper_ore",
    };

    /// <summary>Blocks that require an axe to mine efficiently.</summary>
    public static readonly HashSet<string> RequiresAxeBlocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "oak_log", "birch_log", "spruce_log", "dark_oak_log",
        "jungle_log", "acacia_log", "cherry_log", "mangrove_log",
    };

    /// <summary>All pickaxe item IDs (any tier).</summary>
    public static readonly string[] PickaxeItems =
        ["wooden_pickaxe", "stone_pickaxe", "iron_pickaxe", "diamond_pickaxe", "golden_pickaxe"];

    /// <summary>All axe item IDs (any tier).</summary>
    public static readonly string[] AxeItems =
        ["wooden_axe", "stone_axe", "iron_axe", "diamond_axe", "golden_axe"];

    /// <summary>First-tier pickaxe to craft when none is available.</summary>
    public const string DefaultPickaxeToCraft = "wooden_pickaxe";

    /// <summary>First-tier axe to craft when none is available.</summary>
    public const string DefaultAxeToCraft = "wooden_axe";
}
