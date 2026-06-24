namespace Agent.Planning;

using System.Text.Json;
using Agent.Construction;
using Agent.Core;
using Agent.Planning.Goals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Hybrid HTN planner implementation.
///
/// Sprint 27 P0-D: type-switch branches for <see cref="IItemSpecGoal"/>,
/// <see cref="BuildGoal"/>, and <see cref="CraftItemGoal"/> have been removed.
/// Those are now handled by registered <see cref="IGoalDecomposer"/> implementations
/// (<see cref="GatherGoalDecomposer"/>, <see cref="BuildGoalDecomposer"/>,
/// <see cref="CraftItemGoalDecomposer"/>) through the <see cref="PlannerRouter"/>
/// which checks the <see cref="DecomposerRegistry"/> first.
///
/// <see cref="HtnPlanner"/> is now a pure fallback that handles goals by:
///   1. Direct task-library name match.
///   2. Phase-by-phase decomposition.
///   3. Throw if no actions result (caller falls back to LLM).
///
/// In production the agent loop uses <see cref="IPlanner"/> â†’ <see cref="PlannerRouter"/>
/// which routes typed goals to their decomposers before reaching <see cref="HtnPlanner"/>.
/// <see cref="HtnPlanner"/> therefore only receives goals with no registered decomposer.
/// </summary>
public sealed class HtnPlanner(HtnTaskLibrary library, ILogger<HtnPlanner>? logger = null) : IPlanner
{
    private readonly ILogger<HtnPlanner> _logger = logger ??
        Microsoft.Extensions.Logging.Abstractions.NullLogger<HtnPlanner>.Instance;

    // â”€â”€ PlanAsync â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public Task<IPlan> PlanAsync(
        IGoal goal, WorldState state, CancellationToken cancellationToken = default)
    {
        var actions = new List<ActionData>();

        if (goal is BuildGoal buildGoal)
        {
            var originX = ReadOriginFact(state, buildGoal.Blueprint.Id, "x");
            var originY = ReadOriginFact(state, buildGoal.Blueprint.Id, "y");
            var originZ = ReadOriginFact(state, buildGoal.Blueprint.Id, "z");

            if (state.IsCreativeMode)
            {
                actions.AddRange(CreateCreativeBuildActions(buildGoal, state, originX, originY, originZ));
            }
            else
            {
                actions.AddRange(library.DecomposeBuild(
                    buildGoal.Blueprint,
                    buildGoal.Blocks,
                    originX,
                    originY,
                    originZ,
                    state));
            }
        }
        else if (goal is CraftItemGoal craftGoal)
        {
            actions.AddRange(library.DecomposeCraftItem(craftGoal.ItemId, craftGoal.Count, state));
        }
        else if (goal is IItemSpecGoal itemSpecGoal)
        {
            actions.AddRange(library.DecomposeGatherItem(
                itemSpecGoal.Spec,
                [itemSpecGoal.TargetCount.ToString()],
                state));
        }
        else if (library.HasTask(goal.Name))
        {
            actions.AddRange(library.Decompose(goal.Name, [], state));
        }
        else
        {
            // 2. Phase-by-phase decomposition (pure HTN fallback).
            // Typed goals now decompose directly above for compatibility with the
            // older tests and direct callers that still invoke HtnPlanner directly.
            foreach (var phase in goal.Phases)
                if (library.HasTask(phase))
                    actions.AddRange(library.Decompose(phase, [], state));
        }

        if (actions.Count == 0)
            throw new InvalidOperationException(
                $"HtnPlanner could not decompose goal '{goal.Name}' " +
                $"(phases: [{string.Join(", ", goal.Phases)}]). " +
                "No matching task methods found. LLM fallback not yet implemented (Phase 4).");

        return Task.FromResult<IPlan>(new ActionPlan(goal.Name, goal.Phases.ToArray(), actions));
    }

    // Sprint 44 (TSK-0080): SearchMemory: removed â€” results were never consumed.
    // PreservedContextPrefixes keeps CraftItem:, FindFlatArea:, Build:, MoveTo: results.
    private static readonly string[] PreservedContextPrefixes =
        ["CraftItem:", "FindFlatArea:", "Build:", "MoveTo:"];

    private static IReadOnlyList<ActionData> CreateCreativeBuildActions(
        BuildGoal buildGoal, WorldState state, int originX, int originY, int originZ)
    {
        var actions = new List<ActionData>
        {
            // Sprint 44 (TSK-0080): SearchMemory removed â€” results were never consumed downstream.
            MakeAction("MoveTo", ("x", (object?)originX), ("y", (object?)originY), ("z", (object?)originZ)),
        };

        var progressKey = BuildFactKeys.BuildProgressIndex(buildGoal.Blueprint.Name);
        var checkpointIndex = 0;
        if (TryGetIntFactFromState(state, progressKey, out var lastPlaced))
            checkpointIndex = lastPlaced + 1;

        var executor = new BlueprintExecutor();
        var blockActions = executor.Execute(buildGoal.Blocks, originX, originY, originZ);

        for (var i = checkpointIndex; i < blockActions.Count; i++)
        {
            var placeAction = blockActions[i];
            placeAction.Context[BuildFactKeys.PlaceBlockProgressBlueprintId] = buildGoal.Blueprint.Name;
            placeAction.Context[BuildFactKeys.PlaceBlockProgressBlockIndex] = i;
            actions.Add(placeAction);
        }

        actions.Add(MakeAction("GetStatus"));
        return actions;
    }

    private static int ReadOriginFact(WorldState state, string blueprintId, string axis)
    {
        var key = $"build:{blueprintId}:origin:{axis}";
        if (!state.Facts.TryGetValue(key, out var v))
            return 0;

        return v switch
        {
            int i => i,
            long l when l >= int.MinValue && l <= int.MaxValue => (int)l,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => 0,
        };
    }

    private static ActionData MakeAction(
        string tool, params (string key, object? value)[] args)
    {
        var action = new ActionData { Tool = tool };
        foreach (var (key, value) in args)
            action.Arguments[key] = value;
        return action;
    }

    private static bool TryGetIntFactFromState(WorldState state, string key, out int result)
    {
        result = 0;
        if (!state.Facts.TryGetValue(key, out var v)) return false;
        return v switch
        {
            int i => (result = i) != int.MinValue,
            long l => (result = (int)l) != int.MinValue,
            double d => (result = (int)d) != int.MinValue,
            string s => int.TryParse(s, out result),
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.TryGetInt32(out result),
            _ => false,
        };
    }

    // â”€â”€ ReplanAsync (TSK-0104: ReplanResult + ReplanGoalContext) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public async Task<ReplanResult> ReplanAsync(
        ReplanGoalContext context, CancellationToken cancellationToken = default)
    {
        // Sprint 32 P2-2: thread failureReason to log so it is visible in telemetry.
        // Two independent audits (Sprint 25 deep-code-audit + Sprint 32 refinement-audit)
        // both flagged this parameter as accepted but unused. Adaptive replanning that
        // acts on the reason (e.g. choose alternative decomposer) is future scope.
        if (!string.IsNullOrWhiteSpace(context.FailureReason))
            _logger.LogInformation(
                "ReplanAsync triggered for goal '{GoalName}' with reason: {FailureReason}",
                context.CurrentPlan.GoalName, context.FailureReason);

        var preservedContext = new Dictionary<string, object?>();
        foreach (var action in context.CurrentPlan.Actions)
            foreach (var (key, value) in action.Context)
                if (Array.Exists(PreservedContextPrefixes,
                        prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
                    preservedContext.TryAdd(key, value);

        var goal = new SimpleGoal(
            context.CurrentPlan.GoalName, "",
            [.. context.CurrentPlan.Phases],
            _ => false);

        try
        {
            var newPlan = await PlanAsync(goal, context.State, cancellationToken);

            if (preservedContext.Count > 0)
                foreach (var newAction in (newPlan as ActionPlan)?.Actions ?? newPlan.Actions)
                    foreach (var (key, value) in preservedContext)
                        newAction.Context.TryAdd(key, value);

            return ReplanResult.Success(newPlan);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HtnPlanner.ReplanAsync: Planner error for goal '{Goal}'",
                context.CurrentPlan.GoalName);
            return ReplanResult.Failure($"Planner error: {ex.Message}");
        }
    }
}
