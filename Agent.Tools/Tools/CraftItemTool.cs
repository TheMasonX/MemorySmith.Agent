namespace Agent.Tools;

using Agent.Core;
using System.Text.Json;

/// <summary>
/// Crafts an item from materials already in the bot's inventory.
///
/// The Mineflayer adapter finds the recipe, checks for a nearby crafting table
/// if one is required (3x3 recipes), and calls bot.craft(). All required
/// ingredient items must already be in inventory before this action is dispatched.
///
/// Returns craftComplete event from Node.js on success.
/// Wire name: <c>craft</c> (ActionProtocol.Craft).
/// </summary>
public sealed class CraftItemTool(IWorldAdapter worldAdapter) : ITool
{
    public string Name => "CraftItem";
    public string Description =>
        "Craft an item from materials in inventory. A crafting table must be nearby for 3x3 recipes.";

    public JsonElement InputSchema => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "item":  { "type": "string",  "description": "Minecraft item ID, e.g. oak_planks" },
            "count": { "type": "integer", "description": "Number of crafting operations (default 1)" }
          },
          "required": ["item"]
        }
        """).RootElement;

    public async Task<ToolResult> ExecuteAsync(
        JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetProperty("item", out var itemEl))
            return new ToolResult(false, "CraftItem requires an 'item' argument.");

        var item  = itemEl.GetString() ?? string.Empty;
        var count = 1;
        if (arguments.TryGetProperty("count", out var countEl))
            count = countEl.GetInt32();

        var action = new ActionData
        {
            Tool      = ActionProtocol.Craft,
            Arguments = { ["item"] = item, ["count"] = (object?)count }
        };

        await worldAdapter.SendActionAsync(action, cancellationToken);
        return new ToolResult(true, $"CraftItem({item}, {count}) dispatched.");
    }
}
