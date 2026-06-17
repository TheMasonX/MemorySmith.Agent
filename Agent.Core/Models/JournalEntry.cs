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
