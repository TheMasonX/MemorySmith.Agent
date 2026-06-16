namespace Agent.Core;

/// <summary>
/// LLM-powered chat evaluation. Determines whether a Minecraft chat message is
/// directed at the agent and maps it to a <see cref="ChatInterpretation"/>.
///
/// Implementations may call a local LLM (Ollama) or a remote API.
/// Must implement graceful fallback: return null when the LLM is unavailable or
/// times out, so the caller can fall back to pattern matching (D-003).
///
/// All implementations must be thread-safe; a single instance is shared across
/// the agent's event-processing loop.
/// </summary>
public interface IChatLlmClient
{
    /// <summary>
    /// Evaluates an in-game chat message and returns a structured interpretation,
    /// or null if the LLM is unavailable, timed out, or returned an unparseable
    /// response.
    /// </summary>
    Task<ChatInterpretation?> EvaluateAsync(
        string botName,
        Position botPosition,
        string username,
        string message,
        int onlinePlayers,
        Position? playerPosition,
        string? currentGoal,
        CancellationToken ct = default);
}
