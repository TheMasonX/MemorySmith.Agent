namespace Agent.Tools;

// ──────────────────────────────────────────────────────────────────────────────
// Sprint 55 Wave B — QueryBlocksTool
//
// Queries blocks at a single position or within a bounding box (like Minecraft's
// /fill command). Dispatches "queryBlocks" to the Node.js/Mineflayer adapter
// which reads block data using bot.blockAt() for single queries or iterates the
// bounding box for range queries (capped at 4096 blocks = 16×16×16).
//
// The result arrives asynchronously via BlocksQueriedEvent in ProcessEventsAsync.
// ──────────────────────────────────────────────────────────────────────────────

using Agent.Core;
using System.Text.Json;

/// <summary>
/// Query the Minecraft world for blocks at a specific position or within a
/// bounding box defined by two corner positions. Returns block names and
/// type IDs for all non-air blocks found.
/// </summary>
public sealed class QueryBlocksTool(IWorldAdapter worldAdapter) : ITool
{
    public string Name => "QueryBlocks";
    public string Description =>
        "Query blocks at a position or in a region. Provide x,y,z for a single " +
        "block, or x,y,z and x2,y2,z2 for a bounding box (like /fill coordinates). " +
        "Returns the Minecraft block name and type ID for each non-air block. " +
        "Bounding box queries are capped at 4096 blocks (16×16×16). " +
        "Use this to inspect the environment: what blocks are at a location, " +
        "scan an area before building, or check if a position is clear.";

    private static readonly JsonDocument _schemaDoc = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "x": {
              "type": "integer",
              "description": "X coordinate of the block (or first corner of bounding box)"
            },
            "y": {
              "type": "integer",
              "description": "Y coordinate of the block (or first corner of bounding box)"
            },
            "z": {
              "type": "integer",
              "description": "Z coordinate of the block (or first corner of bounding box)"
            },
            "x2": {
              "type": "integer",
              "description": "Second corner X for bounding box query. If omitted, queries a single block."
            },
            "y2": {
              "type": "integer",
              "description": "Second corner Y for bounding box query."
            },
            "z2": {
              "type": "integer",
              "description": "Second corner Z for bounding box query."
            }
          },
          "required": ["x", "y", "z"]
        }
        """);

    public JsonElement InputSchema => _schemaDoc.RootElement;

    public async Task<ToolResult> ExecuteAsync(
        JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var x  = GetRequiredInt(arguments, "x");
        var y  = GetRequiredInt(arguments, "y");
        var z  = GetRequiredInt(arguments, "z");

        var action = new ActionData
        {
            Tool = ActionProtocol.QueryBlocks,
            Arguments =
            {
                ["x"] = (object?)x,
                ["y"] = (object?)y,
                ["z"] = (object?)z,
            },
        };

        // Optional second corner for bounding box
        if (arguments.TryGetProperty("x2", out var x2El) && x2El.TryGetInt32(out var x2) &&
            arguments.TryGetProperty("y2", out var y2El) && y2El.TryGetInt32(out var y2) &&
            arguments.TryGetProperty("z2", out var z2El) && z2El.TryGetInt32(out var z2))
        {
            action.Arguments["x2"] = (object?)x2;
            action.Arguments["y2"] = (object?)y2;
            action.Arguments["z2"] = (object?)z2;
        }

        await worldAdapter.SendActionAsync(action, cancellationToken);

        var desc = action.Arguments.ContainsKey("x2")
            ? $"QueryBlocks(bbox: ({x},{y},{z})→({action.Arguments["x2"]},{action.Arguments["y2"]},{action.Arguments["z2"]}))"
            : $"QueryBlocks(at: ({x},{y},{z}))";
        return new ToolResult(true, $"{desc} dispatched.");
    }

    private static int GetRequiredInt(JsonElement arguments, string key)
    {
        if (arguments.TryGetProperty(key, out var el) && el.TryGetInt32(out var val))
            return val;
        throw new ArgumentException($"QueryBlocks requires integer '{key}' argument.");
    }
}
