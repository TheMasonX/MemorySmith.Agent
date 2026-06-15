namespace Agent.Tools;

using Agent.Core;
using System.Text.Json;

/// <summary>
/// Navigates the bot to the specified block coordinates.
/// Hides pathfinding internals from the LLM — it only sees MoveTo(x, y, z).
/// Dispatches {"action":"move","x":…,"y":…,"z":…} to the world adapter.
/// </summary>
public sealed class MoveToTool(IWorldAdapter worldAdapter) : ITool
{
    public string Name => "MoveTo";
    public string Description => "Navigate the bot to the specified block coordinates.";

    public JsonElement InputSchema => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "x": { "type": "integer", "description": "Target X coordinate" },
            "y": { "type": "integer", "description": "Target Y coordinate" },
            "z": { "type": "integer", "description": "Target Z coordinate" }
          },
          "required": ["x", "y", "z"]
        }
        """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetProperty("x", out var xEl) ||
            !arguments.TryGetProperty("y", out var yEl) ||
            !arguments.TryGetProperty("z", out var zEl))
            return new ToolResult(false, "MoveTo requires x, y, z arguments.");

        int x = xEl.GetInt32(), y = yEl.GetInt32(), z = zEl.GetInt32();

        var action = new ActionData
        {
            Tool = "move",
            Arguments = { ["x"] = x, ["y"] = y, ["z"] = z }
        };

        await worldAdapter.SendActionAsync(action, cancellationToken);
        return new ToolResult(true, $"MoveTo({x},{y},{z}) dispatched.");
    }
}
