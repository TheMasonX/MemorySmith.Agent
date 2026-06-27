namespace Agent.Core;

using System.Text.Json;

/// <summary>
/// Rule-based world model that tracks observations, beliefs, and predictions.
/// Uses a simple running average for uncertainty. No ML — pure deterministic rules.
/// Thread-safe for concurrent access.
///
/// Sprint 25 P1-A: Constructor and Observe now use defensive copies for inventory
/// dictionaries, eliminating shared mutable state between _observed and _belief.
/// </summary>
public sealed class WorldModel : IWorldModel
{
    private readonly object _lock = new();

    private ObservationState _observed;
    private BeliefState _belief;

    // Running uncertainty tracking — plain Queue guarded by _lock (no need for ConcurrentQueue)
    private readonly Queue<double> _recentDeviationScores = new();
    private const int MaxDeviationSamples = 20;
    private double _cachedUncertainty;

    public ObservationState Observed
    {
        get { lock (_lock) return _observed; }
    }

    public BeliefState Belief
    {
        get { lock (_lock) return _belief; }
    }

    public double Uncertainty
    {
        get { lock (_lock) return _cachedUncertainty; }
    }

    public WorldModel()
    {
        // Spr�nt 25 P1-A: separate dictionary instances for each state.
        // Previously a single empty dict was shared between _observed and _belief,
        // meaning mutations to one would silently corrupt the other.
        _observed = new ObservationState(20, 20, new Position(0, 0, 0),
            new Dictionary<string, int>(), [], DateTimeOffset.UtcNow);
        _belief = new BeliefState(20, 20, new Position(0, 0, 0),
            new Dictionary<string, int>(), [], DateTimeOffset.UtcNow);
    }

    public void Observe(ObservationState observation)
    {
        lock (_lock)
        {
            _observed = observation;
            // Sprint 25 P1-A: deep-copy the inventory so mutations to the source
            // observation dictionary cannot corrupt belief state.
            _belief = new BeliefState(
                observation.Health,
                observation.Food,
                observation.Position,
                new Dictionary<string, int>(observation.Inventory),
                observation.RecentObservations
                    .Select(f => new Fact(f.Key, f.Value, FactSource.Observed, f.Timestamp))
                    .ToList(),
                DateTimeOffset.UtcNow);
        }
    }

    public PredictionState Predict(string toolName, IReadOnlyDictionary<string, object?> args)
    {
        BeliefState current;
        lock (_lock) { current = _belief; }

        return toolName switch
        {
            "move" => PredictMove(current, args),
            "status" => PredictStatus(current),
            "mine" => PredictMine(current, args),
            "craft" => PredictCraft(current, args),
            "place" => PredictPlace(current, args),
            "smelt" => PredictSmelt(current, args),
            "wander" => PredictWander(current, args),
            "chat" => PredictNoChange(current, toolName, args),
            "findFlatArea" => PredictNoChange(current, toolName, args),
            _ => PredictUnknown(current, toolName, args),
        };
    }

    /// <summary>
    /// Computes how well the prediction matched the actual observation and updates
    /// the running uncertainty average. The entire operation (enqueue, trim, cache-update)
    /// is performed under <see cref="_lock"/> so concurrent Reconcile calls are fully atomic.
    /// </summary>
    public double Reconcile(PredictionState prediction, ObservationState actual)
    {
        // Compute deviation from the (immutable) passed-in arguments — no shared state involved.
        double score = 0.0;
        int factors = 0;

        if (prediction.PredictedPosition is { } pp && actual.Position is { } ap)
        {
            var dx = pp.X - ap.X;
            var dy = pp.Y - ap.Y;
            var dz = pp.Z - ap.Z;
            var dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            // Normalize: 0 = perfect, approaches 1 as distance grows
            score += Math.Min(dist / 50.0, 1.0);
            factors++;
        }

        // Health deviation
        score += Math.Abs(prediction.PredictedHealth - actual.Health) / 20.0;
        factors++;

        // Food deviation
        score += Math.Abs(prediction.PredictedFood - actual.Food) / 20.0;
        factors++;

        var deviation = factors > 0 ? score / factors : 0.0;

        // Update running average under _lock so enqueue, trim, and cache-update are atomic.
        lock (_lock)
        {
            _recentDeviationScores.Enqueue(deviation);
            while (_recentDeviationScores.Count > MaxDeviationSamples)
                _recentDeviationScores.TryDequeue(out _);
            _cachedUncertainty = _recentDeviationScores.Count > 0
                ? _recentDeviationScores.Average()
                : deviation;
        }

        return deviation;
    }

    // ── Rule-based predictors ─────────────────────────────────────────────

    private static PredictionState PredictMove(BeliefState b, IReadOnlyDictionary<string, object?> args)
    {
        var x = GetIntArg(args, "x");
        var y = GetIntArg(args, "y");
        var z = GetIntArg(args, "z");
        return new PredictionState("move", args,
            new Position(x, y, z),
            b.Health, b.Food - 1, // walking costs food
            b.Inventory,
            0.95,
            $"Move to ({x},{y},{z}); food -1 for travel cost");
    }

    private static PredictionState PredictStatus(BeliefState b) =>
        new("status", new Dictionary<string, object?>(),
            b.Position, b.Health, b.Food, b.Inventory,
            1.0, "Status query — no state change");

    private static PredictionState PredictMine(BeliefState b, IReadOnlyDictionary<string, object?> args)
    {
        var block = GetStrArg(args, "block");
        var count = GetIntArg(args, "count", 1);
        var newInv = new Dictionary<string, int>(b.Inventory);
        // TSK-0108: use shared BlockToItemDrop mapping so prediction agrees with
        // WorldStateProjector projection (e.g. diamond_ore → diamond, not diamond_ore).
        var itemKey = CommonMinecraftBlocks.ResolveBlockDrop(block);
        newInv[itemKey] = newInv.GetValueOrDefault(itemKey) + count;
        return new PredictionState("mine", args,
            b.Position, b.Health, b.Food - 1,
            newInv, 0.90,
            $"Mine {count}x {block} → +{count} {itemKey}; food -1");
    }

    private static PredictionState PredictCraft(BeliefState b, IReadOnlyDictionary<string, object?> args)
    {
        var item = GetStrArg(args, "item");
        var count = GetIntArg(args, "count", 1);
        var newInv = new Dictionary<string, int>(b.Inventory);
        newInv[item] = newInv.GetValueOrDefault(item) + count;
        return new PredictionState("craft", args,
            b.Position, b.Health, b.Food,
            newInv, 0.75, // crafting has ingredients uncertainty
            $"Craft {count}x {item}");
    }

    private static PredictionState PredictPlace(BeliefState b, IReadOnlyDictionary<string, object?> args) =>
        new("place", args, b.Position, b.Health, b.Food, b.Inventory,
            0.90, "Place block — inventory unchanged (consumed by action)");

    private static PredictionState PredictSmelt(BeliefState b, IReadOnlyDictionary<string, object?> args) =>
        new("smelt", args, b.Position, b.Health, b.Food, b.Inventory,
            0.80, "Smelt — outcome depends on furnace state");

    private static PredictionState PredictWander(BeliefState b, IReadOnlyDictionary<string, object?> args) =>
        new("wander", args, null, b.Health, b.Food - 1, b.Inventory,
            0.50, "Wander — position unpredictable");

    private static PredictionState PredictNoChange(BeliefState b, string tool, IReadOnlyDictionary<string, object?> args) =>
        new(tool, args, b.Position, b.Health, b.Food, b.Inventory,
            1.0, $"{tool} — no state change expected");

    private static PredictionState PredictUnknown(BeliefState b, string tool, IReadOnlyDictionary<string, object?> args) =>
        new(tool, args, null, b.Health, b.Food, b.Inventory,
            0.30, $"Unknown tool '{tool}' — low confidence prediction");

    /// <summary>
    /// Extracts an integer argument from the args dictionary.
    /// Handles int, long, double, and JsonElement (JSON-deserialised args) gracefully.
    /// Returns <paramref name="fallback"/> if the key is absent or the value is not numeric.
    /// </summary>
    private static int GetIntArg(IReadOnlyDictionary<string, object?> args, string key, int fallback = 0)
    {
        if (!args.TryGetValue(key, out var v)) return fallback;
        return v switch
        {
            int i    => i,
            long l   => (int)l,
            double d => (int)d,
            JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var ji) => ji,
            _ => fallback,
        };
    }

    private static string GetStrArg(IReadOnlyDictionary<string, object?> args, string key, string fallback = "unknown")
    {
        return args.TryGetValue(key, out var v) && v is string s ? s : fallback;
    }
}
