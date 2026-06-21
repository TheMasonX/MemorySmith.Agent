namespace Agent.Planning;

using Agent.Core;

/// <summary>
/// Registry of IGoalDecomposer implementations. The planner queries this
/// to find the right decomposer for a goal, rather than hardcoding a switch.
/// Thread-safe.
/// </summary>
public sealed class DecomposerRegistry
{
    private readonly List<IGoalDecomposer> _decomposers = [];

    public void Register(IGoalDecomposer decomposer)
    {
        lock (_decomposers) _decomposers.Add(decomposer);
    }

    public IGoalDecomposer? Find(IGoal goal)
    {
        lock (_decomposers)
            return _decomposers.FirstOrDefault(d => d.CanHandle(goal));
    }

    public IReadOnlyList<IGoalDecomposer> All
    {
        get { lock (_decomposers) return [.. _decomposers]; }
    }
}
