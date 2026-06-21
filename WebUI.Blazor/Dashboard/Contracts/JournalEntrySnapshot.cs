namespace WebUI.Blazor.Dashboard.Contracts;

/// <summary>Lightweight journal entry for dashboard timeline display.</summary>
public sealed record JournalEntrySnapshot(
    DateTimeOffset TimestampUtc,
    string EntryType,
    string Summary);
