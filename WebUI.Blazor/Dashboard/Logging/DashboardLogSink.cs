using Serilog.Core;
using Serilog.Events;

namespace WebUI.Blazor.Dashboard.Logging;

/// <summary>
/// Serilog <see cref="ILogEventSink"/> that feeds Warning/Information/Error
/// events into the <see cref="LiveLogBuffer"/> for real-time dashboard display.
///
/// Register via Serilog configuration:
/// <code>
/// loggerConfig.WriteTo.Sink(new DashboardLogSink(buffer));
/// </code>
///
/// Debug and Verbose levels are ignored to avoid trace flood.
/// </summary>
public sealed class DashboardLogSink : ILogEventSink
{
    private static readonly HashSet<LogEventLevel> CapturedLevels =
    [
        LogEventLevel.Warning,
        LogEventLevel.Information,
        LogEventLevel.Error,
        LogEventLevel.Fatal,
    ];

    private readonly LiveLogBuffer _buffer;

    public DashboardLogSink(LiveLogBuffer buffer)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
    }

    public void Emit(LogEvent logEvent)
    {
        if (!CapturedLevels.Contains(logEvent.Level))
            return;

        var source = "unknown";
        if (logEvent.Properties.TryGetValue("SourceContext", out var sourceProperty))
            source = sourceProperty.ToString().Trim('"');

        _buffer.Add(new DashboardLogEntry(
            logEvent.Timestamp.ToUniversalTime(),
            logEvent.Level.ToString(),
            source,
            logEvent.RenderMessage(),
            logEvent.Exception?.ToString()));
    }
}
