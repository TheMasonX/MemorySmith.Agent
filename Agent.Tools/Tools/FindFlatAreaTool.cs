namespace Agent.Tools;

// ──────────────────────────────────────────────────────────────────────────────
// Sprint 3b — FindFlatAreaTool
//
// Scans a radius around the bot for a flat, buildable area. The Node.js adapter
// samples columns within the radius looking for solid ground topped by at least
// minFlatBlocks of contiguous air. The best candidate is returned as a
// "flatAreaFound" world event.
//
// Sprint 25 P0-A: defaults unified with JS adapter constants
//   (FLAT_AREA_SCAN_RADIUS=32, FLAT_AREA_MIN_SIZE=25).
//   Safe integer parsing via TryGetInt32 to handle scientific notation gracefully.
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
        "Use 'radius' (default 32) to control search radius and 'minFlatArea' (default 25) " +
        "for the minimum number of contiguous flat blocks required.";

    // Cache the document so the returned JsonElement is never backed by a disposed object.
    // (Returning JsonDocument.Parse(...).RootElement directly is a correctness bug —
    // the document is disposed immediately, leaving the element over freed memory.)
    private static readonly JsonDocument _schemaDoc = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "radius": {
              "type": "integer",
              "description": "Search radius in blocks from current position (default 32)"
            },
            "minFlatArea": {
              "type": "integer",
              "description": "Minimum number of flat contiguous blocks required (default 25, i.e. 5x5)"
            }
          },
          "required": []
        }
        """);

    public JsonElement InputSchema => _schemaDoc.RootElement;

    public async Task<ToolResult> ExecuteAsync(
        JsonElement arguments, CancellationToken cancellationToken = default)
    {
        // Sprint 25 P0-A: TryGetInt32 handles scientific notation and non-integer values
        // gracefully by falling back to the default instead of throwing.
        var radius      = arguments.TryGetProperty("radius",      out var r)
            && r.TryGetInt32(out var rv) ? rv : 32;
        var minFlatArea  = arguments.TryGetProperty("minFlatArea",  out var m)
            && m.TryGetInt32(out var mv) ? mv : 25;

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
