namespace Agent.Core;

/// <summary>
/// No-op IAgentJournal used when the agent is disabled or in unit tests that
/// don't need journal output. Prefer this over null to avoid null-check noise
/// throughout the codebase.
/// </summary>
public sealed class NullAgentJournal : IAgentJournal
{
    /// <summary>Shared singleton — safe because this class has no mutable state.</summary>
    public static readonly NullAgentJournal Instance = new();

    private NullAgentJournal() { }

    public int                        Count => 0;
    public IReadOnlyList<JournalEntry> All  => [];

    public void Log(JournalEntry entry)                            { }
    public void Clear()                                            { }
    public IReadOnlyList<JournalEntry> Recent(int count)          => [];
    public IReadOnlyList<JournalEntry> Query(
        JournalEntryType? type = null,
        DateTimeOffset? from   = null,
        DateTimeOffset? to     = null)                             => [];
}
