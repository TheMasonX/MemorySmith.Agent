using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace WebUI.Blazor.Logging;

/// <summary>
/// Writes chat I/O as JSON lines to a daily rolling log file at
/// <c>logs/chat/chat-YYYY-MM-DD.log</c>, alongside the existing Serilog
/// and Mineflayer adapter log files.
///
/// The directory is created on first write if it does not exist.
/// I/O errors are logged and swallowed (best-effort — never crash the agent loop).
/// Thread-safe via the lock around the StreamWriter.
/// </summary>
public sealed class FileChatLogger : IChatLogger, IDisposable
{
    private const string LogSubDirectory = "chat";
    private const string FilePrefix = "chat-";
    private const string FileExtension = ".log";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _baseDirectory;
    private readonly ILogger<FileChatLogger>? _logger;
    private readonly ReaderWriterLockSlim _rwLock = new();
    private StreamWriter? _writer;
    private string? _currentDate;
    private bool _disposed;

    public FileChatLogger(string? baseDirectory = null, ILogger<FileChatLogger>? logger = null)
    {
        _baseDirectory = baseDirectory ?? Path.Combine(Directory.GetCurrentDirectory(), "logs");
        _logger = logger;
    }

    public void LogInbound(string username, string message, string? correlationId = null)
    {
        WriteEntry(new ChatLogEntry(
            DateTimeOffset.UtcNow, "inbound", username, message, correlationId));
    }

    public void LogOutbound(string username, string message, string? correlationId = null)
    {
        WriteEntry(new ChatLogEntry(
            DateTimeOffset.UtcNow, "outbound", username, message, correlationId));
    }

    private void WriteEntry(ChatLogEntry entry)
    {
        if (_disposed) return;

        try
        {
            var json = JsonSerializer.Serialize(entry, JsonOpts);
            _rwLock.EnterWriteLock();
            try
            {
                EnsureWriter();
                _writer!.WriteLine(json);
                _writer!.Flush();
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Unable to write chat log entry.");
        }
    }

    private void EnsureWriter()
    {
        var today = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
        if (_writer is not null && _currentDate == today)
            return;

        // Date changed or first call — rotate the writer.
        _writer?.Close();
        _currentDate = today;

        var chatDir = Path.Combine(_baseDirectory, LogSubDirectory);
        Directory.CreateDirectory(chatDir);

        var filePath = Path.Combine(chatDir, $"{FilePrefix}{today}{FileExtension}");
        _writer = new StreamWriter(filePath, append: true)
        {
            AutoFlush = true
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _rwLock.EnterWriteLock();
        try
        {
            _writer?.Close();
            _writer = null;
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
        _rwLock.Dispose();
    }
}
