namespace Agent.Core;

/// <summary>
/// What kind of event this journal entry records.
/// </summary>
public enum JournalEntryType
{
    GoalSet,
    GoalCancel,
    PlanCreated,
    ActionDispatched,
    ActionCompleted,
    ActionFailed,
    ReplanTriggered,
    Observation,
    Error,
    AgentStarted,
    AgentStopped,
    /// <summary>
    /// The LLM interpreter was invoked to recover from a game error (blockNotFound,
    /// recipeMissing, etc.). Logged before the recovery interpreter call so failures
    /// are visible in the journal even if the recovery itself throws.
    /// </summary>
    ErrorRecovery,
}

/// <summary>
/// A single timestamped entry in the agent's execution journal.
/// Immutable record — the journal appends, never mutates.
/// </summary>
public sealed record JournalEntry(
    DateTimeOffset Timestamp,
    JournalEntryType Type,
    string Summary,
    IReadOnlyDictionary<string, object?>? Details = null);
