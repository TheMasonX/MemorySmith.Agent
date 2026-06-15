namespace Agent.Planning;

using Agent.Core;

/// <summary>
/// Hybrid HTN planner implementation.
///
/// Strategy:
///   1. If the goal name has a direct decomposition in the task library,
///      use it (single-shot decomposition).
///   2. Otherwise, iterate through the goal's phases and decompose each
///      phase that has a known task method. Unknown phases are skipped.
///   3. If no actions result after trying both paths, throw — caller must
///      fall back to LLM (Phase 4 path, not yet implemented).
///
/// GOAP integration (Phase 4): when a specific phase fails at runtime,
/// HtnPlanner.ReplanAsync will ask the GOAP engine to find an alternative
/// action sequence for that phase. Currently, ReplanAsync just restarts
/// the plan from scratch.
/// </summary>
public sealed class HtnPlanner(HtnTaskLibrary library) : IPlanner
{
    public Task<IPlan> PlanAsync(
        IGoal goal, WorldState state, CancellationToken cancellationToken = default)
    {
        var actions = new List<ActionData>();

        // 1. Try direct goal decomposition
        if (library.HasTask(goal.Name))
        {
            actions.AddRange(library.Decompose(goal.Name, [], state));
        }
        else
        {
            // 2. Phase-by-phase decomposition
            foreach (var phase in goal.Phases)
            {
                if (library.HasTask(phase))
                    actions.AddRange(library.Decompose(phase, [], state));
                // Unknown phases skipped — will trigger LLM fallback in Phase 4
            }
        }

        if (actions.Count == 0)
            throw new InvalidOperationException(
                $"HtnPlanner could not decompose goal '{goal.Name}' " +
                $"(phases: [{string.Join(", ", goal.Phases)}]). " +
                "No matching task methods found. LLM fallback not yet implemented (Phase 4).");

        return Task.FromResult<IPlan>(new ActionPlan(goal.Name, goal.Phases, actions));
    }

    public async Task<IPlan?> ReplanAsync(
        IPlan currentPlan, WorldState state, string failureReason,
        CancellationToken cancellationToken = default)
    {
        // Phase 3: simple full-restart replan from the original goal phases.
        // Phase 4: GOAP will substitute alternative actions for the failed phase
        //   e.g. "path blocked on MoveToTree" → GOAP finds another route.
        var goal = new SimpleGoal(
            currentPlan.GoalName, "",
            [.. currentPlan.Phases],
            _ => false);

        try
        {
            return await PlanAsync(goal, state, cancellationToken);
        }
        catch
        {
            // No decomposition available — caller falls back to idle or LLM (Phase 4)
            return null;
        }
    }
}
