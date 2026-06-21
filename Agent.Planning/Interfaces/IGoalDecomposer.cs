namespace Agent.Planning;

using Agent.Core;

/// <summary>
/// A decomposer that knows how to break down a specific kind of goal into actions.
/// This replaces the hardcoded TaskDecomposer delegate with a discoverable interface.
/// </summary>
public interface IGoalDecomposer
{
    /// <summary>Whether this decomposer handles the given goal type.</summary>
    bool CanHandle(IGoal goal);

    /// <summary>Decompose the goal into an ordered action plan.</summary>
    ActionPlan Decompose(IGoal goal, WorldState state);
}
