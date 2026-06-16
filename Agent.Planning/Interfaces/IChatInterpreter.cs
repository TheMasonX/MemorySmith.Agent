namespace Agent.Planning;

using Agent.Core;

/// <summary>
/// Evaluates in-game chat messages and maps them to <see cref="ChatInterpretation"/>
/// values the agent loop can act on.
///
/// Two implementations:
/// - <see cref="ChatInterpreter"/> — fast, deterministic, pattern-matching only.
/// - <see cref="LlmChatInterpreter"/> — LLM-powered with pattern-matching fallback.
///
/// Both are singleton services; their state (conversation window tracking) persists
/// across chat events.
/// </summary>
public interface IChatInterpreter
{
    /// <summary>
    /// Asynchronously interprets a chat message.
    ///
    /// <paramref name="botPosition"/> and <paramref name="playerPosition"/> are used
    /// for the "closest agent" heuristic: agents far from the player are less likely
    /// to respond unless directly addressed.
    ///
    /// Returns <see cref="ChatInterpretation"/> with
    /// <see cref="ChatIntentType.NotAddressed"/> if the message should be ignored.
    /// Never returns null.
    /// </summary>
    Task<ChatInterpretation> InterpretAsync(
        string username,
        string message,
        string botName,
        int onlinePlayers,
        Position botPosition,
        Position? playerPosition,
        WorldState state,
        CancellationToken ct = default);

    /// <summary>Records that the bot just sent a chat message, opening the
    /// 60-second conversation window for subsequent messages.</summary>
    void RecordBotSpoke();
}
