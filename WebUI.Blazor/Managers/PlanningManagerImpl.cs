namespace WebUI.Blazor.Managers;

using Agent.Core;
using Agent.Core.Runtime;
using Agent.Planning;
using Microsoft.Extensions.Logging;

/// <summary>
/// Sprint 39 P2: Concrete implementation of <see cref="IPlanningManager"/>.
///
/// Wraps <see cref="IPlanner"/> and <see cref="IReplanGovernor"/> in a single
/// injectable planning component. Maintains a <c>_replanRequested</c> flag so
/// the execution pipeline can signal "please replan" without coupling to the planner.
///
/// Sprint 40 target: AgentBackgroundService.DispatchActionsAsync delegates PlanAsync
/// and stall-detection here, reducing ABS to a thin loop shell.
/// </summary>
public sealed class PlanningManagerImpl : IPlanningManager
{
    private readonly IPlanner         _planner;
    private readonly IReplanGovernor? _replanGovernor;
    private readonly ILogger<PlanningManagerImpl> _logger;

    private volatile bool _replanRequested;

    public PlanningManagerImpl(
        IPlanner planner,
        IReplanGovernor? replanGovernor,
        ILogger<PlanningManagerImpl> logger)
    {
        _planner        = planner;
        _replanGovernor = replanGovernor;
        _logger         = logger;
    }

    /// <inheritdoc/>
    public async Task<ActionPlan> PlanAsync(
        IGoal goal,
        WorldState state,
        CancellationToken ct = default)
    {
        _replanRequested = false; // clear flag at each plan boundary
        _logger.LogDebug("[planning] PlanAsync for goal {Goal}", goal.Name);
        var plan = await _planner.PlanAsync(goal, state, ct);
        return (ActionPlan)plan;
    }

    /// <summary>
    /// Sprint 57: Canonical plan path using <see cref="ExecutionContext"/>.
    /// Checks goal preconditions before delegating to the planner, and attaches
    /// postcondition metadata to the result plan when available.
    /// </summary>
    public async Task<ActionPlan> PlanAsync(
        ExecutionContext context,
        CancellationToken ct = default)
    {
        _replanRequested = false; // clear flag at each plan boundary

        if (context.IsIdle)
        {
            _logger.LogDebug("[planning] idle — no plan to generate");
            return new ActionPlan("idle", [], []);
        }

        var goal = context.Goal!;
        _logger.LogDebug("[planning] PlanAsync for goal {Goal} (freshInventory={Fresh})",
            goal.Name, context.HasFreshInventory);

        // Check goal preconditions when the goal implements IGoalPrecondition.
        if (goal is IGoalPrecondition precondition)
        {
            if (!precondition.CanAttempt(context, out var blockingReason))
            {
                _logger.LogWarning(
                    "[planning] precondition failed for {Goal}: {Reason}",
                    goal.Name, blockingReason);
                // Return an empty plan — the caller should treat this as blocked,
                // not failed, and potentially queue prerequisite acquisition.
                return new ActionPlan(goal.Name, goal.Phases, []);
            }
        }

        var plan = await _planner.PlanAsync(goal, context.State, ct);
        return (ActionPlan)plan;
    }

    /// <inheritdoc/>
    public void RequestReplan()
    {
        _replanRequested = true;
        _logger.LogDebug("[planning] replan requested via RequestReplan()");
    }

    /// <summary>
    /// True if <see cref="RequestReplan"/> has been called since the last <see cref="PlanAsync"/>.
    /// Read by the execution pipeline to break the action dispatch loop.
    /// </summary>
    public bool IsReplanRequested => _replanRequested;
}
