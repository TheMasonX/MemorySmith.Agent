namespace Agent.Planning;

using Agent.Core;

/// <summary>
/// Dynamic planner selection strategy.
/// Currently: DecomposerRegistry → HTN fallback.
/// Future: GOAP, LLM-assisted.
/// </summary>
public enum PlannerStrategy { Htn, GoalDecomposer, Goap, LlmAssisted }

/// <summary>
/// Selects the appropriate planner/decomposer for a goal.
/// Single point of dispatch — replaces the hardcoded 4-path switch in HtnPlanner.
/// </summary>
public sealed class PlannerRouter(DecomposerRegistry registry, HtnPlanner htnPlanner)
{
    public IPlanner Select(IGoal goal, WorldState state)
    {
        // Prefer registered decomposer
        if (registry.Find(goal) is { } decomposer)
            return new DecomposerPlanner(decomposer);

        // Fallback to HTN
        return htnPlanner;
    }

    /// <summary>
    /// Thin adapter that wraps a single IGoalDecomposer as an IPlanner.
    /// </summary>
    private sealed class DecomposerPlanner(IGoalDecomposer decomposer) : IPlanner
    {
        public Task<IPlan> PlanAsync(IGoal goal, WorldState state,
            CancellationToken ct = default)
        {
            var plan = decomposer.Decompose(goal, state);
            return Task.FromResult<IPlan>(plan);
        }

        public Task<IPlan?> ReplanAsync(IPlan currentPlan, WorldState state,
            string failureReason, CancellationToken ct = default)
        {
            // Reconstruct a minimal goal shell and re-decompose.
            // The decomposer doesn't need the full goal — it needs the
            // goal name and phases from the current plan to rebuild.
            var goal = new SimpleGoal(
                currentPlan.GoalName, "",
                [.. currentPlan.Phases],
                _ => false);
            var plan = decomposer.Decompose(goal, state);
            return Task.FromResult<IPlan?>(plan);
        }
    }
}
