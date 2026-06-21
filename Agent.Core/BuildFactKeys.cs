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

    /// <summary>
    /// Returns the fact key for the last successfully placed block index within
    /// a specific blueprint build plan. Index is 0-based; the next unplaced
    /// block is at <c>index + 1</c>.
    /// </summary>
    public static string BuildProgressIndex(string blueprintId) =>
        $"build:{blueprintId}:progress:index";

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
