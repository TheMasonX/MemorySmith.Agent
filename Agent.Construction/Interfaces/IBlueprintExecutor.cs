namespace Agent.Construction;

using Agent.Core;

/// <summary>
/// Converts a blueprint's <see cref="PlacementBlock"/> list into an ordered sequence of
/// <see cref="ActionData"/> PlaceBlock instructions ready for dispatch.
///
/// Each block becomes one PlaceBlock action; blocks are ordered floor-first (Y ascending)
/// to ensure structural integrity (floor before walls before roof before furnishings).
/// World coordinates are computed by adding the blueprint-relative offsets to the supplied
/// build origin.
/// </summary>
public interface IBlueprintExecutor
{
    /// <summary>
    /// Emits one PlaceBlock action per non-air block in <paramref name="blocks"/>.
    ///
    /// <paramref name="originX"/>, <paramref name="originY"/>, <paramref name="originZ"/>
    /// are the world-space coordinates of the blueprint's (0,0,0) anchor point
    /// (typically the southwest bottom corner of the structure).
    /// </summary>
    IReadOnlyList<ActionData> Execute(
        IReadOnlyList<PlacementBlock> blocks,
        int originX, int originY, int originZ);
}
