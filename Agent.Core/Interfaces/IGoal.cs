namespace Agent.Core;

/// <summary>
/// A high-level agent objective. Goals are evaluated each reasoning cycle.
/// When IsComplete returns true the agent idles; when HasFailed returns true
/// the planner is asked to replan or the user is alerted.
/// </summary>
public interface IGoal
{
    string Name { get; }
    string Description { get; }
    string[] Phases { get; }

    /// <summary>
    /// Nullable failure reason. <see langword="null"/> means the goal hasn't
    /// failed yet or no reason was recorded. Set by the agent service when
    /// <see cref="HasFailed"/> returns <see langword="true"/>.
    /// </summary>
    string? FailureReason { get; }

    bool IsComplete(WorldState state);
    bool HasFailed(WorldState state);
}
