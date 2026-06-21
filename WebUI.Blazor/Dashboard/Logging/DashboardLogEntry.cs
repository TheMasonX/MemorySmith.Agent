namespace WebUI.Blazor.Dashboard.Logging;

/// <summary>
/// A single entry in the live dashboard log buffer.
/// Mirrors a subset of Serilog's LogEvent for the in-memory ring buffer.
/// </summary>
public sealed record DashboardLogEntry(
    DateTimeOffset TimestampUtc,
    string Level,      // "Warning" | "Information" | "Error"
    string Source,     // SourceContext
    string Message,
    string? Exception = null);
