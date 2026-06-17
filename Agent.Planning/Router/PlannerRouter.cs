namespace Agent.Planning;

using Agent.Core;

/// <summary>
/// Identifies the planner strategy a <see cref="PlannerRouter"/> may select for a goal.
/// </summary>
/// <remarks>
/// Implementation status as of Sprint 16 (2026-06-17):
/// <list type="bullet">
///   <item><see cref="Htn"/>           — [IMPLEMENTED] Always available as the final HTN fallback.</item>
///   <item><see cref="GoalDecomposer"/> — [IMPLEMENTED] Preferred path via <see cref="DecomposerRegistry"/>.</item>
///   <item><see cref="Goap"/>          — [ASPIRATIONAL — not implemented, not wired]. Reserved for Phase 7-E planner migration.</item>
///   <item><see cref="LlmAssisted"/>   — [ASPIRATIONAL — not implemented, not wired]. Reserved for Phase 7-E.</item>
/// </list>
/// See <c>Data/Pages/Architecture/planner-routing-status-20260617.md</c> for the full inventory.
/// </remarks>
public enum PlannerStrategy
{
    /// <summary>
    /// [IMPLEMENTED] HTN decomposition via <see cref="HtnTaskLibrary"/>.
    /// Final fallback when no registered decomposer matches the goal.
    /// </summary>
    Htn,

    /// <summary>
    /// [IMPLEMENTED] Registered <see cref="IGoalDecomposer"/> via <see cref="DecomposerRegistry"/>.
    /// Preferred path — checked first in <see cref="PlannerRouter.Select"/>.
    /// </summary>
    GoalDecomposer,

    /// <summary>
    /// [ASPIRATIONAL — not implemented, not wired]
    /// Goal Oriented Action Planning. Reserved for Phase 7-E planner migration.
    /// No code path in <see cref="PlannerRouter.Select"/> currently reads or routes to this value.
    /// Do not add routing logic for GOAP until Phase 7-E design is approved.
    /// </summary>
    Goap,

    /// <summary>
    /// [ASPIRATIONAL — not implemented, not wired]
    /// LLM-assisted planning for novel or ambiguous goals. Reserved for Phase 7-E.
    /// No code path in <see cref="PlannerRouter.Select"/> currently reads or routes to this value.
    /// Per D-003: deterministic-first — LLM is a last resort only; do not place it ahead of HTN.
    /// </summary>
    LlmAssisted,
}

/// <summary>
/// Selects the appropriate planner or decomposer for a goal.
/// Single point of dispatch — replaces the hardcoded 4-path switch in HtnPlanner.
///
/// <b>Currently implemented routing (as of Sprint 16):</b>
/// <code>
///   [IMPLEMENTED]  DecomposerRegistry.Find(goal) → DecomposerPlanner wrapping the found IGoalDecomposer
///   [IMPLEMENTED]  fallback                       → HtnPlanner
///   [ASPIRATIONAL] Goap                           → not wired; see PlannerStrategy.Goap
///   [ASPIRATIONAL] LlmAssisted                    → not wired; see PlannerStrategy.LlmAssisted
/// </code>
///
/// The <see cref="PlannerStrategy.Goap"/> and <see cref="PlannerStrategy.LlmAssisted"/> enum values
/// are declared as architectural placeholders only and are not consulted by <see cref="Select"/>.
/// Do not route to them until Phase 7-E begins.
/// </summary>
public sealed class PlannerRouter(DecomposerRegistry registry, HtnPlanner htnPlanner)
{
    /// <summary>
    /// Returns the best <see cref="IPlanner"/> for <paramref name="goal"/> given <paramref name="state"/>.
    ///
    /// Routing order (both are [IMPLEMENTED]):
    /// <list type="number">
    ///   <item>
    ///     [IMPLEMENTED] Check <see cref="DecomposerRegistry"/> — returns a <c>DecomposerPlanner</c>
    ///     wrapping the first registered <see cref="IGoalDecomposer"/> whose <c>CanHandle</c> returns true
    ///     (e.g. <c>BuildGoalDecomposer</c>, <c>GatherGoalDecomposer</c>).
    ///   </item>
    ///   <item>
    ///     [IMPLEMENTED] Fallback to <see cref="HtnPlanner"/> for all other goals.
    ///   </item>
    /// </list>
    /// </summary>
    public IPlanner Select(IGoal goal, WorldState state)
    {
        // [IMPLEMENTED] Prefer a registered decomposer
        if (registry.Find(goal) is { } decomposer)
            return new DecomposerPlanner(decomposer);

        // [IMPLEMENTED] Fallback to HTN
        return htnPlanner;
    }

    /// <summary>
    /// Thin adapter that exposes a single <see cref="IGoalDecomposer"/> as an <see cref="IPlanner"/>.
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
