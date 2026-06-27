namespace Agent.Planning.Goals;

/// <summary>
/// Value object representing a resolved build origin with its provenance.
///
/// Consolidates the three nullable coordinate fields (OriginX, OriginY, OriginZ)
/// and <see cref="BuildOriginSource"/> enum previously stored as separate properties
/// on <see cref="BuildGoal"/> (TSK-0103, absorbing TSK-0098).
///
/// Key design decisions:
/// - Atomic: all three coordinates must be present. Partial coordinates are rejected.
/// - Self-documenting: <see cref="Source"/> tracks HOW the origin was determined.
/// - No sentinel overloading: missing origin is represented as <c>null</c>, not (0,0,0).
///
/// Use <see cref="FromNullable"/> to safely construct from nullable components.
/// </summary>
public sealed record BuildOrigin(int X, int Y, int Z, BuildOriginSource Source)
{
    /// <summary>
    /// Safely constructs a <see cref="BuildOrigin"/> from nullable coordinate components.
    /// Returns <c>null</c> when any coordinate is missing (prevents partial origin ambiguity).
    /// </summary>
    public static BuildOrigin? FromNullable(int? x, int? y, int? z, BuildOriginSource source = BuildOriginSource.AutoScanned)
    {
        if (x is null || y is null || z is null)
            return null;
        return new BuildOrigin(x.Value, y.Value, z.Value, source);
    }
}
