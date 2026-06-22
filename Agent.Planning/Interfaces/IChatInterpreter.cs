namespace Agent.Planning;

using Agent.Core;

/// <summary>
/// Evaluates in-game chat messages and maps them to an <see cref="IntentDraft"/>
/// that the agent loop can act on.
///
/// Implementations:
/// - <see cref="ChatInterpreter"/> — fast, deterministic, no network calls.
/// - <see cref="LlmChatInterpreter"/> — LLM-powered with pattern-matching fallback.
///
/// Both are singleton services; state (conversation window) persists across events.
///
/// Sprint 39 P1-C: return type changed from <see cref="ChatInterpretation"/> to
/// <see cref="IntentDraft"/>. Returns <see langword="null"/> when the message is not
/// addressed at this bot (replaces <c>ChatIntentType.NotAddressed</c>). This enforces
/// PRINCIPLE-1 (parsers never create goals): the interpreter produces semantic intent
/// data only; goal creation is the caller's responsibility via IntentManager + GoalFactory.
/// </summary>
public interface IChatInterpreter
{
    /// <summary>
    /// Asynchronously interprets a chat message and returns a semantic intent draft,
    /// or <see langword="null"/> when the message should be ignored.
    ///
    /// <paramref name="botPosition"/> and optional <paramref name="playerPosition"/>
    /// are used for the "closest-agent" distance gate.
    /// </summary>
    Task<IntentDraft?> InterpretAsync(
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
