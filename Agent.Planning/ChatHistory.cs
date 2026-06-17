// ──────────────────────────────────────────────────────────────────────────────
// Sprint 4b — Chat History Context Window
//
// Maintains a rolling buffer of the last N chat turns (player → bot responses)
// so the LLM has conversational context when interpreting a new message.
// ──────────────────────────────────────────────────────────────────────────────

namespace Agent.Planning;

/// <summary>
/// A single turn in the conversation: who said what.
/// The <c>Speaker</c> is either a player username or the bot's name.
/// </summary>
public sealed record ChatTurn(string Speaker, string Message, DateTimeOffset Timestamp);

/// <summary>
/// Thread-safe, bounded rolling buffer of recent chat turns.
/// Thread safety: all writes go through a single consumer (ChatConsumerAsync);
/// reads happen within the same serialized pipeline. No lock needed, but
/// the API is safe for concurrent access via <see cref="Interlocked.Exchange"/>.
/// </summary>
public sealed class ChatHistory(int maxTurns = MaxTurnsDefault)
{
    /// <summary>Default context window size (from Sprint 4b spec).</summary>
    public const int MaxTurnsDefault = 5;

    private volatile ChatTurn[] _buffer = [];

    /// <summary>Number of turns currently stored (up to <c>maxTurns</c>).</summary>
    public int Count => Volatile.Read(ref _buffer).Length;

    /// <summary>
    /// Records a new turn. If the buffer is full, the oldest turn is dropped.
    /// </summary>
    public void Record(string speaker, string message)
    {
        var turn = new ChatTurn(speaker, message, DateTimeOffset.UtcNow);
        while (true)
        {
            var current = Volatile.Read(ref _buffer);
            var updated = current.Length < maxTurns
                ? [.. current, turn]
                : [.. current[1..], turn];
            if (Interlocked.CompareExchange(ref _buffer, updated, current) == current)
                return;
        }
    }

    /// <summary>
    /// Returns a snapshot of recent turns as a human-readable context string,
    /// suitable for injecting into the LLM system prompt. Returns null when empty.
    /// </summary>
    public string? FormatForPrompt()
    {
        var current = Volatile.Read(ref _buffer);
        if (current.Length == 0) return null;
        return string.Join("\n", current.Select(t => $"[{t.Timestamp:HH:mm}] {t.Speaker}: {t.Message}"));
    }

    /// <summary>Clears all history (e.g. on reconnect or goal reset).</summary>
    public void Clear()
    {
        while (true)
        {
            var current = Volatile.Read(ref _buffer);
            if (current.Length == 0) return;
            if (Interlocked.CompareExchange(ref _buffer, [], current) == current)
                return;
        }
    }
}
