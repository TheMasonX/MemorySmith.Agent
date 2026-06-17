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
public sealed class ChatHistory
{
    /// <summary>Default context window size (from Sprint 4b spec).</summary>
    public const int MaxTurnsDefault = 5;

    private readonly int _maxTurns;
    private volatile ChatTurn[] _buffer = [];

    public ChatHistory(int maxTurns = MaxTurnsDefault)
    {
        _maxTurns = maxTurns;
    }

    /// <summary>Number of turns currently stored (up to <c>maxTurns</c>).</summary>
    public int Count
    {
        get
        {
            var current = Volatile.Read(ref _buffer);
            return current.Length;
        }
    }

    /// <summary>
    /// Records a new turn. If the buffer is full, the oldest turn is dropped.
    /// </summary>
    public void Record(string speaker, string message)
    {
        var turn = new ChatTurn(speaker, message, DateTimeOffset.UtcNow);
        while (true)
        {
            var current = Volatile.Read(ref _buffer);
            ChatTurn[] updated;
            if (current.Length < _maxTurns)
            {
                updated = new ChatTurn[current.Length + 1];
                Array.Copy(current, 0, updated, 0, current.Length);
                updated[current.Length] = turn;
            }
            else
            {
                updated = new ChatTurn[current.Length];
                Array.Copy(current, 1, updated, 0, current.Length - 1);
                updated[current.Length - 1] = turn;
            }
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
        Volatile.Write(ref _buffer, []);
    }
}
