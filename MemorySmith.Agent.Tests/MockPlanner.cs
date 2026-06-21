using Agent.Core;
using Agent.Planning;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// In-memory IPlanner for test isolation.
/// Returns a configurable plan or an empty plan if none is set.
/// Tracks all PlanAsync / ReplanAsync calls for assertion.
/// </summary>
public sealed class MockPlanner : IPlanner
{
    public List<(IGoal Goal, WorldState State)> PlanCalls { get; } = [];
    public List<(IPlan Plan, WorldState State, string Reason)> ReplanCalls { get; } = [];

    /// <summary>Plan returned by PlanAsync. Defaults to an empty action plan.</summary>
    public IPlan? PlanToReturn { get; set; }

    /// <summary>Plan returned by ReplanAsync. Defaults to null.</summary>
    public IPlan? ReplanToReturn { get; set; }

    public Task<IPlan> PlanAsync(IGoal goal, WorldState state,
        CancellationToken cancellationToken = default)
    {
        PlanCalls.Add((goal, state));
        var plan = PlanToReturn
            ?? new ActionPlan(goal.Name, goal.Phases, []);
        return Task.FromResult(plan);
    }

    public Task<IPlan?> ReplanAsync(IPlan currentPlan, WorldState state,
        string failureReason, CancellationToken cancellationToken = default,
        IGoal? originalGoal = null)
    {
        ReplanCalls.Add((currentPlan, state, failureReason));
        return Task.FromResult(ReplanToReturn);
    }
}
