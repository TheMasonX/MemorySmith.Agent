namespace WebUI.Blazor.Logging;

/// <summary>
/// A single chat I/O entry persisted to disk for audit and observability.
/// Written as a JSON line to the daily chat log file.
/// </summary>
public sealed record ChatLogEntry(
    DateTimeOffset TimestampUtc,
    string Direction,   // "inbound" | "outbound"
    string Username,    // who sent (inbound) or the bot name (outbound)
    string Message,     // the chat text
    string? CorrelationId = null);
