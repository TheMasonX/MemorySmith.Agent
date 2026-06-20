namespace Agent.Planning;

using Agent.Core;

/// <summary>
/// Hybrid HTN/GOAP planner interface.
/// [XML doc]
/// </summary>
public interface IPlanner
{
    Task<IPlan> PlanAsync(IGoal goal, WorldState state, CancellationToken cancellationToken = default);
    Task<IPlan?> ReplanAsync(IPlan currentPlan, WorldState state, string failureReason, CancellationToken cancellationToken = default, IGoal? originalGoal = null);
}
