namespace Agent.Planning;

using Agent.Core;

/// <summary>
/// A compound HTN task definition. The name identifies the task;
/// the description is injected into LLM prompts when the task is presented
/// as a planning choice.
/// </summary>
public sealed record HtnTask(string Name, string Description, string[] SubTasks);

/// <summary>
/// Decomposes a compound HTN task into a sequence of atomic ActionData items
/// given optional string parameters and the current WorldState.
/// </summary>
public delegate IReadOnlyList<ActionData> TaskDecomposer(
    string[] parameters, WorldState state);
