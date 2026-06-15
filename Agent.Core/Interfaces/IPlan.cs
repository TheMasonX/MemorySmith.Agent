namespace Agent.Core;

/// <summary>
/// An ordered sequence of actions produced by the planner for a given goal.
/// The agent pops actions from the plan's action queue and dispatches them
/// via the tool engine.
/// </summary>
public interface IPlan
{
    string GoalName { get; }
    IReadOnlyList<string> Phases { get; }
    IReadOnlyList<ActionData> Actions { get; }
}
