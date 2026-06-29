namespace Agent.Tools;

using Agent.Core;
using System.Text.Json;

/// <summary>
/// Places a single block at the specified world coordinates.
///
/// The Node.js adapter navigates to within reach of the target location, tries all
/// six adjacent reference blocks, equips the material from inventory, and calls
/// bot.placeBlock(). Returns success once the block is placed; throws if the material
/// is not in inventory or no adjacent solid block can be found.
///
/// For blueprint-scale construction (Phase 3 Construction), use ConstructBlueprint
/// which sequences many PlaceBlock calls from a blueprint page.
/// </summary>
public sealed class PlaceBlockTool(IWorldAdapter worldAdapter) : ITool
{
    public string Name => "PlaceBlock";
    public string Description =>
        "Place a block at (x, y, z). The bot navigates to the site and places " +
        "against an adjacent solid block. Material must be in inventory " +
        "(e.g. 'cobblestone', 'oak_planks').";

    public JsonElement InputSchema => JsonDocument.Parse(
        "{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"integer\"},\"y\":{\"type\":\"integer\"},\"z\":{\"type\":\"integer\"},\"material\":{\"type\":\"string\",\"description\":\"Block name (with or without minecraft: prefix)\"},\"block\":{\"type\":\"string\",\"description\":\"Alias for material\"},\"count\":{\"type\":\"integer\",\"description\":\"Number of blocks to place (informational, ignored by adapter)\"}},\"required\":[\"x\",\"y\",\"z\"]}"
    ).RootElement;

    public async Task<ToolResult> ExecuteAsync(
        JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetProperty("x", out var xEl) ||
            !arguments.TryGetProperty("y", out var yEl) ||
            !arguments.TryGetProperty("z", out var zEl))
            return new ToolResult(false, "PlaceBlock requires x, y, and z.");

        // Accept 'material' or 'block' (planner may emit either)
        if (!arguments.TryGetProperty("material", out var matEl) &&
            !arguments.TryGetProperty("block", out matEl))
            return new ToolResult(false, "PlaceBlock requires material (or block).");

        var x        = xEl.GetInt32();
        var y        = yEl.GetInt32();
        var z        = zEl.GetInt32();
        var material = matEl.GetString() ?? "cobblestone";

        var action = new ActionData
        {
            Tool = ActionProtocol.Place,
            Arguments = { ["x"] = (object?)x, ["y"] = (object?)y, ["z"] = (object?)z, ["material"] = material }
        };

        await worldAdapter.SendActionAsync(action, cancellationToken);
        return new ToolResult(true, $"PlaceBlock({material} @ {x},{y},{z}) dispatched.");
    }
}
