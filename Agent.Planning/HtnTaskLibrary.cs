namespace Agent.Planning;

using Agent.Core;

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
/// </summary>
public sealed class HtnTaskLibrary
{
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

    // ── Decomposers ───────────────────────────────────────────────────────────

    private static IReadOnlyList<ActionData> GatherWoodDecompose(
        string[] parameters, WorldState state)
    {
        var count = parameters.Length > 0 && int.TryParse(parameters[0], out var c) ? c : 10;

        // Wander first so the bot explores a new area before mining.
        // This prevents the loop from repeatedly mining a depleted patch.
        // Radius 40 gives good coverage; maxDistanceFromSpawn 200 keeps the bot
        // from straying too far. Phase 4 (TSK-0004) will replace Wander with a
        // context-injected MoveTo using coordinates from the SearchMemory result.
        return
        [
            MakeAction("SearchMemory", ("query", "wood trees oak log location nearby")),
            MakeAction("Wander",   ("radius", (object?)40), ("maxDistanceFromSpawn", (object?)200)),
            MakeAction("MineBlock", ("block", "minecraft:oak_log"), ("count", (object?)count)),
            MakeAction("GetStatus"),
        ];
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
            MakeAction("MineBlock", ("block", "minecraft:oak_log"), ("count", (object?)count)),
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
