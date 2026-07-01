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
            ["stone brick"] = "stone_bricks",
            ["stone bricks"]= "stone_bricks",
            ["stone_brick"] = "stone_bricks",

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

    // ── Fuzzy matching (Sprint 57 TSK-0304) ──────────────────────────────────

    /// <summary>
    /// All known canonical item IDs (values from <see cref="ItemAliases"/> deduplicated,
    /// plus keys that are valid item IDs themselves). Used as the search space for
    /// fuzzy matching when no exact alias exists. Lazily computed on first access.
    /// </summary>
    private static readonly Lazy<HashSet<string>> _knownItemIds = new(() =>
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Include all alias values (canonical IDs).
        foreach (var v in ItemAliases.Values)
            ids.Add(v);
        // Include alias keys that look like valid item IDs (contain underscore or are
        // single-word IDs like "dirt", "sand", "coal", "diamond", etc.).
        foreach (var k in ItemAliases.Keys)
        {
            if (k.Contains('_') || !k.Contains(' '))
                ids.Add(k);
        }
        // Additional common block IDs not covered by aliases.
        foreach (var extra in new[] {
            "stone_bricks", "stone_brick_stairs", "stone_brick_slab",
            "bricks", "brick_stairs", "brick_slab",
            "cobblestone_stairs", "cobblestone_slab", "cobblestone_wall",
            "torch", "ladder", "chest", "furnace", "crafting_table",
            "oak_stairs", "oak_slab", "oak_fence", "oak_door", "oak_trapdoor",
            "oak_sign", "glass_pane", "iron_door", "iron_trapdoor",
            "bookshelf", "enchanting_table", "anvil", "tnt",
            "obsidian", "bedrock", "netherrack", "soul_sand", "glowstone",
            "white_wool", "orange_wool", "red_wool", "blue_wool",
            "oak_planks", "birch_planks", "spruce_planks",
        })
            ids.Add(extra);

        return ids;
    });

    /// <summary>
    /// Resolves a user-supplied item name to a canonical Minecraft item ID.
    /// First checks exact alias match, then falls back to fuzzy substring matching
    /// against all known item IDs.
    /// Returns true if a match was found, false otherwise.
    /// </summary>
    public static bool TryResolve(string name, out string canonicalId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            canonicalId = string.Empty;
            return false;
        }

        // 1. Exact alias match (case-insensitive)
        if (ItemAliases.TryGetValue(name, out var alias))
        {
            canonicalId = alias;
            return true;
        }

        // 2. Normalize: replace spaces with underscores, lowercase
        var normalized = name.Trim().Replace(' ', '_').ToLowerInvariant();

        // 3. Direct match against known IDs
        if (_knownItemIds.Value.Contains(normalized))
        {
            canonicalId = normalized;
            return true;
        }

        // 4. Check if any alias value matches the normalized name
        foreach (var (_, value) in ItemAliases)
        {
            if (string.Equals(value, normalized, StringComparison.OrdinalIgnoreCase))
            {
                canonicalId = value;
                return true;
            }
        }

        // 5. Fuzzy substring match: find IDs that contain the normalized name
        var matches = _knownItemIds.Value
            .Where(id => id.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();

        if (matches.Count == 1)
        {
            canonicalId = matches[0];
            return true;
        }

        canonicalId = string.Empty;
        return false;
    }

    /// <summary>
    /// Searches for item IDs matching a partial name. Returns up to <paramref name="maxResults"/>
    /// results ranked by relevance (exact match first, then substring matches).
    /// </summary>
    public static IReadOnlyList<string> Search(string partialName, int maxResults = 5)
    {
        if (string.IsNullOrWhiteSpace(partialName))
            return Array.Empty<string>();

        var normalized = partialName.Trim().Replace(' ', '_').ToLowerInvariant();
        if (normalized.Length == 0)
            return Array.Empty<string>();

        // Exact match first
        var results = new List<string>();
        if (_knownItemIds.Value.Contains(normalized))
            results.Add(normalized);

        // Then substring matches
        var substringMatches = _knownItemIds.Value
            .Where(id => !results.Contains(id) &&
                         id.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .Take(maxResults - results.Count);

        results.AddRange(substringMatches);
        return results;
    }

    /// <summary>
    /// Sprint 57 (TSK-0304): Returns a formatted list of common item aliases
    /// for inclusion in the LLM system prompt, so the LLM knows canonical IDs.
    /// Limited to ~12 most useful entries to keep prompt compact.
    /// </summary>
    public static string GetAliasesForPrompt()
    {
        // Select the most impactful aliases where the alias differs significantly
        // from the canonical ID (i.e., the LLM is likely to get it wrong without help).
        var keyPairs = new (string Alias, string Canonical)[]
        {
            ("wood", "oak_log"),
            ("log", "oak_log"),
            ("cobble", "cobblestone"),
            ("stone brick", "stone_bricks"),
            ("wool", "white_wool"),
            ("planks", "oak_planks"),
            ("lapis", "lapis_lazuli"),
            ("netherite", "ancient_debris"),
            ("table", "crafting_table"),
            ("workbench", "crafting_table"),
            ("iron", "iron_ore"),
            ("gold", "gold_ore"),
            ("copper", "copper_ore"),
        };

        var sb = new System.Text.StringBuilder();
        sb.Append("\nCommon item aliases (use the canonical ID on the right):");
        foreach (var (alias, canonical) in keyPairs)
            sb.Append($"\n  \"{alias}\" → {canonical}");

        return sb.ToString();
    }
}
