namespace Agent.Tools;

using Agent.Core;
using System.Text.Json;

/// <summary>
/// Sends a chat message in the Minecraft server as the bot.
///
/// The message appears to other players in the in-game chat.
/// Used by the agent to respond to player commands and report status.
/// Wire name: <c>chat</c> (ActionProtocol.Chat).
/// </summary>
public sealed class ChatTool(IWorldAdapter worldAdapter) : ITool
{
    public string Name => "Chat";
    public string Description => "Send a chat message in Minecraft as the bot.";

    public JsonElement InputSchema => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "message": { "type": "string", "description": "Message text to send in chat (max 256 chars)" }
          },
          "required": ["message"]
        }
        """).RootElement;

    public async Task<ToolResult> ExecuteAsync(
        JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetProperty("message", out var msgEl))
            return new ToolResult(false, "Chat requires a 'message' argument.");

        var message = (msgEl.GetString() ?? string.Empty).Trim();
        if (message.Length == 0)
            return new ToolResult(false, "Chat message is empty.");

        // Minecraft chat messages are capped at 256 characters
        if (message.Length > 256)
            message = message[..256];

        var action = new ActionData
        {
            Tool      = ActionProtocol.Chat,
            Arguments = { ["message"] = message }
        };

        await worldAdapter.SendActionAsync(action, cancellationToken);
        return new ToolResult(true, $"Chat sent: {message}");
    }
}
