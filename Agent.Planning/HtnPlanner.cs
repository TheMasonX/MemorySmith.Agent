namespace Agent.Planning;

using Agent.Construction;
using Agent.Core;
using Agent.Planning.Goals;

/// <summary>
/// Hybrid HTN planner implementation.
///
/// Strategy:
///   1. If the goal name has a direct decomposition in the task library,
///      use it (single-shot decomposition).
///   2. If the goal implements <see cref="IItemSpecGoal"/> (e.g. <see cref="GenericGatherGoal"/>),
///      delegate to <see cref="HtnTaskLibrary.DecomposeGatherItem"/> with the goal's
///      <see cref="ItemSpec"/>. This avoids registering one library entry per item ID
///      while still allowing the full ItemSpec to drive action generation.
///   3. If the goal is a <see cref="BuildGoal"/>, delegate to
///      <see cref="HtnTaskLibrary.DecomposeBuild"/> with the parsed block list and
///      build origin from world-state facts.
///   4. Otherwise, iterate through the goal's phases and decompose each phase
///      that has a known task method. Unknown phases are skipped.
///   5. If no actions result after all paths, throw — caller must fall back to
///      LLM (Phase 4 path, not yet implemented).
///
/// GOAP integration (Phase 4): when a phase fails at runtime, ReplanAsync will ask
/// the GOAP engine for an alternative sequence. Currently it restarts from scratch.
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
        //    Goal name is "Gather:{itemId}" which is not a fixed library task;
        //    delegate with the full ItemSpec instead.
        else if (goal is IItemSpecGoal isg)
        {
            actions.AddRange(library.DecomposeGatherItem(isg.Spec, [], state));
        }
        // 3. BuildGoal — blueprint-aware decomposition.
        //    Emits material-gather actions followed by PlaceBlock actions.
        //    Build origin is read from world-state facts; defaults to (0,0,0).
        else if (goal is BuildGoal bg)
        {
            var ox = ReadOriginFact(state, bg.Blueprint.Id, "x");
            var oy = ReadOriginFact(state, bg.Blueprint.Id, "y");
            var oz = ReadOriginFact(state, bg.Blueprint.Id, "z");
            actions.AddRange(library.DecomposeBuild(bg.Blueprint, bg.Blocks, ox, oy, oz, state));
        }
        else
        {
            // 4. Phase-by-phase decomposition.
            foreach (var phase in goal.Phases)
            {
                if (library.HasTask(phase))
                    actions.AddRange(library.Decompose(phase, [], state));
                // Unknown phases skipped — triggers LLM fallback in Phase 4
            }
        }

        if (actions.Count == 0)
            throw new InvalidOperationException(
                $"HtnPlanner could not decompose goal '{goal.Name}' " +
                $"(phases: [{string.Join(", ", goal.Phases)}]). " +
                "No matching task methods found. LLM fallback not yet implemented (Phase 4).");

        return Task.FromResult<IPlan>(new ActionPlan(goal.Name, goal.Phases.ToArray(), actions));
    }

    /// <summary>
    /// Context key prefixes that carry inter-action state across replans.
    /// Tools such as SearchMemoryTool, FindFlatAreaTool, CraftItemTool,
    /// BuildTask, and MoveToTool write results with these prefixes so
    /// subsequent actions can consume them. Transient keys (e.g. single-action
    /// scratch values) are not in this set and will be dropped.
    /// </summary>
    private static readonly string[] PreservedContextPrefixes =
        ["SearchMemory:", "CraftItem:", "FindFlatArea:", "Build:", "MoveTo:"];

    public async Task<IPlan?> ReplanAsync(
        IPlan currentPlan, WorldState state, string failureReason,
        CancellationToken cancellationToken = default)
    {
        // 1. Capture inter-action context entries from the previous plan
        //    whose keys start with one of the preserved prefixes.
        var preservedContext = new Dictionary<string, object?>();
        foreach (var action in currentPlan.Actions)
        {
            foreach (var (key, value) in action.Context)
            {
                if (Array.Exists(PreservedContextPrefixes, prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
                    preservedContext.TryAdd(key, value);
            }
        }

        // 2. Build a fresh plan from the original goal phases.
        //    Phase 3: simple full-restart replan from the original goal phases.
        //    Phase 4: GOAP will substitute alternative actions for the failed phase.
        var goal = new SimpleGoal(
            currentPlan.GoalName, "",
            [.. currentPlan.Phases],
            _ => false);

        try
        {
            var newPlan = await PlanAsync(goal, state, cancellationToken);

            // 3. Restore preserved context entries into the new plan's actions
            //    so inter-action state survives the replan.
            if (preservedContext.Count > 0)
            {
                foreach (var newAction in (newPlan as ActionPlan)?.Actions ?? newPlan.Actions)
                {
                    foreach (var (key, value) in preservedContext)
                        newAction.Context.TryAdd(key, value);
                }
            }

            return newPlan;
        }
        catch
        {
            // No decomposition available — caller falls back to idle or LLM (Phase 4)
            return null;
        }
    }

    /// <summary>
    /// Reads an integer build origin coordinate from world-state facts.
    /// Key format: "build:{blueprintId}:origin:{axis}".
    /// Returns 0 if the fact is absent or unparseable.
    /// </summary>
    private static int ReadOriginFact(WorldState state, string blueprintId, string axis)
    {
        var key = $"build:{blueprintId}:origin:{axis}";
        return state.Facts.TryGetValue(key, out var v)
            ? v switch
            {
                int i                                          => i,
                long l                                         => (int)l,
                string s when int.TryParse(s, out var parsed) => parsed,
                _                                              => 0,
            }
            : 0;
    }
}
