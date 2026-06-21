namespace Agent.Core;

/// <summary>
/// Append-only event-sourced execution journal for full agent traceability.
/// Supports querying by type, time range, and recency.
/// Thread-safe for concurrent writes from multiple producers (background service, tool dispatcher).
/// </summary>
public interface IAgentJournal
{
    /// <summary>Append an entry. Thread-safe.</summary>
    void Log(JournalEntry entry);

    /// <summary>Approximate number of entries currently stored. Thread-safe.</summary>
    int Count { get; }

    /// <summary>All entries, newest-first.</summary>
    IReadOnlyList<JournalEntry> All { get; }

    /// <summary>Last N entries, newest-first.</summary>
    IReadOnlyList<JournalEntry> Recent(int count);

    /// <summary>Query by type and optional time range.</summary>
    IReadOnlyList<JournalEntry> Query(
        JournalEntryType? type = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null);

    /// <summary>Clear all entries (e.g., on agent restart).</summary>
    void Clear();
}
