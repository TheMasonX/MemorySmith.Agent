namespace Agent.Planning;

using Agent.Construction;
using Agent.Core;
using Agent.Planning.Goals;

/// <summary>
/// Hybrid HTN planner implementation.
///
/// Strategy:
///   1. Direct task-library name match.
///   2. IItemSpecGoal (e.g. GenericGatherGoal) -> DecomposeGatherItem.
///   3. BuildGoal -> DecomposeBuild with requireOrigin:true (Sprint 13 D3).
///   4. CraftItemGoal -> DecomposeCraftItem (Sprint 13).
///   5. Phase-by-phase decomposition.
///   6. Throw if no actions result (caller falls back to LLM).
///
/// Sprint 22 P1: IItemSpecGoal branch now passes the goal's TargetCount as
/// parameters[0] to GatherItemDecompose. Previously the empty array [] caused
/// the decomposer to default to count=10 regardless of the user's requested quantity.
/// </summary>
public sealed class HtnPlanner(HtnTaskLibrary library) : IPlanner
{
    public Task<IPlan> PlanAsync(
        IGoal goal, WorldState state, CancellationToken cancellationToken = default)
    {
        var actions = new List<ActionData>();

        // 1. Direct task-library decomposition by goal name.
        if (library.HasTask(goal.Name))
        {
            actions.AddRange(library.Decompose(goal.Name, [], state));
        }
        // 2. IItemSpecGoal — ItemSpec-aware decomposition (e.g. GenericGatherGoal).
        //    Sprint 22 P1: pass TargetCount so GatherItemDecompose emits the correct
        //    MineBlock count (fixes "get 100 sand" emitting count=10 plan).
        else if (goal is IItemSpecGoal isg)
        {
            var parameters = isg is GenericGatherGoal ggg
                ? new[] { ggg.TargetCount.ToString() }
                : Array.Empty<string>();
            actions.AddRange(library.DecomposeGatherItem(isg.Spec, parameters, state));
        }
        // 3. BuildGoal — blueprint-aware decomposition.
        //    Sprint 13 D3: pass requireOrigin:true — if no valid build origin is available,
        //    DecomposeBuild returns a single FindFlatArea action so the scanner can locate
        //    a flat site before the actual build plan begins.
        else if (goal is BuildGoal bg)
        {
            var ox = ReadOriginFact(state, bg.Blueprint.Id, "x");
            var oy = ReadOriginFact(state, bg.Blueprint.Id, "y");
            var oz = ReadOriginFact(state, bg.Blueprint.Id, "z");
            actions.AddRange(library.DecomposeBuild(bg.Blueprint, bg.Blocks, ox, oy, oz, state,
                requireOrigin: true));
        }
        // 4. CraftItemGoal — crafting decomposition (Sprint 13).
        else if (goal is CraftItemGoal cig)
        {
            actions.AddRange(library.DecomposeCraftItem(cig.ItemId, cig.Count, state));
        }
        else
        {
            // 5. Phase-by-phase decomposition.
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

    private static readonly string[] PreservedContextPrefixes =
        ["SearchMemory:", "CraftItem:", "FindFlatArea:", "Build:", "MoveTo:"];

    public async Task<IPlan?> ReplanAsync(
        IPlan currentPlan, WorldState state, string failureReason,
        CancellationToken cancellationToken = default)
    {
        var preservedContext = new Dictionary<string, object?>();
        foreach (var action in currentPlan.Actions)
            foreach (var (key, value) in action.Context)
                if (Array.Exists(PreservedContextPrefixes,
                        prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
                    preservedContext.TryAdd(key, value);

        var goal = new SimpleGoal(
            currentPlan.GoalName, "",
            [.. currentPlan.Phases],
            _ => false);

        try
        {
            var newPlan = await PlanAsync(goal, state, cancellationToken);

            if (preservedContext.Count > 0)
                foreach (var newAction in (newPlan as ActionPlan)?.Actions ?? newPlan.Actions)
                    foreach (var (key, value) in preservedContext)
                        newAction.Context.TryAdd(key, value);

            return newPlan;
        }
        catch
        {
            return null;
        }
    }

    private static int ReadOriginFact(WorldState state, string blueprintId, string axis)
    {
        var key = $"build:{blueprintId}:origin:{axis}";
        return state.Facts.TryGetValue(key, out var v)
            ? v switch
            {
                int i                                          => i,
                long l                                         => (int)l,
                string s when int.TryParse(s, out var parsed)  => parsed,
                _                                              => 0,
            }
            : 0;
    }
}
