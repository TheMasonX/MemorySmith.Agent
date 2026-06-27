namespace Agent.Planning;

using System.Text.Json;

/// <summary>
/// Sprint 51: Dedicated logger that captures the full raw context sent to and
/// received from the LLM provider. Writes to a separate rolling log file
/// with timestamped entries for complete auditability.
///
/// File layout:
///   logs/llm-context/llm-context-2026-06-26.log   ← active file
///   logs/llm-context/archive/llm-context-2026-06-25.log.zip  ← archived
///
/// Rolling: new file each day. Archives are zipped after rotation.
/// This logger uses its own StreamWriter — it does NOT go through Serilog,
/// ensuring the raw LLM context is never truncated, filtered, or mixed
/// with application log output.
/// </summary>
public sealed class LlmContextLogger : IDisposable
{
    private readonly object _lock = new();
    private readonly string _logDirectory;
    private readonly int _retainedDays;

    private string _currentDate = string.Empty;
    private StreamWriter? _writer;

    /// <param name="logDirectory">Directory for log files. Defaults to "logs/llm-context" relative to current directory.</param>
    /// <param name="retainedDays">Number of days to keep before archiving. Default 7.</param>
    public LlmContextLogger(string? logDirectory = null, int retainedDays = 7)
    {
        _logDirectory = logDirectory ?? Path.Combine(Directory.GetCurrentDirectory(), "logs", "llm-context");
        _retainedDays = retainedDays;
        Directory.CreateDirectory(_logDirectory);
    }

    /// <summary>
    /// Logs a complete LLM request (system prompt + user message sent to provider).
    /// </summary>
    public void LogRequest(string provider, string model, string username,
        string systemPrompt, string userMessage)
    {
        var entry = new
        {
            timestamp = DateTimeOffset.UtcNow.ToString("O"),
            direction = "REQUEST",
            provider,
            model,
            username,
            systemPrompt,
            userMessage
        };
        WriteEntry(entry);
    }

    /// <summary>
    /// Logs a complete LLM response (raw text received from provider).
    /// </summary>
    public void LogResponse(string provider, string model,
        string? rawResponse, TimeSpan elapsed)
    {
        var entry = new
        {
            timestamp = DateTimeOffset.UtcNow.ToString("O"),
            direction = "RESPONSE",
            provider,
            model,
            elapsedMs = (int)elapsed.TotalMilliseconds,
            rawResponse
        };
        WriteEntry(entry);
    }

    private void WriteEntry(object entry)
    {
        var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions
        {
            WriteIndented = false // one entry per line for grep-ability
        });

        lock (_lock)
        {
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (today != _currentDate)
            {
                _writer?.Dispose();
                _writer = null;
                _currentDate = today;
                RotateOldFiles();
            }

            if (_writer is null)
            {
                var filePath = Path.Combine(_logDirectory, $"llm-context-{today}.log");
                _writer = new StreamWriter(filePath, append: true);
                _writer.AutoFlush = true;
            }

            _writer.WriteLine(json);
        }
    }

    private void RotateOldFiles()
    {
        try
        {
            var cutoff = DateTime.UtcNow.Date.AddDays(-_retainedDays);
            var archiveDir = Path.Combine(_logDirectory, "archive");
            Directory.CreateDirectory(archiveDir);

            foreach (var file in Directory.GetFiles(_logDirectory, "llm-context-*.log"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                // Extract date from filename: "llm-context-2026-06-25"
                var datePart = name.Replace("llm-context-", "");
                if (DateTime.TryParse(datePart, out var fileDate) && fileDate < cutoff)
                {
                    var archivePath = Path.Combine(archiveDir,
                        Path.GetFileName(file) + ".zip");
                    if (!File.Exists(archivePath))
                    {
                        // Simple zip: just compress the file using System.IO.Compression
                        try
                        {
                            using var inStream = new FileStream(file, FileMode.Open, FileAccess.Read);
                            using var outStream = new FileStream(archivePath, FileMode.Create);
                            using var zip = new System.IO.Compression.GZipStream(
                                outStream, System.IO.Compression.CompressionLevel.Optimal);
                            inStream.CopyTo(zip);
                            File.Delete(file);
                        }
                        catch
                        {
                            // If archiving fails, leave the file alone
                        }
                    }
                }
            }
        }
        catch
        {
            // Best-effort rotation — never throw from background cleanup
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
