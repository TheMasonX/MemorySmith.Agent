namespace Agent.Core;

/// <summary>
/// The top-level agent abstraction. Manages goal lifecycle, drives the planner,
/// dispatches tool calls, and maintains world state.
/// </summary>
public interface IAgent
{
    string Id { get; }
    string Name { get; }
    IGoal? CurrentGoal { get; }
    WorldState WorldState { get; }

    Task RunAsync(CancellationToken cancellationToken = default);
    Task SetGoalAsync(IGoal goal, CancellationToken cancellationToken = default);
    Task StopAsync();
}
