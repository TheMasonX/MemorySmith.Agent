namespace Agent.Planning;

using Agent.Core;

/// <summary>
/// Hierarchical Task Network planner stub.
///
/// Phase 3 implementation will:
///   1. Check if the goal matches a known HTN task library entry.
///   2. If yes: decompose using the predefined method (deterministic).
///   3. If no: call the LLM (via IChatClient) to produce a JSON plan.
///   4. For each phase, apply GOAP if sub-task preconditions cannot be met.
///
/// Example LLM response:
/// {
///   "goal": "BuildCathedral",
///   "phases": ["GatherStone", "LayFoundation", "BuildWalls", "FinishRoof"]
/// }
/// </summary>
public class HtnPlanner : IPlanner
{
    public Task<IPlan> PlanAsync(IGoal goal, WorldState state, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("HTN planner — Phase 3 implementation pending.");

    public Task<IPlan?> ReplanAsync(IPlan currentPlan, WorldState state, string failureReason, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("HTN replanner — Phase 3 implementation pending.");
}
