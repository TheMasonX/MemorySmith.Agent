namespace Agent.Planning;

using Agent.Core;
using Agent.Planning.Goals;

/// <summary>
/// Decomposes a SurviveNightGoal by delegating to the "SurviveNight"
/// string-keyed task method in HtnTaskLibrary.
/// </summary>
public sealed class SurviveNightGoalDecomposer(HtnTaskLibrary taskLibrary) : IGoalDecomposer
{
    public bool CanHandle(IGoal goal) => goal is SurviveNightGoal;

    public ActionPlan Decompose(IGoal goal, WorldState state)
    {
        var actions = taskLibrary.Decompose("SurviveNight", [], state);
        return new ActionPlan(goal.Name, goal.Phases, actions);
    }
}
