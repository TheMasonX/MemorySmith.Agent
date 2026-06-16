namespace Agent.Tools;

using Agent.Core;
using System.Text.Json;

/// <summary>
/// Mines specified blocks near the bot's current position.
/// Dispatches {"action":"mine","block":"minecraft:oak_log","count":5} to the world adapter.
/// </summary>
public sealed class MineBlockTool(IWorldAdapter worldAdapter) : ITool
{
    public string Name => "MineBlock";
    public string Description => "Mine specified blocks near the bot. Requires block name and optional count.";

    public JsonElement InputSchema => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "block": { "type": "string", "description": "Block name to mine, e.g. minecraft:oak_log" },
            "count": { "type": "integer", "description": "Number of blocks to mine (default: 1)" }
          },
          "required": ["block"]
        }
        """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetProperty("block", out var blockEl))
            return new ToolResult(false, "MineBlock requires a 'block' argument.");

        string block = blockEl.GetString() ?? "minecraft:oak_log";
        int count = 1;
        if (arguments.TryGetProperty("count", out var countEl))
            count = countEl.GetInt32();

        var action = new ActionData
        {
            Tool = "mine",
            Arguments = { ["block"] = block, ["count"] = count }
        };

        await worldAdapter.SendActionAsync(action, cancellationToken);
        return new ToolResult(true, $"MineBlock({block}, {count}) dispatched.");
    }
}
