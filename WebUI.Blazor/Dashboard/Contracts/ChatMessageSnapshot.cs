namespace WebUI.Blazor.Dashboard.Contracts;

/// <summary>Lightweight chat message for dashboard display.</summary>
public sealed record ChatMessageSnapshot(
    string Type,     // "player" | "bot"
    string? Who,     // username
    string Text,
    DateTimeOffset TimestampUtc);
