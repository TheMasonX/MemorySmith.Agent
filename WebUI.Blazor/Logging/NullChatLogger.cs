namespace WebUI.Blazor.Logging;

/// <summary>
/// No-op chat logger used when disk logging is disabled via configuration.
/// Satisfies the <see cref="IChatLogger"/> contract without allocating or writing.
/// </summary>
public sealed class NullChatLogger : IChatLogger
{
    public static readonly NullChatLogger Instance = new();

    public void LogInbound(string username, string message, string? correlationId = null) { }
    public void LogOutbound(string username, string message, string? correlationId = null) { }
}
