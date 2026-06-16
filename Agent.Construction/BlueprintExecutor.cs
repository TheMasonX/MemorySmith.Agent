namespace Agent.Construction;

using Agent.Core;

/// <summary>
/// Default <see cref="IBlueprintExecutor"/>. Emits PlaceBlock actions for every non-air
/// block in the blueprint, sorted floor-first (Y ascending) to ensure structural integrity.
///
/// Each <see cref="ActionData"/> uses:
/// - <c>Tool = "PlaceBlock"</c> (matches <c>PlaceBlockTool.Name</c>)
/// - Arguments: <c>material</c> (block ID), <c>x</c>, <c>y</c>, <c>z</c> (absolute world coords)
///
/// The Mineflayer adapter handles bot navigation and block placement against the nearest
/// adjacent solid block. It also handles two-block entities (doors, beds) automatically
/// when the lower/head block is placed.
/// </summary>
public sealed class BlueprintExecutor : IBlueprintExecutor
{
    private const string PlaceBlockToolName = "PlaceBlock";

    /// <inheritdoc/>
    public IReadOnlyList<ActionData> Execute(
        IReadOnlyList<PlacementBlock> blocks,
        int originX, int originY, int originZ)
    {
        // Order: Y ascending (floor → walls → roof), then Z (back to front), then X (west to east).
        // This order ensures that each block has a foundation before it's placed.
        var ordered = blocks
            .OrderBy(b => b.Y)
            .ThenBy(b => b.Z)
            .ThenBy(b => b.X);

        var actions = new List<ActionData>(blocks.Count);
        foreach (var block in ordered)
        {
            var action = new ActionData
            {
                Tool = PlaceBlockToolName,
                Arguments =
                {
                    ["material"] = block.BlockId,
                    ["x"]        = (object?)(originX + block.X),
                    ["y"]        = (object?)(originY + block.Y),
                    ["z"]        = (object?)(originZ + block.Z),
                }
            };
            actions.Add(action);
        }

        return actions;
    }
}
