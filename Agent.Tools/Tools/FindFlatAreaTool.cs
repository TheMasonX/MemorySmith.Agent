namespace Agent.Tools;

// ──────────────────────────────────────────────────────────────────────────────
// Sprint 3b — FindFlatAreaTool
//
// Scans a radius around the bot for a flat, buildable area. The Node.js adapter
// samples columns within the radius looking for solid ground topped by at least
// minFlatBlocks of contiguous air. The best candidate is returned as a
// "flatAreaFound" world event.
// ──────────────────────────────────────────────────────────────────────────────

using Agent.Core;
using System.Text.Json;

/// <summary>
/// Find a flat, contiguous area near the bot suitable for building.
/// Dispatches <c>findFlatArea</c> to the Node.js/Mineflayer adapter which
/// scans columns of blocks and picks the best candidate.
/// </summary>
public sealed class FindFlatAreaTool(IWorldAdapter worldAdapter) : ITool
{
    public string Name => "FindFlatArea";
    public string Description =>
        "Scan a radius around the bot for a flat, buildable area. " +
        "Returns the center coordinates of the best candidate (solid ground + air above). " +
        "Use 'radius' (default 20) to control search radius and 'minFlatArea' (default 9) " +
        "for the minimum number of contiguous flat blocks required.";

    public JsonElement InputSchema => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "radius": {
              "type": "integer",
              "description": "Search radius in blocks from current position (default 20)"
            },
            "minFlatArea": {
              "type": "integer",
              "description": "Minimum number of flat contiguous blocks required (default 9, i.e. 3x3)"
            }
          },
          "required": []
        }
        """).RootElement;

    public async Task<ToolResult> ExecuteAsync(
        JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var radius       = arguments.TryGetProperty("radius",       out var r) ? r.GetInt32() : 20;
        var minFlatArea  = arguments.TryGetProperty("minFlatArea",  out var m) ? m.GetInt32() : 9;

        var action = new ActionData
        {
            Tool = ActionProtocol.FindFlatArea,
            Arguments =
            {
                ["radius"]     = (object?)radius,
                ["minFlatArea"] = (object?)minFlatArea,
            },
        };

        await worldAdapter.SendActionAsync(action, cancellationToken);
        return new ToolResult(true, $"FindFlatArea(radius={radius}, minFlatArea={minFlatArea}) dispatched.");
    }
}
