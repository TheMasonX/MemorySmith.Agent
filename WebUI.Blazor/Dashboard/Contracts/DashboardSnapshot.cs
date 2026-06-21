namespace WebUI.Blazor.Dashboard.Contracts;

public sealed record DashboardSnapshot(
    AgentStatusSnapshot Status,
    GoalSnapshot? Goal,
    InventorySnapshot Inventory,
    QueueSnapshot Queue,
    IReadOnlyList<ChatMessageSnapshot> RecentChat,
    IReadOnlyList<JournalEntrySnapshot> RecentJournal,
    DateTimeOffset TimestampUtc);
