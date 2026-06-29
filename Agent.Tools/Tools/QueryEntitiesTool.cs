namespace Agent.Tools;

// ──────────────────────────────────────────────────────────────────────────────
// Sprint 55 Wave B — QueryEntitiesTool
//
// Queries entities (mobs, players, items) near the bot within a configurable
// radius. Dispatches "queryEntities" to the Node.js/Mineflayer adapter which
// scans bot.entities for matching entities and returns their types, positions,
// distances, and health.
//
// The result arrives asynchronously via EntitiesQueriedEvent in ProcessEventsAsync.
//
// Also see: EntityObservedEvent — the passive, periodic threat-scan event that
// fires automatically when hostile mobs are detected during action execution.
// ──────────────────────────────────────────────────────────────────────────────

using Agent.Core;
using System.Text.Json;

/// <summary>
/// Query entities near the bot. Returns a list of entities (mobs, players,
/// dropped items) within the specified radius, sorted by distance.
/// Useful for environmental awareness: checking for threats, finding animals,
/// or locating specific mob types before taking action.
/// </summary>
public sealed class QueryEntitiesTool(IWorldAdapter worldAdapter) : ITool
{
    public string Name => "QueryEntities";
    public string Description =>
        "Scan for entities near the bot. Returns a list of nearby mobs, players, " +
        "and dropped items within the specified radius, sorted by distance. " +
        "Use 'radius' (default 16, max 64) to control search range. " +
        "Use 'entityType' to filter by type: 'mob' (creatures), 'player' (players), " +
        "or 'object' (dropped items, arrows, etc.). " +
        "This is useful for: checking if the area is safe, finding specific mobs, " +
        "locating dropped items, or seeing who is nearby.";

    private static readonly JsonDocument _schemaDoc = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "radius": {
              "type": "integer",
              "description": "Search radius in blocks from current position (default 16, max 64)"
            },
            "entityType": {
              "type": "string",
              "description": "Filter by entity type: 'mob', 'player', or 'object' (optional)"
            }
          },
          "required": []
        }
        """);

    public JsonElement InputSchema => _schemaDoc.RootElement;

    public async Task<ToolResult> ExecuteAsync(
        JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var radius = arguments.TryGetProperty("radius", out var r)
            && r.TryGetInt32(out var rv) ? Math.Min(rv, 64) : 16;

        var action = new ActionData
        {
            Tool = ActionProtocol.QueryEntities,
            Arguments =
            {
                ["radius"] = (object?)radius,
            },
        };

        if (arguments.TryGetProperty("entityType", out var et) &&
            et.ValueKind == JsonValueKind.String)
        {
            action.Arguments["entityType"] = et.GetString();
        }

        await worldAdapter.SendActionAsync(action, cancellationToken);

        var filterDesc = action.Arguments.TryGetValue("entityType", out var ft) && ft is string fts
            ? $" type={fts}"
            : "";
        return new ToolResult(true, $"QueryEntities(radius={radius}{filterDesc}) dispatched.");
    }
}
