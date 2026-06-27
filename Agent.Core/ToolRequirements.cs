namespace Agent.Core;

/// <summary>
/// Maps Minecraft block/item types to the tool required for efficient mining.
/// Used by <see cref="Agent.Planning.GatherGoalDecomposer"/> to auto-craft tools
/// before gathering (TSK-0208).
///
/// Tool tiers: wooden=1, stone=2, iron=3, diamond=4.
/// Sprint 54 (TSK-0208): initial implementation with wooden-tier tools.
/// Stone/iron tier auto-upgrade deferred to future sprint.
/// </summary>
public static class ToolRequirements
{
    /// <summary>Tool types for tool-requirement lookups.</summary>
    public const string Pickaxe = "pickaxe";
    public const string Axe = "axe";
    public const string Shovel = "shovel";

    /// <summary>
    /// Blocks that require a pickaxe to mine effectively.
    /// Without one, mining is slow and some blocks (ores, stone) drop nothing.
    /// </summary>
    private static readonly HashSet<string> PickaxeBlocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "stone", "cobblestone",
        "iron_ore", "deepslate_iron_ore",
        "gold_ore", "deepslate_gold_ore",
        "coal_ore", "deepslate_coal_ore",
        "diamond_ore", "deepslate_diamond_ore",
        "redstone_ore", "deepslate_redstone_ore",
        "lapis_ore", "deepslate_lapis_ore",
        "emerald_ore", "deepslate_emerald_ore",
        "copper_ore", "deepslate_copper_ore",
    };

    /// <summary>Blocks that benefit from an axe (faster mining).</summary>
    private static readonly HashSet<string> AxeBlocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "oak_log", "birch_log", "spruce_log", "dark_oak_log",
        "jungle_log", "acacia_log", "cherry_log", "mangrove_log",
    };

    /// <summary>Blocks that benefit from a shovel (faster mining).</summary>
    private static readonly HashSet<string> ShovelBlocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "dirt", "grass_block", "sand", "gravel", "clay", "snow", "snow_block",
    };

    /// <summary>
    /// Returns the tool type required for mining the given block, or null if
    /// the block can be mined effectively by hand.
    /// </summary>
    public static string? GetRequiredToolType(string blockId)
    {
        if (PickaxeBlocks.Contains(blockId)) return Pickaxe;
        if (AxeBlocks.Contains(blockId)) return Axe;
        if (ShovelBlocks.Contains(blockId)) return Shovel;
        return null;
    }

    /// <summary>
    /// Returns the required tool type for any of the source blocks in the spec,
    /// or null if all source blocks are hand-mineable.
    /// </summary>
    public static string? GetRequiredToolType(ItemSpec spec)
    {
        foreach (var block in spec.SourceBlocks)
        {
            var tool = GetRequiredToolType(block);
            if (tool is not null) return tool;
        }
        return null;
    }

    /// <summary>
    /// Returns the wooden-tier tool item ID for a given tool type.
    /// "pickaxe" → "wooden_pickaxe", "axe" → "wooden_axe", "shovel" → "wooden_shovel".
    /// </summary>
    public static string GetWoodenTool(string toolType) => toolType switch
    {
        Pickaxe => "wooden_pickaxe",
        Axe => "wooden_axe",
        Shovel => "wooden_shovel",
        _ => throw new ArgumentException($"Unknown tool type: {toolType}", nameof(toolType))
    };

    /// <summary>
    /// Checks whether the inventory contains any tool of the given type
    /// (any tier — wooden, stone, iron, diamond).
    /// </summary>
    public static bool HasAnyToolOfType(IReadOnlyDictionary<string, int> inventory, string toolType)
    {
        var suffixes = toolType switch
        {
            "pickaxe" => new[] { "wooden_pickaxe", "stone_pickaxe", "iron_pickaxe", "diamond_pickaxe", "netherite_pickaxe" },
            "axe" => new[] { "wooden_axe", "stone_axe", "iron_axe", "diamond_axe", "netherite_axe" },
            "shovel" => new[] { "wooden_shovel", "stone_shovel", "iron_shovel", "diamond_shovel", "netherite_shovel" },
            _ => Array.Empty<string>()
        };

        foreach (var id in suffixes)
        {
            if (inventory.TryGetValue(id, out var count) && count > 0)
                return true;
        }
        return false;
    }
}
