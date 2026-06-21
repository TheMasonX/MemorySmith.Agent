namespace Agent.Planning;

using Agent.Construction;
using Agent.Core;
using Agent.Planning.Goals;
using Microsoft.Extensions.Logging;

/// <summary>
/// Decomposes a BuildGoal into its construction phases.
/// Delegates to HtnTaskLibrary.DecomposeBuild with blueprint, blocks,
/// and origin coordinates read from world-state facts.
/// </summary>
public sealed class BuildGoalDecomposer(HtnTaskLibrary taskLibrary, ILogger<BuildGoalDecomposer> logger) : IGoalDecomposer
{
    public bool CanHandle(IGoal goal) => goal is BuildGoal;

    public ActionPlan Decompose(IGoal goal, WorldState state)
    {
        var bg = (BuildGoal)goal;

        var ox = ReadOriginFact(state, bg.Blueprint.Id, "x");
        var oy = ReadOriginFact(state, bg.Blueprint.Id, "y");
        var oz = ReadOriginFact(state, bg.Blueprint.Id, "z");

        var actions = taskLibrary.DecomposeBuild(
            bg.Blueprint, bg.Blocks, ox, oy, oz, state);

        return new ActionPlan(bg.Name, bg.Phases, actions);
    }

    /// <summary>
    /// Reads an integer build origin coordinate from world-state facts.
    /// Key format: "build:{blueprintId}:origin:{axis}".
    /// Returns 0 if the fact is absent or unparseable, and logs a warning.
    /// Sprint 28 P0-B: added LogWarning on missing/unparseable fact so silent (0,0,0)
    /// fallback is visible in logs. ResolveAutoOrigin will try live WorldState facts next.
    /// </summary>
    private int ReadOriginFact(WorldState state, string blueprintId, string axis)
    {
        var key = $"build:{blueprintId}:origin:{axis}";
        if (!state.Facts.TryGetValue(key, out var v))
        {
            logger.LogWarning(
                "Build origin fact missing or unparseable; defaulting to (0,0,0). Goal may build at wrong location. Key={Key}",
                key);
            return 0;
        }

        if (v is int i) return i;
        if (v is long l && l >= int.MinValue && l <= int.MaxValue) return (int)l;
        if (v is string s && int.TryParse(s, out var parsed)) return parsed;

        logger.LogWarning(
            "Build origin fact unparseable; defaulting to 0 for axis {Axis}. Value={Value}",
            axis, v);
        return 0;
    }
}
