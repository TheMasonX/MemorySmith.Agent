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
    public bool CanHandle(IGoal goal) => goal is IBuildGoal;

    public ActionPlan Decompose(IGoal goal, WorldState state)
    {
        var bg = (IBuildGoal)goal;

        // Sprint 35: explicit origin from chat takes precedence over stored facts.
        // Sprint 37: when explicit origin is given, it's used as the scan center for
        // FindFlatArea to find the nearest flat ground near those coordinates, rather
        // than building directly at the specified position.
        // TSK-0103: origin consolidated into BuildOrigin value object.
        int ox, oy, oz;
        if (bg.HasExplicitOrigin)
        {
            ox = bg.Origin!.X;
            oy = bg.Origin!.Y;
            oz = bg.Origin!.Z;
            logger.LogInformation(
                "Using explicit build origin ({X},{Y},{Z}) for '{Blueprint}' from chat parameters " +
                "— scanning for flat ground near this location.",
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

        // Sprint 37: always requireOrigin=true when explicit origin is given, so
        // DecomposeBuild emits FindFlatArea with scanOrigin set to those coords.
        // TSK-0107: construct BuildOrigin instead of passing raw ints to eliminate
        // the (0,0,0) sentinel ambiguity. The source is derived from origin provenance.
        var source = bg.HasExplicitOrigin ? BuildOriginSource.Explicit : BuildOriginSource.AutoScanned;
        var buildOrigin = new BuildOrigin(ox, oy, oz, source);
        var requireOrigin = true;

        var actions = taskLibrary.DecomposeBuild(
            bg.Blueprint, bg.Blocks, buildOrigin, state, requireOrigin);

        // Sprint 52 diagnostic: log RLE-compressed action list from BuildGoalDecomposer
        // to help trace where MoveTo enters the pipeline (TSK-0121).
        // Inline RLE to avoid coupling to AgentBackgroundService.RleCompressActions.
        var actionSummary = RleCompress(actions);
        logger.LogDebug(
            "[BuildGoalDecomposer] DecomposeBuild returned {Count} actions: [{Sequence}]",
            actions.Count, actionSummary);

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

    /// <summary>RLE-compresses an action list for compact logging.</summary>
    private static string RleCompress(IReadOnlyList<ActionData> actions)
    {
        var sb = new System.Text.StringBuilder();
        string? current = null;
        int count = 0;
        foreach (var a in actions)
        {
            var tool = a.Tool;
            if (tool == current) { count++; }
            else
            {
                if (current is not null)
                {
                    if (sb.Length > 0) sb.Append(" → ");
                    sb.Append(current);
                    if (count > 1) sb.Append('×').Append(count);
                }
                current = tool;
                count = 1;
            }
        }
        if (current is not null)
        {
            if (sb.Length > 0) sb.Append(" → ");
            sb.Append(current);
            if (count > 1) sb.Append('×').Append(count);
        }
        return sb.ToString();
    }
}
