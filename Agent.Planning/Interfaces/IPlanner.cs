namespace Agent.Planning;

using Agent.Core;

/// <summary>
/// Hybrid HTN/GOAP planner interface.
/// [XML doc]
/// </summary>
public interface IPlanner
{
    Task<IPlan> PlanAsync(IGoal goal, WorldState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replans using a structured context. Returns a <see cref="ReplanResult"/>
    /// that distinguishes success (with plan) from failure (with error message).
    ///
    /// TSK-0104: replaced nullable-IPlan return with typed ReplanResult.
    /// </summary>
    Task<ReplanResult> ReplanAsync(ReplanGoalContext context, CancellationToken cancellationToken = default);
}
