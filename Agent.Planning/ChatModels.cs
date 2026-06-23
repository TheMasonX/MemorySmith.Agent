namespace Agent.Planning;

/// <summary>
/// Sprint 44 (P1-1): Removed — this type has been superseded by <see cref="IntentDraft"/>.
/// The <c>GoalName</c> zombie field was kept for backward compatibility through Sprint 43.
/// All callers now use <see cref="IntentDraft"/>? (null = not addressed).
/// </summary>
// public record ChatInterpretation(...) — removed. Use IntentDraft instead.

/// <summary>Classified intent of a chat message.</summary>
public enum ChatIntentType
{
    /// <summary>Message is not directed at this bot — ignore.</summary>
    NotAddressed,

    /// <summary>Player wants the bot to pursue a goal.</summary>
    CreateGoal,

    /// <summary>Player wants the bot to stop its current goal.</summary>
    CancelGoal,

    /// <summary>Player is asking for a status report.</summary>
    QueryStatus,

    /// <summary>Player is asking what commands are available.</summary>
    QueryHelp,

    /// <summary>Player wants the bot to move somewhere.</summary>
    NavigateTo,

    /// <summary>Intent could not be parsed — bot asks a clarifying question.</summary>
    Unknown,

    /// <summary>
    /// Conversational message addressed to the bot (greetings, questions, small-talk).
    /// No goal or navigation action — just send the LLM response to in-game chat.
    /// </summary>
    Chat,
}
