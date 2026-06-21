namespace WebUI.Blazor.Dashboard.Contracts;

public sealed record QueueSnapshot(
    int Count,
    IReadOnlyList<QueueActionSnapshot> Actions);
