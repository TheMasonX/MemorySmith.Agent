namespace Agent.Planning;

using Agent.Core;

/// <summary>
/// Evaluates in-game chat messages and maps them to <see cref="ChatInterpretation"/>
/// values the agent loop can act on.
///
/// Implementations:
/// - <see cref="ChatInterpreter"/> — fast, deterministic, no network calls.
/// - <see cref="LlmChatInterpreter"/> — LLM-powered with pattern-matching fallback.
///
/// Both are singleton services; state (conversation window) persists across events.
/// Never returns null; returns <see cref="ChatIntentType.NotAddressed"/> when the
/// message should be ignored.
/// </summary>
public interface IChatInterpreter
{
    /// <summary>
    /// Asynchronously interprets a chat message.
    ///
    /// <paramref name="botPosition"/> and optional <paramref name="playerPosition"/>
    /// are used for the "closest-agent" distance gate.
    /// </summary>
    Task<ChatInterpretation> InterpretAsync(
        string username,
        string message,
        string botName,
        int onlinePlayers,
        Agent.Core.Position botPosition,
        Agent.Core.Position? playerPosition,
        WorldState state,
        CancellationToken ct = default);

    /// <summary>Records that the bot sent a chat response, opening the conversation window.</summary>
    void RecordBotSpoke();
}
