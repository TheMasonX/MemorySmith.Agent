namespace WebUI.Blazor.Logging;

/// <summary>
/// Logger for chat I/O. Records both inbound (player→bot) and outbound
/// (bot→players) chat messages to persistent storage.
/// Opt-out by default — enabled unless explicitly disabled in config.
/// </summary>
public interface IChatLogger
{
    /// <summary>Records an inbound chat message (player→bot).</summary>
    void LogInbound(string username, string message, string? correlationId = null);

    /// <summary>Records an outbound chat message (bot→players).</summary>
    void LogOutbound(string username, string message, string? correlationId = null);
}
