namespace Agent.Core;

/// <summary>
/// Named constants for World-State Fact keys shared between fact writers
/// and fact readers across the Agent.* assemblies.
///
/// Sprint 9: introduced to eliminate the string-duplication coupling between
/// <c>AgentBackgroundService</c> (writer) and <c>HtnTaskLibrary</c> (reader).
///
/// Sprint 10: added build-progress checkpoint keys (B2).
/// </summary>
public static class BuildFactKeys
{
    // ── Auto-origin keys ──────────────────────────────────────────────────────
    // Set by AgentBackgroundService when a FlatAreaFoundEvent arrives with
    // Area ≥ MinUsableFlatArea. Read by HtnTaskLibrary.DecomposeBuild when
    // the caller supplies origin (0, 0, 0) as a "let the scanner decide" sentinel.

    /// <summary>Blueprint ID used for auto-detected flat-area origins.</summary>
    public const string AutoBlueprintId = "auto";

    /// <summary>World X coordinate of the auto-selected build origin.</summary>
    public const string AutoOriginX = $"build:{AutoBlueprintId}:origin:x";

    /// <summary>World Y coordinate of the auto-selected build origin.</summary>
    public const string AutoOriginY = $"build:{AutoBlueprintId}:origin:y";

    /// <summary>World Z coordinate of the auto-selected build origin.</summary>
    public const string AutoOriginZ = $"build:{AutoBlueprintId}:origin:z";

    // ── Build-progress checkpoint keys (Sprint 10 B2) ─────────────────────────
    // Written by AgentBackgroundService after each successful PlaceBlock action.
    // Read by HtnTaskLibrary.DecomposeBuild to resume from the last checkpoint
    // instead of re-placing already-placed blocks.
    //
    // TSK-0125: replaced single-index checkpoint with per-block status facts.
    // The legacy key is kept for backward compatibility during migration.

    /// <summary>
    /// [LEGACY — TSK-0125] Returns the fact key for the last successfully placed
    /// block index within a specific blueprint. Replaced by per-block status facts
    /// for checkpointing; still used by ReplanGovernor for stall detection.
    /// </summary>
    public static string BuildProgressIndex(string blueprintId) =>
        $"build:{blueprintId}:progress:index";

    // ── Per-block status keys (TSK-0125) ──────────────────────────────────
    // Each block in a blueprint has an independent status fact.
    // Status transitions: pending → in-progress → placed | skipped
    // - pending: not yet dispatched (default if fact is absent)
    // - in-progress: dispatched to adapter, awaiting BlockPlacedEvent
    // - placed: confirmed by BlockPlacedEvent
    // - skipped: cannot place (bot position, terrain occupied, no reference)

    /// <summary>Status value for blocks not yet attempted.</summary>
    public const string BlockStatusPending = "pending";

    /// <summary>Status value for blocks dispatched but unconfirmed.</summary>
    public const string BlockStatusInProgress = "in-progress";

    /// <summary>Status value for blocks confirmed placed.</summary>
    public const string BlockStatusPlaced = "placed";

    /// <summary>Status value for blocks that could not be placed.</summary>
    public const string BlockStatusSkipped = "skipped";

    /// <summary>
    /// Returns the fact key for per-block status in a blueprint.
    /// Format: build:{blueprintId}:block:{index}:status
    /// </summary>
    public static string BlockStatus(string blueprintId, int blockIndex) =>
        $"build:{blueprintId}:block:{blockIndex}:status";

    /// <summary>
    /// Returns the fact key prefix for all blocks in a blueprint.
    /// Used by ClearFactsByPrefix to remove all build facts for a blueprint.
    /// </summary>
    public static string BlockStatusPrefix(string blueprintId) =>
        $"build:{blueprintId}:block:";

    /// <summary>
    /// Context key added to each PlaceBlock <see cref="ActionData"/> so that
    /// <see cref="AgentBackgroundService"/> can write the checkpoint fact on success.
    /// </summary>
    public const string PlaceBlockProgressBlueprintId = "build:progress:blueprintId";

    /// <summary>
    /// Context key carrying the 0-based block index for a PlaceBlock action.
    /// </summary>
    public const string PlaceBlockProgressBlockIndex = "build:progress:blockIndex";

    // ── Flat-area observation keys ────────────────────────────────────────────
    // Written by WorldStateProjector; readable by any planner component.

    /// <summary>Area (cell count) of the most recently observed flat region.</summary>
    public const string LastFlatArea = "flat:last:area";

    /// <summary>Y-range (max − min elevation) of the most recently observed flat region.</summary>
    public const string LastFlatYRange = "flat:last:yRange";

    /// <summary>Compactness score (0.0–1.0) of the most recently observed flat region.</summary>
    public const string LastFlatCompactness = "flat:last:compactness";
}
