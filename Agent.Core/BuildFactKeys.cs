namespace Agent.Core;

/// <summary>
/// Named constants for World-State Fact keys shared between the fact writer
/// (<c>AgentBackgroundService</c>) and the fact reader (<c>HtnTaskLibrary</c>).
///
/// Sprint 9: introduced to eliminate the string-duplication coupling that
/// previously existed between the two assemblies. Any refactor of a key now
/// produces a single compile-time error instead of a silent runtime miss.
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

    // ── Flat-area observation keys ────────────────────────────────────────────
    // Written by WorldStateProjector; can be read by any planner component
    // that wants the last scan result without subscribing to events.

    /// <summary>Area (cell count) of the most recently observed flat region.</summary>
    public const string LastFlatArea = "flat:last:area";

    /// <summary>Y-range (max − min elevation) of the most recently observed flat region.</summary>
    public const string LastFlatYRange = "flat:last:yRange";

    /// <summary>Compactness score (0.0–1.0) of the most recently observed flat region.</summary>
    public const string LastFlatCompactness = "flat:last:compactness";
}
