namespace Agent.Tools;

using Agent.Core;
using System.Text.Json;

/// <summary>
/// Smelts an item in a nearby furnace.
///
/// The Mineflayer adapter navigates to the nearest furnace (within 16 blocks),
/// opens it, adds fuel (default: coal) if empty, places the input item, waits
/// for at least one output item, takes it, then closes the furnace.
///
/// The input item must be in inventory before dispatch. The furnace must
/// already exist in the world (the bot does not place a new furnace).
///
/// Returns smeltComplete event from Node.js on success.
/// Wire name: <c>smelt</c> (ActionProtocol.Smelt).
/// </summary>
public sealed class FurnaceTool(IWorldAdapter worldAdapter) : ITool
{
    public string Name => "SmeltItem";
    public string Description =>
        "Smelt an item in a nearby furnace. Requires the item in inventory and a furnace within 16 blocks.";

    public JsonElement InputSchema => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "item":  { "type": "string",  "description": "Minecraft item/block ID to smelt, e.g. iron_ore, sand" },
            "count": { "type": "integer", "description": "Number of items to smelt (default 1)" },
            "fuel":  { "type": "string",  "description": "Fuel item name (default: coal)" }
          },
          "required": ["item"]
        }
        """).RootElement;

    public async Task<ToolResult> ExecuteAsync(
        JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetProperty("item", out var itemEl))
            return new ToolResult(false, "SmeltItem requires an 'item' argument.");

        var item  = itemEl.GetString() ?? string.Empty;
        var count = 1;
        var fuel  = "coal";

        if (arguments.TryGetProperty("count", out var countEl))
            count = countEl.GetInt32();
        if (arguments.TryGetProperty("fuel", out var fuelEl))
            fuel = fuelEl.GetString() ?? "coal";

        var action = new ActionData
        {
            Tool      = ActionProtocol.Smelt,
            Arguments =
            {
                ["item"]  = item,
                ["count"] = (object?)count,
                ["fuel"]  = fuel,
            }
        };

        await worldAdapter.SendActionAsync(action, cancellationToken);
        return new ToolResult(true, $"SmeltItem({item}, {count}, fuel={fuel}) dispatched.");
    }
}
