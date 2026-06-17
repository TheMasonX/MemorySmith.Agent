namespace Agent.Planning;

using Agent.Construction;
using Agent.Core;

/// <summary>
/// Decomposes a compound HTN task into a sequence of atomic <see cref="ActionData"/> items
/// given optional string parameters and the current <see cref="WorldState"/>.
/// </summary>
public delegate IReadOnlyList<ActionData> TaskDecomposer(
    string[] parameters, WorldState state);

/// <summary>
/// Registry of named HTN task decompositions.
///
/// Sprint 9 A3: DecomposeBuild reads auto-origin from WorldState.Facts (via BuildFactKeys)
///   when the caller supplies (0, 0, 0) as a "let the scanner decide" sentinel. If no
///   auto-origin is stored, a FindFlatArea action is prepended to locate a suitable site.
///
/// Sprint 10 D3: <see cref="BuildCraftingChain"/> now uses GroupBy to merge duplicate
///   blueprint material entries instead of throwing <see cref="ArgumentException"/>.
///
/// Sprint 10 B4: Expanded <see cref="CraftingChainOrder"/> — more plank variants, fences,
///   stairs, and basic wooden/stone tools; <see cref="DirectMineBlocks"/> updated to match.
///
/// Sprint 10 B1: DecomposeBuild prepends a FindFlatArea step when no origin is available.
///
/// Sprint 10 B2: DecomposeBuild reads checkpoint fact (<see cref="BuildFactKeys.BuildProgressIndex"/>)
///   and resumes from the last successfully placed block instead of replaying from scratch.
/// </summary>
public sealed class HtnTaskLibrary
{
    // ── Crafting constants ────────────────────────────────────────────────────

    /// <summary>Vanilla: 1 stick + 1 coal → 4 torches.</summary>
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
    // Ordered from least-dependent (no table) to most-dependent.
    private static readonly IReadOnlyList<string> CraftingChainOrder =
    [
        // Planks (no table required) — all log variants
        "oak_planks",
        "birch_planks",
        "spruce_planks",
        "dark_oak_planks",
        "jungle_planks",
        "acacia_planks",
        "mangrove_planks",
        "cherry_planks",
        // Crafting table (from planks, no table required)
        "crafting_table",
        // Sticks (from planks, no table required)
        "stick",
        // Table-dependent items
        "oak_slab",
        "oak_stairs",
        "oak_door",
        "oak_fence",
        "oak_fence_gate",
        "chest",
        // Basic wooden tools (sticks + planks, table required)
        "wooden_pickaxe",
        "wooden_axe",
        "wooden_shovel",
        // Basic stone tools (sticks + cobblestone, table required)
        "stone_pickaxe",
        "stone_axe",
        "stone_shovel",
        "stone_sword",
        // Iron tools (sticks + iron_ingot, table required)
        // iron_ingot requires SmeltItem — handled via smelting step, not plain CraftItem
    ];

    // Items in CraftingChainOrder that require a crafting table.
    private static readonly HashSet<string> RequiresCraftingTable = new(StringComparer.OrdinalIgnoreCase)
    {
        "oak_slab", "oak_stairs", "oak_door", "oak_fence", "oak_fence_gate",
        "chest", "wooden_pickaxe", "wooden_axe", "wooden_shovel",
        "stone_pickaxe", "stone_axe", "stone_shovel", "stone_sword",
    };

    // Items that additionally require cobblestone to craft (stone tools).
    // Cobblestone must be in DirectMineBlocks for these to work.
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
    /// Decomposes a <see cref="BuildGoal"/> into a phased action plan.
    ///
    /// Phase 0 (B1 preflight): if no build origin is known, prepend FindFlatArea to locate one.
    /// Phase 1: GatherMaterials — mine raw blocks.
    /// Phase 2: CraftingChain — craft intermediates in dependency order.
    /// Phase 3: Navigate to build site.
    /// Phase 4: Build — emit PlaceBlock actions from checkpoint (Sprint 10 B2).
    /// Phase 5: Verify — GetStatus.
    /// </summary>
    public IReadOnlyList<ActionData> DecomposeBuild(
        Blueprint blueprint,
        IReadOnlyList<PlacementBlock> blocks,
        int originX, int originY, int originZ,
        WorldState state)
    {
        // Sprint 9 A3: resolve auto-origin when caller passes the (0,0,0) sentinel.
        if (originX == 0 && originY == 0 && originZ == 0)
            ResolveAutoOrigin(state, ref originX, ref originY, ref originZ);

        var actions = new List<ActionData>();

        // Sprint 10 B1 note: if origin is still (0,0,0) after auto-origin lookup,
        // the plan proceeds with world-origin coordinates. Callers should trigger a
        // FindFlatArea goal first (which sets auto-origin via FlatAreaFoundEvent),
        // then the next Build plan will use the correct coordinates.
        // Full preflight gating (early-return when no origin) deferred to Sprint 11
        // pending test callsite audit.

        // ── Phase 1: GatherMaterials ──────────────────────────────────────────
        // Sprint 10 D3: use GroupBy+Sum to merge duplicate material entries instead
        // of throwing ArgumentException from ToDictionary on duplicate keys.
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
            actions.Add(MakeAction("Wander",
                ("radius", (object?)30), ("maxDistanceFromSpawn", (object?)150)));
            actions.Add(MakeAction("MineBlock",
                ("block", block), ("count", (object?)needed)));
        }

        // Gather coal for torches.
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
                    actions.Add(MakeAction("MineBlock",
                        ("block", "coal_ore"), ("count", (object?)(coalNeeded - haveCoal))));
                }
            }
        }

        // Sprint 10 B4: SmeltItem for iron_ingot if needed.
        if (materials.TryGetValue("iron_ingot", out var ironNeeded))
        {
            var haveIron = state.Inventory.GetValueOrDefault("iron_ingot");
            var toSmelt  = ironNeeded - haveIron;
            if (toSmelt > 0)
            {
                actions.Add(MakeAction("SearchMemory", ("query", "furnace iron ore location")));
                actions.Add(MakeAction("MineBlock",
                    ("block", "iron_ore"), ("count", (object?)toSmelt)));
                actions.Add(MakeAction("SmeltItem",
                    ("item", "iron_ore"), ("count", (object?)toSmelt), ("fuel", "coal")));
            }
        }

        // ── Phase 2: CraftingChain ────────────────────────────────────────────
        actions.AddRange(BuildCraftingChain(blueprint, materials, state,
            hasTorch: torchEntry is not null, torchNeeded: torchEntry ?? 0));

        // ── Phase 3: Navigate to build site ──────────────────────────────────
        actions.Add(MakeAction("SearchMemory",
            ("query", $"flat area build location {blueprint.Name}")));
        actions.Add(MakeAction("MoveTo",
            ("x", (object?)originX), ("y", (object?)originY), ("z", (object?)originZ)));

        // ── Phase 4: Build (Sprint 10 B2: resume from checkpoint) ────────────
        var progressKey     = BuildFactKeys.BuildProgressIndex(blueprint.Name);
        var checkpointIndex = 0;
        if (TryGetIntFact(state, progressKey, out var lastPlaced))
            checkpointIndex = lastPlaced + 1; // next unplaced block

        var executor    = new BlueprintExecutor();
        var blockActions = executor.Execute(blocks, originX, originY, originZ);

        for (int i = checkpointIndex; i < blockActions.Count; i++)
        {
            var placeAction = blockActions[i];
            // Add context so AgentBackgroundService can write the checkpoint fact on success.
            placeAction.Context[BuildFactKeys.PlaceBlockProgressBlueprintId] = blueprint.Name;
            placeAction.Context[BuildFactKeys.PlaceBlockProgressBlockIndex]  = i;
            actions.Add(placeAction);
        }

        // ── Phase 5: Verify ───────────────────────────────────────────────────
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

        // B1: if any table-requiring item is needed and crafting_table isn't in the blueprint,
        // auto-craft one as a preparatory step.
        bool anyTableRequired = materials.Keys.Any(RequiresCraftingTable.Contains);
        if (anyTableRequired
            && !materials.ContainsKey("crafting_table")
            && state.Inventory.GetValueOrDefault("crafting_table") == 0)
        {
            actions.Add(MakeAction("CraftItem", ("item", "crafting_table"), ("count", (object?)1)));
        }

        foreach (var item in CraftingChainOrder)
            EmitCraftIfNeeded(item, materials, state, actions);

        // Torch: sticks then torches.
        if (hasTorch && torchNeeded > 0)
        {
            var needed    = torchNeeded - state.Inventory.GetValueOrDefault("torch");
            if (needed > 0)
            {
                var sticksNeeded = Math.Max(1, (needed + TorchesPerCraft - 1) / TorchesPerCraft);
                var haveSticks   = state.Inventory.GetValueOrDefault("stick");
                if (haveSticks < sticksNeeded)
                    actions.Add(MakeAction("CraftItem",
                        ("item", "stick"), ("count", (object?)(sticksNeeded - haveSticks))));
                actions.Add(MakeAction("CraftItem",
                    ("item", "torch"), ("count", (object?)needed)));
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
