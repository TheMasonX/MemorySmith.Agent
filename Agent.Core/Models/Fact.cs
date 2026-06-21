namespace Agent.Core;

/// <summary>
/// Provenance of a fact in the agent's world state.
/// </summary>
public enum FactSource
{
    /// <summary>Directly observed from a world event (spawn, move, health, block mined, inventory snapshot, etc.).</summary>
    Observed,

    /// <summary>Inferred by the agent (e.g. error messages, constraint violations, deductions).</summary>
    Inferred,

    /// <summary>Persisted across sessions or restored from durable storage.</summary>
    Durable,
}

/// <summary>
/// A single fact with its key, value, provenance, and timestamp.
/// Stored alongside the legacy <see cref="WorldState.Facts"/> dictionary.
/// </summary>
public record Fact(string Key, string Value, FactSource Source, DateTimeOffset Timestamp);
