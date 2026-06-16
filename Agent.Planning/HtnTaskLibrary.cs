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
/// </summary>
public sealed class HtnTaskLibrary
{
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
    /// Decomposes a <see cref="BuildGoal"/> into material-gather actions followed
    /// by PlaceBlock actions for every block in the blueprint.
    ///
    /// Material gathering: for each required material in <paramref name="blueprint"/>.Materials
    /// that can be directly mined (wood, stone, ore) and is not already in inventory,
    /// emit SearchMemory + Wander + MineBlock actions.
    ///
    /// Build phase: emit one PlaceBlock action per block in <paramref name="blocks"/>,
    /// ordered floor-first (Y ascending) by <see cref="BlueprintExecutor"/>.
    ///
    /// Crafted items (planks, slabs, torches, doors, chests, beds, glass) are not
    /// auto-gathered here — they must be pre-crafted or obtained via separate
    /// GatherItem goals. CraftItem automation is deferred to Phase 5.
    /// </summary>
    public IReadOnlyList<ActionData> DecomposeBuild(
        Blueprint blueprint,
        IReadOnlyList<PlacementBlock> blocks,
        int originX, int originY, int originZ,
        WorldState state)
    {
        var actions = new List<ActionData>();

        // ── Phase: GatherMaterials ────────────────────────────────────────────
        foreach (var material in blueprint.Materials)
        {
            // Only emit gather actions for directly-mineable raw blocks.
            // Crafted items (planks, slabs, torches, etc.) must be pre-prepared.
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

        // ── Phase: Navigate to build site ─────────────────────────────────────
        actions.Add(MakeAction("SearchMemory",
            ("query", $"flat area build location {blueprint.Name}")));
        actions.Add(MakeAction("MoveTo",
            ("x", (object?)originX),
            ("y", (object?)originY),
            ("z", (object?)originZ)));

        // ── Phase: Build — emit PlaceBlock for every block in order ───────────
        var executor = new BlueprintExecutor();
        actions.AddRange(executor.Execute(blocks, originX, originY, originZ));

        // ── Phase: Verify ─────────────────────────────────────────────────────
        actions.Add(MakeAction("GetStatus"));

        return actions;
    }

    // ── Decomposers ───────────────────────────────────────────────────────────

    private static IReadOnlyList<ActionData> GatherWoodDecompose(
        string[] parameters, WorldState state) =>
        // Delegate to the generic implementation using the built-in oak-log spec.
        // This keeps GatherWood backward-compatible while sharing the same logic.
        GatherItemDecompose(OakLogSpec, parameters, state);

    /// <summary>
    /// Generic item-gather decomposition. Generates SearchMemory + Wander +
    /// up to two MineBlock actions (one per source-block variant) + GetStatus.
    /// </summary>
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

        // Mine up to 2 source-block variants per planning cycle.
        // Additional variants are tried in subsequent replanning cycles.
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

    /// <summary>
    /// Single random wander step. Optional parameters: radius (default 20),
    /// maxDistanceFromSpawn (default 100).
    /// </summary>
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

    /// <summary>
    /// Three-step exploration: wander, observe surroundings, wander again.
    /// Keeps the bot within 100 blocks of spawn by default.
    /// </summary>
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
