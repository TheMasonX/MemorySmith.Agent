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
/// Build origin is read from world-state facts at plan time:
///   Facts["build:{blueprintId}:origin:x"] (default 0)
///   Facts["build:{blueprintId}:origin:y"] (default 0)
///   Facts["build:{blueprintId}:origin:z"] (default 0)
///
/// Introduced in TSK-0011 Phase 4b.
/// </summary>
public sealed class BuildGoal(Blueprint blueprint, IReadOnlyList<PlacementBlock> blocks) : IGoal
{
    /// <summary>Blueprint metadata (id, name, materials, dimensions).</summary>
    public Blueprint Blueprint => blueprint;

    /// <summary>Flat ordered list of blocks to place, relative to the build origin.</summary>
    public IReadOnlyList<PlacementBlock> Blocks => blocks;

    /// <inheritdoc/>
    public string Name => $"Build:{blueprint.Id}";

    /// <inheritdoc/>
    public string Description =>
        $"Build {blueprint.Name} ({blocks.Count} blocks, {blueprint.Dimensions.X}x{blueprint.Dimensions.Y}x{blueprint.Dimensions.Z}).";

    /// <inheritdoc/>
    public string[] Phases => ["GatherMaterials", "Build", "Verify"];

    /// <inheritdoc/>
    public string? FailureReason { get; set; }

    /// <inheritdoc/>
    public bool IsComplete(WorldState state) =>
        state.Facts.TryGetValue($"goal:Build:{blueprint.Id}:complete", out var v) && IsTruthy(v);

    /// <inheritdoc/>
    public bool HasFailed(WorldState state) =>
        state.Facts.TryGetValue($"goal:Build:{blueprint.Id}:failed", out var v) && IsTruthy(v);

    private static bool IsTruthy(object? value) => value switch
    {
        bool b => b,
        string s when bool.TryParse(s, out var parsed) => parsed,
        _ => false,
    };
}
