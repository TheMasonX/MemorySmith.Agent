namespace Agent.Core;

/// <summary>
/// Concrete IPlan returned by the planner.
/// Holds an immutable ordered list of ActionData items that the
/// AgentBackgroundService dequeues and dispatches one-by-one.
/// </summary>
public sealed class ActionPlan(
    string goalName,
    string[] phases,
    IReadOnlyList<ActionData> actions) : IPlan
{
    public string GoalName { get; } = goalName;
    public IReadOnlyList<string> Phases { get; } = phases;
    public IReadOnlyList<ActionData> Actions { get; } = actions;

    /// <summary>Returns true when the plan has no actions to execute.</summary>
    public bool IsEmpty => Actions.Count == 0;

    public override string ToString() =>
        $"ActionPlan({GoalName}, {Actions.Count} actions, phases=[{string.Join(", ", Phases)}])";
}
