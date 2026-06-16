namespace Agent.Planning;

/// <summary>
/// The interpreted result of an in-game chat message, produced by
/// <see cref="IChatInterpreter"/> and consumed by <c>AgentBackgroundService</c>.
/// </summary>
public record ChatInterpretation(
    ChatIntentType IntentType,
    string? GoalName = null,
    IReadOnlyDictionary<string, object?>? GoalParameters = null,
    string Response = "");

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
}
