namespace Agent.Core;

/// <summary>
/// Registry of available actions/tools with their metadata, preconditions,
/// and postconditions. Gives the planner a structured, explicit view of
/// action semantics instead of relying on prompt-only assumptions.
///
/// Sprint 57: Introduced as part of the ExecutionContext architecture.
/// Sprint 59 target: populated from ToolDispatcher + ITool.InputSchema
///                   with optional precondition/postcondition metadata.
/// </summary>
public sealed class ActionRegistry
{
    private readonly Dictionary<string, ActionDescriptor> _actions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All registered action descriptors.</summary>
    public IReadOnlyDictionary<string, ActionDescriptor> Actions => _actions;

    /// <summary>Registers an action with its metadata.</summary>
    public void Register(ActionDescriptor descriptor)
    {
        _actions[descriptor.Name] = descriptor;
    }

    /// <summary>Registers an action by name only (minimal metadata).</summary>
    public void Register(string name, string? description = null)
    {
        _actions[name] = new ActionDescriptor(name, description);
    }

    /// <summary>True if the named action is available.</summary>
    public bool CanExecute(string actionName) =>
        _actions.ContainsKey(actionName);

    /// <summary>Returns the descriptor for the given action, or null if not found.</summary>
    public ActionDescriptor? Get(string actionName) =>
        _actions.TryGetValue(actionName, out var d) ? d : null;
}

/// <summary>
/// Describes a single action/tool available to the planner.
/// </summary>
/// <param name="Name">Canonical tool name (e.g., "MineBlock", "PlaceBlock").</param>
/// <param name="Description">Human-readable description for the LLM prompt.</param>
/// <param name="RequiresCreative">True if this action only works in creative mode.</param>
/// <param name="RequiresSurvival">True if this action only works in survival mode.</param>
public sealed record ActionDescriptor(
    string Name,
    string? Description = null,
    bool RequiresCreative = false,
    bool RequiresSurvival = false);
