namespace Agent.Tools;

using Agent.Core;
using System.Text.Json;

/// <summary>
/// Requests current bot status (position, health, food, inventory) from the world adapter.
/// The adapter responds asynchronously via a WorldEvent of type "status".
/// </summary>
public sealed class StatusTool(IWorldAdapter worldAdapter) : ITool
{
    public string Name => "GetStatus";
    public string Description => "Request current bot position, health, food level, and inventory.";

    public JsonElement InputSchema => JsonDocument.Parse("""
        {"type":"object","properties":{}}
        """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var action = new ActionData { Tool = ActionProtocol.Status };
        await worldAdapter.SendActionAsync(action, cancellationToken);
        return new ToolResult(true, "Status requested — await WorldEvent type 'status'.");
    }
}
