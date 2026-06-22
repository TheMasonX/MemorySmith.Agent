namespace Agent.Core;

/// <summary>
/// Provenance of a fact in the agent's world state.
///
/// Sprint 36 P1-A: expanded with PlayerInstruction, Memory, Scan, Recovery
/// to support richer diagnostics, reasoning, and the Sprint 36 observation-driven
/// replanning loop. These four sources are metadata-only — no runtime behavior
/// changes in Sprint 36 (planned for Sprint 37 agent runtime decomposition).
///
/// Mappings from BuildGoal.BuildOriginSource enum (Sprint 35):
///   Explicit    → PlayerInstruction  (origin came from player chat coordinates)
///   AutoScanned → Scan               (origin found by FindFlatArea scan)
/// </summary>
public enum FactSource
{
    /// <summary>Directly observed from a world event (spawn, move, health, block mined, inventory snapshot, etc.).</summary>
    Observed,

    /// <summary>Inferred by the agent (e.g. error messages, constraint violations, deductions).</summary>
    Inferred,

    /// <summary>Persisted across sessions or restored from durable storage.</summary>
    Durable,

    // ── Sprint 36 P1-A expansion ──────────────────────────────────────────────

    /// <summary>
    /// Sprint 36 P1-A: Fact originated from a player instruction (chat command).
    /// Conceptually maps to BuildGoal.BuildOriginSource.Explicit for build-origin facts.
    /// Example: player says "build a house at 100 64 200" → origin facts tagged PlayerInstruction.
    /// </summary>
    PlayerInstruction,

    /// <summary>
    /// Sprint 36 P1-A: Fact came from a MemorySmith search result
    /// (SearchMemoryTool or GetPageTool).
    /// Example: block location retrieved from wiki page → tagged Memory.
    /// </summary>
    Memory,

    /// <summary>
    /// Sprint 36 P1-A: Fact produced by a world sensor scan (FindFlatArea, GetStatus, etc.).
    /// Conceptually maps to BuildGoal.BuildOriginSource.AutoScanned for build-origin facts.
    /// Example: flat area found at (100,64,200) after FindFlatArea scan → tagged Scan.
    /// </summary>
    Scan,

    /// <summary>
    /// Sprint 36 P1-A: Fact set during error recovery (TryRecoverFromGameErrorAsync).
    /// Marks facts that reflect the post-recovery believed world state — lower confidence
    /// than Observed because they derive from error interpretation, not direct observation.
    /// </summary>
    Recovery,
}

/// <summary>
/// A single fact with its key, value, provenance, and timestamp.
/// Stored alongside the legacy <see cref="WorldState.Facts"/> dictionary.
/// </summary>
public record Fact(string Key, string Value, FactSource Source, DateTimeOffset Timestamp);
