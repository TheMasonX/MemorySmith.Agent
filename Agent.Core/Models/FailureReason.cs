namespace Agent.Core;

/// <summary>
/// Reasons why a goal may have failed. Stored on <see cref="IGoal.FailureReason"/>.
///
/// Sprint 55 (TSK-0155): Added <see cref="OutcomeMismatch"/> and <see cref="ThreatDetected"/>
/// for the observe→compare→evaluate replan loop.
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

    /// <summary>
    /// Sprint 55 (TSK-0155): Observed world state contradicts expected outcome.
    /// The action completed but inventory/position/health deltas don't match
    /// what was expected — e.g., mined 5 blocks but only 2 items collected.
    /// </summary>
    OutcomeMismatch,

    /// <summary>
    /// Sprint 55 (TSK-0155): A hostile entity appeared during action execution
    /// that was not present before. Triggers immediate replan to handle the threat.
    /// </summary>
    ThreatDetected,

    /// <summary>Unclassified or unknown failure.</summary>
    Unknown,
}
