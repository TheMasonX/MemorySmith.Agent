namespace Agent.Tools;

using Agent.Core;
using System.Text.Json;

/// <summary>
/// Compatibility alias for plans that dispatch "GetStatus".
/// Sends the same world action as <see cref="StatusTool"/>.
/// </summary>
public sealed class GetStatusTool(IWorldAdapter worldAdapter) : ITool
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
