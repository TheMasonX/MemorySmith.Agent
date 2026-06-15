namespace Agent.Planning;

using Agent.Core;

/// <summary>
/// Creates typed IGoal instances from a string name and optional parameters.
/// Used by REST endpoints to translate user commands into goals the planner
/// can process.
/// </summary>
public interface IGoalFactory
{
    /// <summary>
    /// Returns null if the goal name is not registered.
    /// </summary>
    IGoal? Create(
        string goalName,
        IReadOnlyDictionary<string, object?>? parameters = null);

    IReadOnlyList<string> RegisteredGoals { get; }
}
