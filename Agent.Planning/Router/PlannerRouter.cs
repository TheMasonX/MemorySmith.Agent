namespace Agent.Planning;

using Agent.Core;

/// <summary>
/// Identifies the planner strategy a <see cref="PlannerRouter"/> may select for a goal.
/// </summary>
/// <remarks>
/// Implementation status as of Sprint 27 (2026-06-19):
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
/// Selects the appropriate planner or decomposer for a goal, and implements
/// <see cref="IPlanner"/> so the agent background service can use it directly.
///
/// Sprint 27 P0-D: <see cref="PlannerRouter"/> now implements <see cref="IPlanner"/>
/// (in addition to its existing <see cref="Select"/> method), allowing it to be registered
/// as the singleton <see cref="IPlanner"/> in DI. This wires the decomposer registry into
/// the production planning path for the first time:
/// <code>
///   AgentBackgroundService → IPlanner → PlannerRouter → DecomposerRegistry → [decomposer]
///                                                     ↓ fallback
///                                                   HtnPlanner (pure phase-by-phase)
/// </code>
///
/// <b>Currently implemented routing (as of Sprint 27):</b>
/// <code>
///   [IMPLEMENTED]  DecomposerRegistry.Find(goal) → DecomposerPlanner wrapping the found IGoalDecomposer
///   [IMPLEMENTED]  fallback                        → HtnPlanner
///   [ASPIRATIONAL] Goap                            → not wired; see PlannerStrategy.Goap
///   [ASPIRATIONAL] LlmAssisted                      → not wired; see PlannerStrategy.LlmAssisted
/// </code>
///
/// The <see cref="PlannerStrategy.Goap"/> and <see cref="PlannerStrategy.LlmAssisted"/> enum values
/// are declared as architectural placeholders only and are not consulted by <see cref="Select"/>.
/// Do not route to them until Phase 7-E begins.
/// </summary>
/// <remarks>
/// Sprint 28 P1-A: htnFallback parameter broadened from <c>HtnPlanner</c> to <c>IPlanner</c>
/// to allow injection of any fallback planner in tests (e.g. a recording stub).
/// In production, <c>HtnPlanner</c> is registered and resolved as the fallback via DI —
/// the type change is backward-compatible since <c>HtnPlanner : IPlanner</c>.
/// </remarks>
public sealed class PlannerRouter(DecomposerRegistry registry, IPlanner htnPlanner) : IPlanner
{
    /// <summary>
    /// Returns the best <see cref="IPlanner"/> for <paramref name="goal"/> given <paramref name="state"/>.
    ///
    /// Routing order (both are [IMPLEMENTED]):
    /// <list type="number">
    ///   <item>
    ///     [IMPLEMENTED] Check <see cref="DecomposerRegistry"/> — returns a <c>DecomposerPlanner</c>
    ///     wrapping the first registered <see cref="IGoalDecomposer"/> whose <c>CanHandle</c> returns true
    ///     (e.g. <c>BuildGoalDecomposer</c>, <c>GatherGoalDecomposer</c>, <c>CraftItemGoalDecomposer</c>).
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

    // ── IPlanner implementation ─────────────────────────────────────────────

    /// <summary>
    /// Plans by routing to the best available planner for <paramref name="goal"/>.
    /// Sprint 27 P0-D: exposes router as <see cref="IPlanner"/> so DI can register a
    /// single IPlanner that transparently dispatches through the decomposer registry.
    /// </summary>
    public Task<IPlan> PlanAsync(IGoal goal, WorldState state,
        CancellationToken ct = default)
        => Select(goal, state).PlanAsync(goal, state, ct);

    /// <summary>
    /// Replans by routing through <see cref="Select"/> using the original goal object when available.
    /// Sprint 28 P1-A: uses originalGoal to preserve the concrete goal type (IItemSpecGoal,
    /// BuildGoal, CraftItemGoal) for correct decomposer routing. Previously reconstructed a
    /// SimpleGoal shell which silently fell through to HtnPlanner for all decomposer-handled goals.
    ///
    /// TSK-0104: accepts <see cref="ReplanGoalContext"/> and returns <see cref="ReplanResult"/>.
    /// </summary>
    public Task<ReplanResult> ReplanAsync(ReplanGoalContext context,
        CancellationToken ct = default)
    {
        // Sprint 28 P1-A: use the original goal object when available to preserve
        // concrete type (IItemSpecGoal, BuildGoal, CraftItemGoal) for decomposer routing.
        // Reconstructing SimpleGoal from plan data silently routes all decomposer-handled
        // goals to HtnPlanner fallback instead.
        var routingGoal = context.OriginalGoal ?? new SimpleGoal(
            context.CurrentPlan.GoalName, "",
            [.. context.CurrentPlan.Phases],
            _ => false);
        return Select(routingGoal, context.State)
            .ReplanAsync(context, ct);
    }

    // ── Private adapter ──────────────────────────────────────────────────────

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

        public Task<ReplanResult> ReplanAsync(ReplanGoalContext context,
            CancellationToken ct = default)
        {
            // Use the original goal (with concrete type) if available; fall back to
            // reconstructing a SimpleGoal shell from the plan metadata.
            var goalToDecompose = context.OriginalGoal ?? new SimpleGoal(
                context.CurrentPlan.GoalName, "",
                [.. context.CurrentPlan.Phases],
                _ => false);
            var plan = decomposer.Decompose(goalToDecompose, context.State);
            return Task.FromResult(ReplanResult.Success(plan));
        }
    }
}
