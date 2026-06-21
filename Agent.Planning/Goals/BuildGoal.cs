namespace Agent.Planning.Goals;

using Agent.Construction;
using Agent.Core;

/// <summary>
/// Builds a structure defined by a <see cref="Blueprint"/> and its parsed
/// <see cref="PlacementBlock"/> list.
///
/// Phases: GatherMaterials → Build → Verify
///   GatherMaterials — mine raw blocks required by the blueprint that are not yet
///     in inventory. Crafted items (planks from logs, slabs, torches, etc.) must
///     be prepared beforehand or via a separate CraftItem goal (Phase 5).
///   Build           — emit PlaceBlock actions in Y-ascending order.
///   Verify          — emit GetStatus to confirm completion.
///
/// IsComplete: world-state fact "goal:Build:{blueprintId}:complete" = true
///   Set by AgentBackgroundService after the Verify phase succeeds.
///
/// HasFailed: world-state fact "goal:Build:{blueprintId}:failed" = true.
///
/// Origin resolution priority (Sprint 35):
///   1. Explicit origin passed via constructor (<see cref="OriginX"/>, <see cref="OriginY"/>,
///      <see cref="OriginZ"/>) — e.g. from chat "build a house at 100 64 200".
///   2. World-state facts: "build:{blueprintId}:origin:{axis}" — e.g. from REST API or prior run.
///   3. Auto-detect: FindFlatArea action emitted to locate the nearest suitable flat spot.
///
/// Blueprints are "stamps" — they never store absolute positions. All coordinates
/// are relative offsets applied on top of the resolved origin.
///
/// Introduced in TSK-0011 Phase 4b. Sprint 35: explicit origin support + auto-find fallback.
/// </summary>
public sealed class BuildGoal : IGoal
{
    /// <summary>Blueprint metadata (id, name, materials, dimensions).</summary>
    public Blueprint Blueprint { get; }

    /// <summary>Flat ordered list of blocks to place, relative to the build origin.</summary>
    public IReadOnlyList<PlacementBlock> Blocks { get; }

    /// <summary>Explicit world-coordinate origin from chat (e.g. "build a house at 100 64 200").</summary>
    public int? OriginX { get; }
    /// <summary>Explicit world-coordinate origin from chat.</summary>
    public int? OriginY { get; }
    /// <summary>Explicit world-coordinate origin from chat.</summary>
    public int? OriginZ { get; }

    /// <summary>True when the builder explicitly supplied a build origin.</summary>
    public bool HasExplicitOrigin => OriginX.HasValue || OriginY.HasValue || OriginZ.HasValue;

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string Description { get; }

    public BuildGoal(
        Blueprint blueprint,
        IReadOnlyList<PlacementBlock> blocks,
        int? originX = null,
        int? originY = null,
        int? originZ = null)
    {
        Blueprint = blueprint;
        Blocks = blocks;
        Name = $"Build:{blueprint.Id}";
        OriginX = originX;
        OriginY = originY;
        OriginZ = originZ;
        Description = originX.HasValue
            ? $"Build {blueprint.Name} ({blocks.Count} blocks) at ({originX},{originY},{originZ})."
            : $"Build {blueprint.Name} ({blocks.Count} blocks, {blueprint.Dimensions.X}x{blueprint.Dimensions.Y}x{blueprint.Dimensions.Z}).";
    }

    /// <inheritdoc/>
    public string[] Phases => ["GatherMaterials", "Build", "Verify"];

    /// <inheritdoc/>
    public string? FailureReason { get; set; }

    /// <inheritdoc/>
    public bool IsComplete(WorldState state) =>
        state.Facts.TryGetValue($"goal:Build:{Blueprint.Id}:complete", out var v) && IsTruthy(v);

    /// <inheritdoc/>
    public bool HasFailed(WorldState state) =>
        state.Facts.TryGetValue($"goal:Build:{Blueprint.Id}:failed", out var v) && IsTruthy(v);

    private static bool IsTruthy(object? value) => value switch
    {
        bool b => b,
        string s when bool.TryParse(s, out var parsed) => parsed,
        _ => false,
    };
}
