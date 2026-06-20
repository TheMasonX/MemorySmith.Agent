namespace Agent.Planning;

using Agent.Core;
using Agent.Planning.Goals;
using Microsoft.Extensions.Logging;

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
/// In production the agent loop uses <see cref="IPlanner"/> → <see cref="PlannerRouter"/>
/// which routes typed goals to their decomposers before reaching <see cref="HtnPlanner"/>.
/// <see cref="HtnPlanner"/> therefore only receives goals with no registered decomposer.
/// </summary>
public sealed class HtnPlanner(HtnTaskLibrary library, ILogger<HtnPlanner>? logger = null) : IPlanner
{
    private readonly ILogger<HtnPlanner> _logger = logger ??
        Microsoft.Extensions.Logging.Abstractions.NullLogger<HtnPlanner>.Instance;

    // ── PlanAsync ─────────────────────────────────────────────────────────────
    public Task<IPlan> PlanAsync(
        IGoal goal, WorldState state, CancellationToken cancellationToken = default)
    {
        var actions = new List<ActionData>();

        // 1. Direct task-library decomposition by goal name.
        if (library.HasTask(goal.Name))
        {
            actions.AddRange(library.Decompose(goal.Name, [], state));
        }
        else
        {
            // 2. Phase-by-phase decomposition (pure HTN fallback).
            // All typed goal branches (IItemSpecGoal, BuildGoal, CraftItemGoal) have been
            // moved to registered IGoalDecomposer implementations and are intercepted by
            // PlannerRouter before reaching this method.
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

    // ── ReplanAsync ───────────────────────────────────────────────────────────
    public async Task<IPlan?> ReplanAsync(
        IPlan currentPlan, WorldState state, string failureReason,
        CancellationToken cancellationToken = default, IGoal? originalGoal = null)
    {
        // Sprint 32 P2-2: thread failureReason to log so it is visible in telemetry.
        // Two independent audits (Sprint 25 deep-code-audit + Sprint 32 refinement-audit)
        // both flagged this parameter as accepted but unused. Adaptive replanning that
        // acts on the reason (e.g. choose alternative decomposer) is future scope.
        if (!string.IsNullOrWhiteSpace(failureReason))
            _logger.LogInformation(
                "ReplanAsync triggered for goal '{GoalName}' with reason: {FailureReason}",
                currentPlan.GoalName, failureReason);

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
}
