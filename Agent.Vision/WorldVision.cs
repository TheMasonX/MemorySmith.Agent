namespace Agent.Vision;

using Agent.Core;

/// <summary>
/// Deterministic world vision layer using Mineflayer state data.
/// Provides block/entity queries without LLM involvement.
/// Feeds into ISpatialAnalyzer for environmental metric computation.
/// </summary>
public sealed class WorldVision
{
    private WorldState? _state;

    public void UpdateState(WorldState state) => _state = state;

    /// <summary>Returns the block type at the given coordinates, if known.</summary>
    public string? GetBlockAt(int x, int y, int z)
        => _state?.Facts.TryGetValue($"block:{x},{y},{z}", out var v) == true ? v?.ToString() : null;

    /// <summary>Returns all entity facts currently tracked.</summary>
    public IEnumerable<KeyValuePair<string, object?>> GetNearbyEntities()
        => _state?.Facts.Where(f => f.Key.StartsWith("entity:", StringComparison.OrdinalIgnoreCase))
           ?? [];
}
