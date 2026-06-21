namespace Agent.Planning;

using Agent.Construction;
using Agent.Core;
using Agent.Planning.Goals;
using Microsoft.Extensions.Logging;

/// <summary>
/// Decomposes a BuildGoal into its construction phases.
/// Delegates to HtnTaskLibrary.DecomposeBuild with blueprint, blocks,
/// and origin coordinates.
///
/// Origin resolution priority (Sprint 35):
///   1. Explicit origin from BuildGoal (chat "build a house at 100 64 200").
///   2. World-state facts "build:{blueprintId}:origin:{axis}" (REST API or prior run).
///   3. Auto-detect: FindFlatArea action to locate nearest flat spot near the agent.
/// </summary>
public sealed class BuildGoalDecomposer(HtnTaskLibrary taskLibrary, ILogger<BuildGoalDecomposer> logger) : IGoalDecomposer
{
    public bool CanHandle(IGoal goal) => goal is BuildGoal;

    public ActionPlan Decompose(IGoal goal, WorldState state)
    {
        var bg = (BuildGoal)goal;

        // Sprint 35: explicit origin from chat takes precedence over stored facts.
        int ox, oy, oz;
        if (bg.HasExplicitOrigin)
        {
            ox = bg.OriginX ?? 0;
            oy = bg.OriginY ?? 0;
            oz = bg.OriginZ ?? 0;
            logger.LogInformation(
                "Using explicit build origin ({X},{Y},{Z}) for '{Blueprint}' from chat parameters.",
                ox, oy, oz, bg.Blueprint.Id);
        }
        else
        {
            ox = ReadOriginFact(state, bg.Blueprint.Id, "x", out var foundX);
            oy = ReadOriginFact(state, bg.Blueprint.Id, "y", out var foundY);
            oz = ReadOriginFact(state, bg.Blueprint.Id, "z", out var foundZ);

            if (foundX || foundY || foundZ)
            {
                logger.LogInformation(
                    "Using stored build origin ({X},{Y},{Z}) for '{Blueprint}' from world-state facts.",
                    ox, oy, oz, bg.Blueprint.Id);
            }
        }

        // requireOrigin=true when no explicit origin was given AND no stored facts exist.
        // This causes DecomposeBuild to emit a FindFlatArea action instead of defaulting to (0,0,0).
        var requireOrigin = !bg.HasExplicitOrigin;

        var actions = taskLibrary.DecomposeBuild(
            bg.Blueprint, bg.Blocks, ox, oy, oz, state, requireOrigin);

        return new ActionPlan(bg.Name, bg.Phases, actions);
    }

    /// <summary>
    /// Reads an integer build origin coordinate from world-state facts.
    /// Key format: "build:{blueprintId}:origin:{axis}".
    /// Returns 0 if the fact is absent or unparseable.
    /// Sets <paramref name="found"/> to true when the fact actually existed.
    /// Sprint 35: returns found flag instead of logging warning, since auto-detect
    /// is the expected fallback now (not a warning condition).
    /// </summary>
    private int ReadOriginFact(WorldState state, string blueprintId, string axis, out bool found)
    {
        var key = $"build:{blueprintId}:origin:{axis}";
        if (!state.Facts.TryGetValue(key, out var v))
        {
            found = false;
            return 0;
        }

        found = true;

        if (v is int i) return i;
        if (v is long l && l >= int.MinValue && l <= int.MaxValue) return (int)l;
        if (v is string s && int.TryParse(s, out var parsed)) return parsed;

        logger.LogWarning(
            "Build origin fact unparseable; defaulting to 0 for axis {Axis}. Value={Value}",
            axis, v);
        return 0;
    }
}
