namespace Agent.Core;

/// <summary>
/// Sprint 55 (TSK-0155): Captures the delta between expected and observed world state
/// after a set of actions complete. Drives the observation-driven replan comparison loop:
///   Plan → Dispatch → Observe → Compare (WorldStateDiff) → Replan?
///
/// Each dimension captures what was expected vs. what actually happened:
///   - Inventory: items gained/lost vs. expected gains/losses
///   - Position: where the bot ended up vs. where it was expected to be
///   - Health: damage taken vs. expected (or none)
///   - Entity: new threats that appeared during action execution
///
/// A null dimension means "not applicable for this action set" (e.g., a Chat action
/// has no inventory expectation). An empty/matching dimension means "expectation met."
/// </summary>
/// <param name="InventoryGained">Items expected to be gained (item → count).</param>
/// <param name="InventoryLost">Items expected to be lost (item → count).</param>
/// <param name="ActualInventoryDelta">Net inventory change observed (item → delta, positive = gain).</param>
/// <param name="ExpectedPosition">Where the bot was expected to end up, or null if no movement was expected.</param>
/// <param name="ActualPosition">Where the bot actually is after actions.</param>
/// <param name="HealthDelta">Observed health change (negative = damage). 0 = no change.</param>
/// <param name="NewThreats">Entity types that appeared during execution (e.g., "zombie", "skeleton").</param>
/// <param name="OutcomeSummary">Human-readable summary of mismatches, or empty if all expectations met.</param>
public sealed record WorldStateDiff(
    IReadOnlyDictionary<string, int>? InventoryGained = null,
    IReadOnlyDictionary<string, int>? InventoryLost = null,
    IReadOnlyDictionary<string, int>? ActualInventoryDelta = null,
    Position? ExpectedPosition = null,
    Position? ActualPosition = null,
    int HealthDelta = 0,
    IReadOnlyList<string>? NewThreats = null,
    string OutcomeSummary = "")
{
    /// <summary>
    /// True when any observed outcome contradicts the expected outcome.
    /// Drives the replan decision in the observe→evaluate loop.
    /// </summary>
    public bool HasMismatch =>
        HasInventoryMismatch ||
        HasPositionMismatch ||
        HealthDelta < 0 ||
        (NewThreats is { Count: > 0 });

    /// <summary>
    /// True when inventory changes don't match expectations.
    /// Sprint 58 Wave D (TSK-0334): also detects unexpected inventory changes
    /// (items gained/lost that weren't in the expected gain/loss lists).
    /// Sprint 59 (TSK-0344): now also returns true when there are unexpected
    /// changes even without expected gains/losses (e.g. unmodeled side effects).
    /// </summary>
    public bool HasInventoryMismatch
    {
        get
        {
            // Sprint 59 (TSK-0344): check for unexpected changes first —
            // even without expected gains/losses, unexpected inventory deltas
            // are real anomalies worth surfacing.
            if (HasUnexpectedChanges) return true;

            if (InventoryGained is null && InventoryLost is null) return false;
            if (ActualInventoryDelta is null) return false;

            // Check expected gains: did we actually get what we expected?
            if (InventoryGained is not null)
            {
                foreach (var (item, expectedCount) in InventoryGained)
                {
                    var actual = ActualInventoryDelta.GetValueOrDefault(item, 0);
                    if (actual < expectedCount) return true;
                }
            }

            // Check expected losses: did we actually lose what we expected?
            if (InventoryLost is not null)
            {
                foreach (var (item, expectedCount) in InventoryLost)
                {
                    // Expected loss appears as negative delta
                    var actual = ActualInventoryDelta.GetValueOrDefault(item, 0);
                    if (actual > -expectedCount) return true; // didn't lose enough (or gained instead)
                }
            }

            return false;
        }
    }

    /// <summary>
    /// True when inventory items changed that were not in either the expected
    /// gains or expected losses. Detects unmodeled side effects such as mob drops,
    /// other players, or environmental pickup.
    /// Sprint 59 (TSK-0344): extracted as a standalone property for use by the
    /// evaluator prompt context.
    /// </summary>
    public bool HasUnexpectedChanges
    {
        get
        {
            if (ActualInventoryDelta is null || ActualInventoryDelta.Count == 0)
                return false;

            var expectedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (InventoryGained is not null)
            {
                foreach (var k in InventoryGained.Keys) expectedKeys.Add(k);
            }
            if (InventoryLost is not null)
            {
                foreach (var k in InventoryLost.Keys) expectedKeys.Add(k);
            }

            // If there are no expected keys and any delta is non-zero, it's unexpected.
            if (expectedKeys.Count == 0)
            {
                return ActualInventoryDelta.Values.Any(static delta => delta != 0);
            }

            foreach (var (item, delta) in ActualInventoryDelta)
            {
                if (delta == 0) continue;
                if (!expectedKeys.Contains(item)) return true; // unexpected change
            }

            return false;
        }
    }

    /// <summary>
    /// True when the bot's position differs significantly from expectation.
    /// </summary>
    public bool HasPositionMismatch =>
        ExpectedPosition is not null &&
        ActualPosition is not null &&
        (Math.Abs(ExpectedPosition.X - ActualPosition.X) > 3 ||
         Math.Abs(ExpectedPosition.Y - ActualPosition.Y) > 2 ||
         Math.Abs(ExpectedPosition.Z - ActualPosition.Z) > 3);

    /// <summary>
    /// True when new threats appeared that weren't present before.
    /// </summary>
    public bool HasThreats => NewThreats is { Count: > 0 };

    /// <summary>
    /// Returns a concise human-readable description of mismatches for the LLM evaluator.
    /// Sprint 59 (TSK-0344): describes unexpected inventory changes when present.
    /// </summary>
    public string DescribeMismatches()
    {
        var parts = new List<string>();

        if (HasInventoryMismatch)
        {
            // Describe expected-miss shortfalls first.
            if (InventoryGained is not null && ActualInventoryDelta is not null)
            {
                var missed = new List<string>();
                foreach (var (item, expected) in InventoryGained)
                {
                    var actual = ActualInventoryDelta.GetValueOrDefault(item, 0);
                    if (actual < expected)
                        missed.Add($"{item} (expected +{expected}, got +{actual})");
                }
                if (missed.Count > 0)
                    parts.Add($"Inventory shortfall: {string.Join(", ", missed)}");
            }

            // Sprint 59 (TSK-0344): describe unexpected inventory changes.
            if (HasUnexpectedChanges && ActualInventoryDelta is not null)
            {
                var unexpected = new List<string>();
                var expectedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (InventoryGained is not null)
                    foreach (var k in InventoryGained.Keys) expectedKeys.Add(k);
                if (InventoryLost is not null)
                    foreach (var k in InventoryLost.Keys) expectedKeys.Add(k);

                foreach (var (item, delta) in ActualInventoryDelta)
                {
                    if (delta == 0) continue;
                    if (!expectedKeys.Contains(item))
                        unexpected.Add($"{item} {(delta > 0 ? "+" : "")}{delta}");
                }
                if (unexpected.Count > 0)
                    parts.Add($"Unexpected inventory: {string.Join(", ", unexpected)}");
            }
        }

        if (HasPositionMismatch && ExpectedPosition is not null && ActualPosition is not null)
            parts.Add($"Position drift: expected ({ExpectedPosition.X},{ExpectedPosition.Y},{ExpectedPosition.Z}), actual ({ActualPosition.X},{ActualPosition.Y},{ActualPosition.Z})");

        if (HealthDelta < 0)
            parts.Add($"Health dropped by {Math.Abs(HealthDelta)} HP");

        if (HasThreats)
            parts.Add($"New threats: {string.Join(", ", NewThreats!)}");

        return parts.Count > 0 ? string.Join("; ", parts) : "";
    }

    /// <summary>Empty diff — no expectations to compare against.</summary>
    public static WorldStateDiff Empty => new();
}
