namespace Agent.Core;

/// <summary>
/// Reasons why a goal may have failed. Stored on <see cref="IGoal.FailureReason"/>.
/// </summary>
public enum FailureReason
{
    /// <summary>A tool action timed out.</summary>
    ToolTimeout,

    /// <summary>The target block or entity was unreachable (block not found, obstructed, etc.).</summary>
    TargetUnreachable,

    /// <summary>Inventory is full — cannot collect more items.</summary>
    InventoryFull,

    /// <summary>A required crafting recipe is missing or unknown.</summary>
    RecipeMissing,

    /// <summary>Too many consecutive tool failures, regardless of type.</summary>
    ConsecutiveFailures,

    /// <summary>No valid actions could be planned for the current goal.</summary>
    NoValidActions,

    /// <summary>Unclassified or unknown failure.</summary>
    Unknown,
}
