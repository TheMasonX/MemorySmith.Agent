namespace Agent.Core;

/// <summary>
/// Concrete IGoal implementation backed by constructor-injected predicates.
/// Use for quick in-code goal definitions; prefer named goal classes for
/// reusable or configurable goals (see Agent.Planning.Goals.*).
/// </summary>
public sealed class SimpleGoal(
    string name,
    string description,
    string[] phases,
    Func<WorldState, bool> isComplete,
    Func<WorldState, bool>? hasFailed = null) : IGoal
{
    public string Name { get; } = name;
    public string Description { get; } = description;
    public string[] Phases { get; } = phases;

    public bool IsComplete(WorldState state) => isComplete(state);
    public bool HasFailed(WorldState state) => hasFailed?.Invoke(state) ?? false;
}
