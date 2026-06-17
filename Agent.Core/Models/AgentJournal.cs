using System.Collections.Concurrent;
using Agent.Core;

namespace Agent.Core;

/// <summary>
/// Thread-safe, bounded append-only journal. Uses a ConcurrentQueue for lock-free writes
/// and trims oldest entries when exceeding capacity.
/// </summary>
public sealed class AgentJournal : IAgentJournal
{
    /// <summary>Maximum journal entries before oldest are trimmed.</summary>
    public const int MaxEntries = 1000;

    private readonly ConcurrentQueue<JournalEntry> _entries = new();
    private volatile int _count;

    public IReadOnlyList<JournalEntry> All => [.. _entries.Reverse()];

    public void Log(JournalEntry entry)
    {
        _entries.Enqueue(entry);
        var current = Interlocked.Increment(ref _count);

        // Trim oldest if over capacity (fire-and-forget, best-effort)
        while (current > MaxEntries)
        {
            if (_entries.TryDequeue(out _))
                Interlocked.Decrement(ref _count);
            current = Volatile.Read(ref _count);
        }
    }

    public IReadOnlyList<JournalEntry> Recent(int count) =>
        _entries.Reverse().Take(count).ToList();

    public IReadOnlyList<JournalEntry> Query(
        JournalEntryType? type = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null)
    {
        var query = _entries.AsEnumerable();
        if (type.HasValue)
            query = query.Where(e => e.Type == type.Value);
        if (from.HasValue)
            query = query.Where(e => e.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(e => e.Timestamp <= to.Value);
        return query.Reverse().ToList();
    }

    public void Clear()
    {
        while (_entries.TryDequeue(out _)) { }
        _count = 0;
    }
}
