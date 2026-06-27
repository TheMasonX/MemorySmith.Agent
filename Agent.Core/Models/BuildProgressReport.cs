namespace Agent.Core;

/// <summary>
/// TSK-0125: Read-only snapshot of per-block build progress.
/// Computed from WorldState per-block status facts.
/// </summary>
public sealed class BuildProgressReport
{
    public string BlueprintId { get; init; } = "";
    public int TotalBlocks { get; init; }
    public int PlacedCount { get; init; }
    public int SkippedCount { get; init; }
    public int InProgressCount { get; init; }
    public int PendingCount { get; init; }
    public double PercentComplete => TotalBlocks > 0 ? (double)PlacedCount / TotalBlocks * 100.0 : 0;

    /// <summary>
    /// Computes a progress report from WorldState facts for the given blueprint.
    /// Expects up to <paramref name="totalBlocks"/> per-block status facts.
    /// Missing facts are treated as <see cref="BuildFactKeys.BlockStatusPending"/>.
    /// </summary>
    public static BuildProgressReport FromFacts(WorldState state, string blueprintId, int totalBlocks)
    {
        var placed = 0;
        var skipped = 0;
        var inProgress = 0;
        var pending = 0;

        for (int i = 0; i < totalBlocks; i++)
        {
            var key = BuildFactKeys.BlockStatus(blueprintId, i);
            var status = state.Facts.TryGetValue(key, out var v) ? v?.ToString() : null;
            switch (status)
            {
                case BuildFactKeys.BlockStatusPlaced: placed++; break;
                case BuildFactKeys.BlockStatusSkipped: skipped++; break;
                case BuildFactKeys.BlockStatusInProgress: inProgress++; break;
                default: pending++; break;
            }
        }

        return new BuildProgressReport
        {
            BlueprintId = blueprintId,
            TotalBlocks = totalBlocks,
            PlacedCount = placed,
            SkippedCount = skipped,
            InProgressCount = inProgress,
            PendingCount = pending,
        };
    }

    public override string ToString() =>
        $"{BlueprintId}: {PlacedCount}/{TotalBlocks} placed, {SkippedCount} skipped, {PendingCount} pending";
}
