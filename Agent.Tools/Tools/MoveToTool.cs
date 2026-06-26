namespace Agent.Tools;

using Agent.Core;
using System.Text.Json;

/// <summary>
/// Navigates the bot to the specified block coordinates.
/// Hides pathfinding internals from the LLM — it only sees MoveTo(x, y, z).
/// Supports context carry: when x/y/z are not supplied as explicit arguments,
/// the tool reads nearestX/nearestY/nearestZ from arguments (merged from
/// ActionData.Context by the dispatch loop when SearchMemoryTool wrote them).
/// Dispatches {\"action\":\"move\",\"x\":…,\"y\":…,\"z\":…} to the world adapter.
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
            "z": { "type": "integer", "description": "Target Z coordinate" },
            "nearestX": { "type": "integer", "description": "Context-carried X coordinate (from SearchMemory)" },
            "nearestY": { "type": "integer", "description": "Context-carried Y coordinate (from SearchMemory)" },
            "nearestZ": { "type": "integer", "description": "Context-carried Z coordinate (from SearchMemory)" }
          },
          "required": []
        }
        """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        // Priority: explicit x/y/z arguments, then context-carried nearestX/Y/Z
        int x, y, z;
        if (arguments.TryGetProperty("x", out var xEl) &&
            arguments.TryGetProperty("y", out var yEl) &&
            arguments.TryGetProperty("z", out var zEl))
        {
            x = xEl.GetInt32(); y = yEl.GetInt32(); z = zEl.GetInt32();
        }
        else if (arguments.TryGetProperty("nearestX", out var nxEl) &&
                 arguments.TryGetProperty("nearestY", out var nyEl) &&
                 arguments.TryGetProperty("nearestZ", out var nzEl))
        {
            x = nxEl.GetInt32(); y = nyEl.GetInt32(); z = nzEl.GetInt32();
        }
        else
        {
            return new ToolResult(false, "MoveTo requires x, y, z arguments (or nearestX/Y/Z from context).");
        }

        var action = new ActionData
        {
            Tool = ActionProtocol.Move,
            Arguments = { ["x"] = x, ["y"] = y, ["z"] = z }
        };

        await worldAdapter.SendActionAsync(action, cancellationToken);
        return new ToolResult(true, $"MoveTo({x},{y},{z}) dispatched.");
    }
}
