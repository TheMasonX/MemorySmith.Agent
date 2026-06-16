namespace Agent.Tools;

using Agent.Core;
using System.Text.Json;

/// <summary>
/// Sends the bot on a short exploratory walk in a random nearby direction.
///
/// Node.js picks a random azimuth, computes a target within <paramref name="radius"/> blocks,
/// and optionally clamps the destination to stay within <c>maxDistanceFromSpawn</c> of the
/// bot's spawn point. This prevents the bot from wandering too far from base.
///
/// Phase 4 (TSK-future): persist interesting observations (biome, structure) discovered
/// during exploration as MemorySmith wiki pages via CreatePage.
/// </summary>
public sealed class WanderTool(IWorldAdapter worldAdapter) : ITool
{
    public string Name => "Wander";
    public string Description =>
        "Walk in a random nearby direction to explore the world. " +
        "Use 'radius' (default 20) to control wander distance and " +
        "'maxDistanceFromSpawn' (default 100) to set a hard boundary.";

    public JsonElement InputSchema => JsonDocument.Parse(
        "{\"type\":\"object\",\"properties\":{\"radius\":{\"type\":\"integer\",\"description\":\"Max wander radius in blocks (default 20)\"},\"maxDistanceFromSpawn\":{\"type\":\"integer\",\"description\":\"Hard boundary from spawn point in blocks (default 100)\"}},\"required\":[]}"
    ).RootElement;

    public async Task<ToolResult> ExecuteAsync(
        JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var radius  = arguments.TryGetProperty("radius",             out var r) ? r.GetInt32() : 20;
        var maxDist = arguments.TryGetProperty("maxDistanceFromSpawn", out var m) ? m.GetInt32() : 100;

        var action = new ActionData
        {
            Tool = ActionProtocol.Wander,
            Arguments = { ["radius"] = (object?)radius, ["maxDistanceFromSpawn"] = (object?)maxDist }
        };

        await worldAdapter.SendActionAsync(action, cancellationToken);
        return new ToolResult(true, $"Wander(radius={radius}, boundary={maxDist}) dispatched.");
    }
}
