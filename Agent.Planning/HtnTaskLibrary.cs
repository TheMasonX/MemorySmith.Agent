namespace Agent.Planning;

using Agent.Construction;
using Agent.Core;

/// <summary>
/// Decomposes a compound HTN task into a sequence of atomic <see cref="ActionData"/> items
/// given optional string parameters and the current <see cref="WorldState"/>.
///
/// Moved here from HtnTask.cs (see ADR-009 in Data/Pages/decisions.md).
/// </summary>
public delegate IReadOnlyList<ActionData> TaskDecomposer(
    string[] parameters, WorldState state);

/// <summary>
/// Registry of named HTN task decompositions.
///
/// Each entry maps a task name to a TaskDecomposer delegate that returns the
/// atomic ActionData sequence for that task. Decomposers are deterministic —
/// no LLM involvement. Unknown tasks must be handled by the LLM fallback in
/// HtnPlanner (Phase 4).
///
/// Phase 3 tasks: GatherWood, FindTree, MineWood, Collect, SurviveNight,
///               FindShelter, LightArea, WaitForSunrise.
/// Phase 4a additions: GatherItemDecompose (generic item gathering via ItemSpec).
/// Phase 4b additions: DecomposeBuild (blueprint construction via PlacementBlock list).
/// Sprint 2b: DecomposeBuild now emits a CraftingChain after raw material gathering.
/// </summary>
public sealed class HtnTaskLibrary
{
    // ── Crafting constants ────────────────────────────────────────────────────

    /// <summary>
    /// Vanilla Minecraft: 1 stick + 1 coal → 4 torches.
    /// Used to compute the number of sticks and coal needed when torch is in the blueprint.
    /// </summary>
    private const int TorchesPerCraft = 4;

    // Built-in ItemSpec for oak logs — used as the GatherWood fallback so that
    // GatherWoodDecompose can delegate to GatherItemDecompose without needing
    // an IItemRegistry at construction time.
    private static readonly ItemSpec OakLogSpec = new()
    {
        ItemId          = "oak_log",
        DisplayName     = "Oak Log",
        SourceBlocks    = ["oak_log", "birch_log", "spruce_log",
                           "dark_oak_log", "jungle_log", "acacia_log", "cherry_log"],
        RequiresSmelting = false,
        MinHarvestLevel  = 0,
    };

    // Blocks that can be directly mined by the bot (no crafting required).
    // Used by DecomposeBuild to determine which blueprint materials need gather actions.
    private static readonly HashSet<string> DirectMineBlocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "cobblestone", "stone", "dirt", "gravel", "sand",
        "oak_log", "birch_log", "spruce_log", "dark_oak_log",
        "jungle_log", "acacia_log", "cherry_log", "mangrove_log",
        "iron_ore", "deepslate_iron_ore",
        "gold_ore", "deepslate_gold_ore",
        "coal_ore", "deepslate_coal_ore",
        "diamond_ore", "deepslate_diamond_ore",
        "redstone_ore", "deepslate_redstone_ore",
        "lapis_ore", "deepslate_lapis_ore",
        "gravel",
    };

    // Items that can be crafted by DecomposeBuild via the CraftingChain phase.
    // Key = craftable item; Value = the raw log/ore source that must already be in
    // inventory (gathered in the earlier phase or found via DirectMineBlocks).
    // Ordered: planks first (no table), then table (no table), then table-dependent items.
    private static readonly IReadOnlyList<string> CraftingChainOrder =
    [
        "oak_planks",
        "birch_planks",
        "spruce_planks",
        "crafting_table",
        "oak_slab",
        "oak_door",
        "chest",
        // torch handled separately (requires intermediate stick step)
    ];

    // Items in the crafting chain that require a crafting table placed in the world.
    // The bot must have crafted and placed a crafting_table before reaching these.
    private static readonly HashSet<string> RequiresCraftingTable = new(StringComparer.OrdinalIgnoreCase)
    {
        "oak_slab", "oak_door", "chest",
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
        };
    }

    /// <summary>Returns true if a decomposition exists for the given task name.</summary>
    public bool HasTask(string taskName) =>
        _methods.ContainsKey(taskName);

    /// <summary>
    /// Decomposes the named task into atomic actions.
    /// Throws InvalidOperationException if no method exists.
    /// </summary>
    public IReadOnlyList<ActionData> Decompose(
        string taskName, string[] parameters, WorldState state)
    {
        if (!_methods.TryGetValue(taskName, out var decompose))
            throw new InvalidOperationException(
                $"No decomposition registered for task '{taskName}'. " +
                "Add a method to HtnTaskLibrary or use the LLM fallback.");

        return decompose(parameters, state);
    }

    /// <summary>
    /// Decomposes a generic item-gather goal into concrete actions using the
    /// provided <paramref name="spec"/>. Called directly by
    /// <see cref="HtnPlanner"/> when it encounters an <see cref="IItemSpecGoal"/>,
    /// bypassing the string-keyed dispatch table so that the full
    /// <see cref="ItemSpec"/> is available.
    /// </summary>
    public IReadOnlyList<ActionData> DecomposeGatherItem(
        ItemSpec spec, string[] parameters, WorldState state) =>
        GatherItemDecompose(spec, parameters, state);

    /// <summary>
    /// Decomposes a <see cref="BuildGoal"/> into:
    ///   1. GatherMaterials — mine directly-mineable raw blocks (cobblestone, oak_log, coal_ore…).
    ///   2. CraftingChain — emit CraftItem actions for crafted blueprint materials
    ///      (planks → crafting_table → slabs, doors, chests; sticks+coal → torches).
    ///   3. Navigate to build site.
    ///   4. Build — one PlaceBlock per block in blueprint order.
    ///   5. Verify — GetStatus.
    ///
    /// Sprint 2b: Phase 2 (CraftingChain) is new. Items already in inventory are skipped.
    /// Coal for torches is gathered implicitly — coal_ore is in <see cref="DirectMineBlocks"/>
    /// and is added as an explicit gather step when torch is in the blueprint.
    /// </summary>
    public IReadOnlyList<ActionData> DecomposeBuild(
        Blueprint blueprint,
        IReadOnlyList<PlacementBlock> blocks,
        int originX, int originY, int originZ,
        WorldState state)
    {
        var actions = new List<ActionData>();

        // ── Phase 1: GatherMaterials ──────────────────────────────────────────
        // Mine directly-mineable raw blocks (cobblestone, oak_log, coal_ore, etc.)
        foreach (var material in blueprint.Materials)
        {
            if (!DirectMineBlocks.Contains(material.Block)) continue;

            var have   = state.Inventory.GetValueOrDefault(material.Block);
            var needed = material.Quantity - have;
            if (needed <= 0) continue;

            var searchQuery = $"{material.Block} nearby source location";
            actions.Add(MakeAction("SearchMemory", ("query", searchQuery)));
            actions.Add(MakeAction("Wander",
                ("radius", (object?)30),
                ("maxDistanceFromSpawn", (object?)150)));
            actions.Add(MakeAction("MineBlock",
                ("block",  material.Block),
                ("count",  (object?)needed)));
        }

        // Sprint 2b: Gather coal if torch is in blueprint (coal_ore not in blueprint Materials,
        // but is needed as an ingredient for crafting torches).
        var torchEntry = blueprint.Materials
            .FirstOrDefault(m => string.Equals(m.Block, "torch", StringComparison.OrdinalIgnoreCase));
        if (torchEntry is not null)
        {
            var torchNeeded = torchEntry.Quantity - state.Inventory.GetValueOrDefault("torch");
            if (torchNeeded > 0)
            {
                // 1 coal → TorchesPerCraft torches; mine coal_ore to get coal
                var coalNeeded = Math.Max(1, (torchNeeded + TorchesPerCraft - 1) / TorchesPerCraft);
                var haveCoal   = state.Inventory.GetValueOrDefault("coal");
                if (haveCoal < coalNeeded)
                {
                    actions.Add(MakeAction("SearchMemory", ("query", "coal ore location nearby")));
                    actions.Add(MakeAction("MineBlock",
                        ("block", "coal_ore"),
                        ("count", (object?)(coalNeeded - haveCoal))));
                }
            }
        }

        // ── Phase 2: CraftingChain ────────────────────────────────────────────
        // Emit CraftItem actions for crafted blueprint materials, in dependency order.
        actions.AddRange(BuildCraftingChain(blueprint, state, torchEntry));

        // ── Phase 3: Navigate to build site ──────────────────────────────────
        actions.Add(MakeAction("SearchMemory",
            ("query", $"flat area build location {blueprint.Name}")));
        actions.Add(MakeAction("MoveTo",
            ("x", (object?)originX),
            ("y", (object?)originY),
            ("z", (object?)originZ)));

        // ── Phase 4: Build — emit PlaceBlock for every block in order ─────────
        var executor = new BlueprintExecutor();
        actions.AddRange(executor.Execute(blocks, originX, originY, originZ));

        // ── Phase 5: Verify ───────────────────────────────────────────────────
        actions.Add(MakeAction("GetStatus"));

        return actions;
    }

    // ── Crafting chain helper ─────────────────────────────────────────────────

    /// <summary>
    /// Builds the CraftItem action sequence for crafted blueprint materials.
    /// Steps are emitted in dependency order only when the item appears in
    /// the blueprint and inventory is insufficient.
    ///
    /// B1 fix: if any item in <see cref="RequiresCraftingTable"/> is needed and
    /// <c>crafting_table</c> is not already listed in the blueprint Materials,
    /// a preparatory <c>CraftItem(crafting_table, 1)</c> step is auto-emitted.
    /// </summary>
    private static IReadOnlyList<ActionData> BuildCraftingChain(
        Blueprint blueprint,
        WorldState state,
        MaterialEntry? torchEntry)
    {
        var actions    = new List<ActionData>();
        var materials  = blueprint.Materials
            .ToDictionary(m => m.Block, m => m.Quantity, StringComparer.OrdinalIgnoreCase);

        // B1: if any table-requiring item (slab, door, chest) is needed and crafting_table is
        // not explicitly in the blueprint Materials, auto-craft one as a preparatory step.
        bool anyTableRequired = materials.Keys.Any(RequiresCraftingTable.Contains);
        if (anyTableRequired
            && !materials.ContainsKey("crafting_table")
            && state.Inventory.GetValueOrDefault("crafting_table") == 0)
        {
            actions.Add(MakeAction("CraftItem", ("item", "crafting_table"), ("count", (object?)1)));
        }

        // Emit each step in CraftingChainOrder if the item is in the blueprint
        // and the bot doesn't already have enough.
        foreach (var item in CraftingChainOrder)
            EmitCraftIfNeeded(item, materials, state, actions);

        // Torch requires an intermediate stick step (sticks are not in blueprints directly).
        if (torchEntry is not null)
        {
            var torchNeeded = torchEntry.Quantity - state.Inventory.GetValueOrDefault("torch");
            if (torchNeeded > 0)
            {
                // Each crafting batch: 1 stick + 1 coal → TorchesPerCraft torches.
                var sticksNeeded = Math.Max(1, (torchNeeded + TorchesPerCraft - 1) / TorchesPerCraft);
                var haveSticks   = state.Inventory.GetValueOrDefault("stick");
                if (haveSticks < sticksNeeded)
                    actions.Add(MakeAction("CraftItem",
                        ("item",  "stick"),
                        ("count", (object?)(sticksNeeded - haveSticks))));

                actions.Add(MakeAction("CraftItem",
                    ("item",  "torch"),
                    ("count", (object?)torchNeeded)));
            }
        }

        return actions;
    }

    /// <summary>
    /// Adds a CraftItem action for <paramref name="item"/> if it is listed in
    /// <paramref name="materials"/> and the current inventory is insufficient.
    /// </summary>
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
        var searchQuery = $"{spec.ItemId} location nearby source";

        var actions = new List<ActionData>
        {
            MakeAction("SearchMemory", ("query", searchQuery)),
            MakeAction("Wander", ("radius", (object?)40), ("maxDistanceFromSpawn", (object?)200)),
        };

        foreach (var block in spec.SourceBlocks.Take(2))
            actions.Add(MakeAction("MineBlock", ("block", block), ("count", (object?)count)));

        actions.Add(MakeAction("GetStatus"));
        return actions;
    }

    private static IReadOnlyList<ActionData> FindTreeDecompose(
        string[] _, WorldState state) =>
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
        string[] _, WorldState __) =>
    [
        MakeAction("GetStatus"),
    ];

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
        string[] _, WorldState __) =>
    [
        MakeAction("GetStatus"),
    ];

    private static IReadOnlyList<ActionData> WaitDecompose(
        string[] _, WorldState __) =>
    [
        MakeAction("GetStatus"),
    ];

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
