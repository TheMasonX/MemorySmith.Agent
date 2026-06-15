namespace Agent.Planning;

using Agent.Core;

/// <summary>
/// Hybrid HTN/GOAP planner interface.
///
/// HTN (Hierarchical Task Network): breaks high-level goals into ordered
/// sub-task sequences using a predefined method library. Fast and predictable
/// for known task patterns (GatherWood, BuildHouse, etc.).
///
/// GOAP (Goal-Oriented Action Planning): plans backward from goal to actions
/// using precondition/effect pairs. Used for ad-hoc problems where no HTN
/// method exists (e.g. "survive the night").
///
/// The LLM is called sparingly: only for novel goals or after repeated failure,
/// to produce a JSON plan with goal and phases. Deterministic methods handle
/// the sub-task decomposition.
/// </summary>
public interface IPlanner
{
    Task<IPlan> PlanAsync(IGoal goal, WorldState state, CancellationToken cancellationToken = default);
    Task<IPlan?> ReplanAsync(IPlan currentPlan, WorldState state, string failureReason, CancellationToken cancellationToken = default);
}
