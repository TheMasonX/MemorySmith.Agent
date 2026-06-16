namespace Agent.Planning;

using Agent.Core;

/// <summary>
/// Creates typed IGoal instances from a string name and optional parameters.
/// Used by REST endpoints to translate user commands and chat messages into goals
/// the planner can process.
/// </summary>
public interface IGoalFactory
{
    /// <summary>
    /// Creates a goal synchronously. Returns null if the goal name is not registered
    /// or requires an async lookup (e.g. GatherItem:{itemId}, Build:{blueprintId}).
    /// </summary>
    IGoal? Create(
        string goalName,
        IReadOnlyDictionary<string, object?>? parameters = null);

    /// <summary>
    /// Creates a goal, performing async registry lookups when needed.
    /// Handles all static goal names (via <see cref="Create"/>) and dynamic prefixes
    /// (GatherItem:{itemId}, Build:{blueprintId}) that require async repository access.
    /// Returns null if the goal name is not registered, required dependencies are missing,
    /// or the registry returns null for the item/blueprint ID.
    /// </summary>
    Task<IGoal?> CreateAsync(
        string goalName,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default);

    /// <summary>Returns all registered goal names and prefix patterns.</summary>
    IReadOnlyList<string> RegisteredGoals { get; }
}
