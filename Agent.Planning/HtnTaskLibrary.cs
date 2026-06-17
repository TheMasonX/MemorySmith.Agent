namespace Agent.Planning;

using Agent.Construction;
using Agent.Core;
using System.Text.Json;

/// <summary>
/// Decomposes a compound HTN task into a sequence of atomic <see cref="ActionData"/> items
/// given optional string parameters and the current <see cref="WorldState"/>.
/// </summary>
public delegate IReadOnlyList<ActionData> TaskDecomposer(
    string[] parameters, WorldState state);

/// <summary>
/// Registry of named HTN task decompositions.
///
/// Sprint 9 A3: DecomposeBuild reads auto-origin from WorldState.Facts (via BuildFactKeys).
/// Sprint 10 D3: BuildCraftingChain uses GroupBy to merge duplicate blueprint materials.
/// Sprint 10 B4: Expanded CraftingChainOrder with more tools and plank variants.
/// Sprint 10 B2: DecomposeBuild reads checkpoint and resumes from last placed block.
/// Sprint 11 B1-v2: DecomposeBuild accepts a requireOrigin flag.
/// Sprint 13 D2: TryGetIntFact now handles JsonElement values (from JSON deserialization).
/// Sprint 13: Added DecomposeCraftItem for CraftItemGoal decomposition.
/// </summary>
public sealed class HtnTaskLibrary
{
    // ── Crafting constants ────────────────────────────────────────────────────

    /// <summary>Vanilla: 1 stick + 1 coal -> 4 torches.</summary>
    private const int TorchesPerCraft = 4;

    /// <summary>Radius passed to FindFlatArea when DecomposeBuild has no origin set.</summary>
    private const int PreflightFlatAreaRadius = 30;

    /// <summary>Minimum qualifying flat area (cells) passed to FindFlatArea during preflight.</summary>
    private const int PreflightFlatAreaMin = 25;

    private static readonly ItemSpec OakLogSpec = new()
    {
        ItemId          = "oak_log",
        DisplayName     = "Oak Log",
        SourceBlocks    = ["oak_log", "birch_log", "spruce_log",
                           "dark_oak_log", "jungle_log", "acacia_log", "cherry_log"],
        RequiresSmelting = false,
        MinHarvestLevel  = 0,
    };

    // Blocks the bot can mine directly (no crafting required).
    private static readonly HashSet<string> DirectMineBlocks = new(StringComparer.OrdinalIgnoreCase)
    {
        // Stone / earth
        "cobblestone", "stone", "dirt", "gravel", "sand",
        // Wood
        "oak_log", "birch_log", "spruce_log", "dark_oak_log",
        "jungle_log", "acacia_log", "cherry_log", "mangrove_log",
        // Ores
        "iron_ore", "deepslate_iron_ore",
        "gold_ore", "deepslate_gold_ore",
        "coal_ore", "deepslate_coal_ore",
        "diamond_ore", "deepslate_diamond_ore",
        "redstone_ore", "deepslate_redstone_ore",
        "lapis_ore", "deepslate_lapis_ore",
        // Other
        "gravel",
    };

    // Sprint 10 B4: expanded crafting chain.
    private static readonly IReadOnlyList<string> CraftingChainOrder =
    [
        "oak_planks", "birch_planks", "spruce_planks", "dark_oak_planks",
        "jungle_planks", "acacia_planks", "mangrove_planks", "cherry_planks",
        "crafting_table", "stick",
        "oak_slab", "oak_stairs", "oak_door", "oak_fence", "oak_fence_gate", "chest",
        "wooden_pickaxe", "wooden_axe", "wooden_shovel",
        "stone_pickaxe", "stone_axe", "stone_shovel", "stone_sword",
    ];

    // Items in CraftingChainOrder that require a crafting table.
    private static readonly HashSet<string> RequiresCraftingTable = new(StringComparer.OrdinalIgnoreCase)
    {
        "oak_slab", "oak_stairs", "oak_door", "oak_fence", "oak_fence_gate",
        "chest", "wooden_pickaxe", "wooden_axe", "wooden_shovel",
        "stone_pickaxe", "stone_axe", "stone_shovel", "stone_sword",
        // Iron tools also require a crafting table
        "iron_pickaxe", "iron_axe", "iron_shovel", "iron_sword", "iron_hoe",
        "iron_helmet", "iron_chestplate", "iron_leggings", "iron_boots",
    };

    // Items that additionally require cobblestone to craft (stone tools).
    private static readonly HashSet<string> RequiresCobblestone = new(StringComparer.OrdinalIgnoreCase)
    {
        "stone_pickaxe", "stone_axe", "stone_shovel", "stone_sword",
    };

    private readonly Dictionary<string, TaskDecomposer> _methods;

    public HtnTaskLibrary()
    {
        _methods = new Dictionary<string, TaskDecomposer>(StringComparer.OrdinalIgnoreCase)
        {
            ["GatherWood"]      = GatherWoodDecompose,
            ["FindTree"]        = FindTreeDecompose,
            ["MineWood"]        = MineWoodDecompose,
            ["Collect"]         = CollectDecompose,
            ["SurviveNight"]    = SurviveNightDecompose,
            ["FindShelter"]     = FindShelterDecompose,
            ["LightArea"]       = LightAreaDecompose,
            ["WaitForSunrise"]  = WaitDecompose,
            ["Wander"]          = WanderDecompose,
            ["Explore"]         = ExploreDecompose,
            ["FindFlatArea"]    = FindFlatAreaDecompose,
        };
    }

    public bool HasTask(string taskName) => _methods.ContainsKey(taskName);

    public IReadOnlyList<ActionData> Decompose(
        string taskName, string[] parameters, WorldState state)
    {
        if (!_methods.TryGetValue(taskName, out var decompose))
            throw new InvalidOperationException(
                $"No decomposition registered for task '{taskName}'. " +
                "Add a method to HtnTaskLibrary or use the LLM fallback.");
        return decompose(parameters, state);
    }

    public IReadOnlyList<ActionData> DecomposeGatherItem(
        ItemSpec spec, string[] parameters, WorldState state) =>
        GatherItemDecompose(spec, parameters, state);

    /// <summary>
    /// Decomposes a <see cref="Goals.CraftItemGoal"/> into a sequence of prerequisite
    /// and crafting actions.
    ///
    /// Sprint 13: first implementation. Ensures a crafting table is available for
    /// table-requiring recipes. Assumes materials are in inventory; if not, the
    /// CraftItemTool returns failure and TryRecoverFromGameErrorAsync suggests gathering.
    /// </summary>
    public IReadOnlyList<ActionData> DecomposeCraftItem(
        string itemId, int count, WorldState state)
    {
        var actions = new List<ActionData>();

        // Ensure a crafting table is present for table-requiring recipes.
        if (RequiresCraftingTable.Contains(itemId) &&
            state.Inventory.GetValueOrDefault("crafting_table") == 0)
        {
            // Need 4 oak_planks for the table; planks need 1 oak_log.
            if (state.Inventory.GetValueOrDefault("oak_planks") < 4)
            {
                if (state.Inventory.GetValueOrDefault("oak_log") < 1)
                    actions.Add(MakeAction("MineBlock", ("block", "oak_log"), ("count", (object?)1)));
                actions.Add(MakeAction("CraftItem", ("item", "oak_planks"), ("count", (object?)4)));
            }
            actions.Add(MakeAction("CraftItem", ("item", "crafting_table"), ("count", (object?)1)));
        }

        // The craft — materials must already be in inventory.
        // If they're absent, CraftItemTool returns failure; error recovery suggests gathering.
        actions.Add(MakeAction("CraftItem", ("item", itemId), ("count", (object?)count)));
        actions.Add(MakeAction("GetStatus"));
        return actions;
    }

    /// <summary>
    /// Decomposes a <see cref="Goals.BuildGoal"/> into a phased action plan.
    ///
    /// Sprint 11 B1-v2: accepts <paramref name="requireOrigin"/> — when true and no valid
    /// origin is resolvable, returns a single FindFlatArea action.
    /// Sprint 10 B2: resumes from checkpoint.
    /// </summary>
    public IReadOnlyList<ActionData> DecomposeBuild(
        Blueprint blueprint,
        IReadOnlyList<PlacementBlock> blocks,
        int originX, int originY, int originZ,
        WorldState state,
        bool requireOrigin = false)
    {
        if (originX == 0 && originY == 0 && originZ == 0)
            ResolveAutoOrigin(state, ref originX, ref originY, ref originZ);

        if (requireOrigin && originX == 0 && originY == 0 && originZ == 0)
        {
            return [MakeAction("FindFlatArea",
                ("radius", (object?)PreflightFlatAreaRadius),
                ("minFlatArea", (object?)PreflightFlatAreaMin))];
        }

        var actions = new List<ActionData>();

        var materials = blueprint.Materials
            .GroupBy(m => m.Block, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity), StringComparer.OrdinalIgnoreCase);

        foreach (var (block, quantity) in materials)
        {
            if (!DirectMineBlocks.Contains(block)) continue;
            var have   = state.Inventory.GetValueOrDefault(block);
            var needed = quantity - have;
            if (needed <= 0) continue;
            actions.Add(MakeAction("SearchMemory", ("query", $"{block} nearby source location")));
            actions.Add(MakeAction("Wander", ("radius", (object?)30), ("maxDistanceFromSpawn", (object?)150)));
            actions.Add(MakeAction("MineBlock", ("block", block), ("count", (object?)needed)));
        }

        var torchEntry = materials.TryGetValue("torch", out var tq) ? (int?)tq : null;
        if (torchEntry is not null)
        {
            var torchNeeded = torchEntry.Value - state.Inventory.GetValueOrDefault("torch");
            if (torchNeeded > 0)
            {
                var coalNeeded = Math.Max(1, (torchNeeded + TorchesPerCraft - 1) / TorchesPerCraft);
                var haveCoal   = state.Inventory.GetValueOrDefault("coal");
                if (haveCoal < coalNeeded)
                {
                    actions.Add(MakeAction("SearchMemory", ("query", "coal ore location nearby")));
                    actions.Add(MakeAction("MineBlock", ("block", "coal_ore"), ("count", (object?)(coalNeeded - haveCoal))));
                }
            }
        }

        if (materials.TryGetValue("iron_ingot", out var ironNeeded))
        {
            var haveIron = state.Inventory.GetValueOrDefault("iron_ingot");
            var toSmelt  = ironNeeded - haveIron;
            if (toSmelt > 0)
            {
                actions.Add(MakeAction("SearchMemory", ("query", "furnace iron ore location")));
                actions.Add(MakeAction("MineBlock", ("block", "iron_ore"), ("count", (object?)toSmelt)));
                actions.Add(MakeAction("SmeltItem", ("item", "iron_ore"), ("count", (object?)toSmelt), ("fuel", "coal")));
            }
        }

        actions.AddRange(BuildCraftingChain(blueprint, materials, state,
            hasTorch: torchEntry is not null, torchNeeded: torchEntry ?? 0));

        actions.Add(MakeAction("SearchMemory", ("query", $"flat area build location {blueprint.Name}")));
        actions.Add(MakeAction("MoveTo", ("x", (object?)originX), ("y", (object?)originY), ("z", (object?)originZ)));

        var progressKey     = BuildFactKeys.BuildProgressIndex(blueprint.Name);
        var checkpointIndex = 0;
        if (TryGetIntFact(state, progressKey, out var lastPlaced))
            checkpointIndex = lastPlaced + 1;

        var executor     = new BlueprintExecutor();
        var blockActions = executor.Execute(blocks, originX, originY, originZ);

        for (int i = checkpointIndex; i < blockActions.Count; i++)
        {
            var placeAction = blockActions[i];
            placeAction.Context[BuildFactKeys.PlaceBlockProgressBlueprintId] = blueprint.Name;
            placeAction.Context[BuildFactKeys.PlaceBlockProgressBlockIndex]  = i;
            actions.Add(placeAction);
        }

        actions.Add(MakeAction("GetStatus"));
        return actions;
    }

    // ── Auto-origin resolution ────────────────────────────────────────────────

    private static void ResolveAutoOrigin(WorldState state, ref int x, ref int y, ref int z)
    {
        if (TryGetIntFact(state, BuildFactKeys.AutoOriginX, out var ax)) x = ax;
        if (TryGetIntFact(state, BuildFactKeys.AutoOriginY, out var ay)) y = ay;
        if (TryGetIntFact(state, BuildFactKeys.AutoOriginZ, out var az)) z = az;
    }

    /// <summary>
    /// Reads an integer from <see cref="WorldState.Facts"/>, handling the common
    /// boxed types stored by different code paths.
    ///
    /// Sprint 13 D2: added <see cref="JsonElement"/> branch so checkpoint facts
    /// that arrive via JSON deserialization (e.g. from a saved state payload)
    /// are coerced correctly instead of silently returning false.
    /// </summary>
    private static bool TryGetIntFact(WorldState state, string key, out int result)
    {
        result = 0;
        if (!state.Facts.TryGetValue(key, out var v)) return false;
        return v switch
        {
            int i    => (result = i)      != int.MinValue,
            long l   => (result = (int)l) != int.MinValue,
            double d => (result = (int)d) != int.MinValue,
            string s => int.TryParse(s, out result),
            // Sprint 13 D2: JsonElement values from deserialized state payloads
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.TryGetInt32(out result),
            _        => false,
        };
    }

    // ── Crafting chain helper ─────────────────────────────────────────────────

    private static IReadOnlyList<ActionData> BuildCraftingChain(
        Blueprint blueprint,
        IReadOnlyDictionary<string, int> materials,
        WorldState state,
        bool hasTorch,
        int torchNeeded)
    {
        var actions = new List<ActionData>();

        bool anyTableRequired = materials.Keys.Any(RequiresCraftingTable.Contains);
        if (anyTableRequired
            && !materials.ContainsKey("crafting_table")
            && state.Inventory.GetValueOrDefault("crafting_table") == 0)
        {
            actions.Add(MakeAction("CraftItem", ("item", "crafting_table"), ("count", (object?)1)));
        }

        foreach (var item in CraftingChainOrder)
            EmitCraftIfNeeded(item, materials, state, actions);

        if (hasTorch && torchNeeded > 0)
        {
            var needed = torchNeeded - state.Inventory.GetValueOrDefault("torch");
            if (needed > 0)
            {
                var sticksNeeded = Math.Max(1, (needed + TorchesPerCraft - 1) / TorchesPerCraft);
                var haveSticks   = state.Inventory.GetValueOrDefault("stick");
                if (haveSticks < sticksNeeded)
                    actions.Add(MakeAction("CraftItem", ("item", "stick"), ("count", (object?)(sticksNeeded - haveSticks))));
                actions.Add(MakeAction("CraftItem", ("item", "torch"), ("count", (object?)needed)));
            }
        }

        return actions;
    }

    private static void EmitCraftIfNeeded(
        string item,
        IReadOnlyDictionary<string, int> materials,
        WorldState state,
        List<ActionData> actions)
    {
        if (!materials.TryGetValue(item, out var needed)) return;
        var have    = state.Inventory.GetValueOrDefault(item);
        var toCraft = needed - have;
        if (toCraft <= 0) return;
        actions.Add(MakeAction("CraftItem", ("item", item), ("count", (object?)toCraft)));
    }

    // ── Decomposers ───────────────────────────────────────────────────────────

    private static IReadOnlyList<ActionData> GatherWoodDecompose(
        string[] parameters, WorldState state) =>
        GatherItemDecompose(OakLogSpec, parameters, state);

    private static IReadOnlyList<ActionData> GatherItemDecompose(
        ItemSpec spec, string[] parameters, WorldState state)
    {
        var count = parameters.Length > 0 && int.TryParse(parameters[0], out var c) ? c : 10;
        var actions = new List<ActionData>
        {
            MakeAction("SearchMemory", ("query", $"{spec.ItemId} location nearby source")),
            MakeAction("Wander", ("radius", (object?)40), ("maxDistanceFromSpawn", (object?)200)),
        };
        foreach (var block in spec.SourceBlocks)
            actions.Add(MakeAction("MineBlock", ("block", block), ("count", (object?)count)));
        actions.Add(MakeAction("GetStatus"));
        return actions;
    }

    private static IReadOnlyList<ActionData> FindTreeDecompose(string[] _, WorldState state) =>
    [
        MakeAction("SearchMemory", ("query", "nearest oak birch spruce tree coordinates")),
        MakeAction("GetStatus"),
    ];

    private static IReadOnlyList<ActionData> MineWoodDecompose(
        string[] parameters, WorldState state)
    {
        var count = parameters.Length > 0 && int.TryParse(parameters[0], out var c) ? c : 10;
        return
        [
            MakeAction("MineBlock", ("block", "minecraft:oak_log"),   ("count", (object?)count)),
            MakeAction("MineBlock", ("block", "minecraft:birch_log"), ("count", (object?)count)),
        ];
    }

    private static IReadOnlyList<ActionData> CollectDecompose(
        string[] _, WorldState __) => [MakeAction("GetStatus")];

    private static IReadOnlyList<ActionData> SurviveNightDecompose(
        string[] _, WorldState state) =>
    [
        MakeAction("SearchMemory", ("query", "shelter cave house location safe night")),
        MakeAction("GetStatus"),
    ];

    private static IReadOnlyList<ActionData> FindShelterDecompose(
        string[] _, WorldState state) =>
    [
        MakeAction("SearchMemory", ("query", "shelter cave house night safe")),
        MakeAction("GetStatus"),
    ];

    private static IReadOnlyList<ActionData> LightAreaDecompose(
        string[] _, WorldState __) => [MakeAction("GetStatus")];

    private static IReadOnlyList<ActionData> WaitDecompose(
        string[] _, WorldState __) => [MakeAction("GetStatus")];

    private static IReadOnlyList<ActionData> WanderDecompose(
        string[] parameters, WorldState state)
    {
        var radius  = parameters.Length > 0 && int.TryParse(parameters[0], out var r) ? r : 20;
        var maxDist = parameters.Length > 1 && int.TryParse(parameters[1], out var m) ? m : 100;
        return
        [
            MakeAction("Wander",    ("radius", (object?)radius), ("maxDistanceFromSpawn", (object?)maxDist)),
            MakeAction("GetStatus"),
        ];
    }

    private static IReadOnlyList<ActionData> ExploreDecompose(
        string[] parameters, WorldState state)
    {
        var maxDist = parameters.Length > 0 && int.TryParse(parameters[0], out var m) ? m : 100;
        return
        [
            MakeAction("SearchMemory", ("query", "unexplored areas points of interest biome")),
            MakeAction("Wander",       ("radius", (object?)30), ("maxDistanceFromSpawn", (object?)maxDist)),
            MakeAction("GetStatus"),
            MakeAction("Wander",       ("radius", (object?)30), ("maxDistanceFromSpawn", (object?)maxDist)),
            MakeAction("GetStatus"),
        ];
    }

    private static IReadOnlyList<ActionData> FindFlatAreaDecompose(
        string[] parameters, WorldState state)
    {
        var radius      = parameters.Length > 0 && int.TryParse(parameters[0], out var r) ? r : 20;
        var minFlatArea = parameters.Length > 1 && int.TryParse(parameters[1], out var a) ? a : 25;
        return
        [
            MakeAction("FindFlatArea", ("radius", (object?)radius), ("minFlatArea", (object?)minFlatArea)),
            MakeAction("GetStatus"),
        ];
    }

    // ── Factory helper ────────────────────────────────────────────────────────

    private static ActionData MakeAction(
        string tool, params (string key, object? value)[] args)
    {
        var action = new ActionData { Tool = tool };
        foreach (var (key, value) in args)
            action.Arguments[key] = value;
        return action;
    }
}
