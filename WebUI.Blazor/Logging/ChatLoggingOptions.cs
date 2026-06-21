namespace WebUI.Blazor.Logging;

/// <summary>
/// Configuration for chat I/O disk logging.
/// Bound from <c>Agent:Chat:Logging</c> section in appsettings.json.
/// </summary>
public sealed class ChatLoggingOptions
{
    /// <summary>
    /// When true (default), all chat I/O is logged to <c>logs/chat/chat-*.log</c>.
    /// Set to false to disable disk logging entirely.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
