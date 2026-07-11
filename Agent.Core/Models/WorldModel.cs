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

        // Sprint 59 (TSK-0336/TSK-0309): normalize tool name to lowercase to bridge
        // the domain mismatch between PascalCase ITool.Name values (e.g. "MineBlock",
        // "CraftItem") and lowercase wire-protocol names (e.g. "mine", "craft").
        // Also accept the PascalCase forms directly for unit-test clarity.
        var normalized = toolName.ToLowerInvariant();
        return normalized switch
        {
            "move" or "moveto" => PredictMove(current, args),
            "status" or "getstatus" => PredictStatus(current),
            "mine" or "mineblock" => PredictMine(current, args),
            "craft" or "craftitem" => PredictCraft(current, args),
            "place" or "placeblock" => PredictPlace(current, args),
            "smelt" or "smeltitem" => PredictSmelt(current, args),
            "wander" => PredictWander(current, args),
            "chat" => PredictNoChange(current, toolName, args),
            "findflatarea" => PredictNoChange(current, toolName, args),
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

    /// <summary>
    /// Sprint 60 (TSK-0348): Apply structured ActionOutcome effects to the belief state.
    /// Updates inventory based on ItemCollected, ItemConsumed, ItemCrafted effects.
    /// This keeps the belief state synchronized with actual action results even when
    /// no follow-up world event arrives (common for fire-and-forget dispatch patterns).
    /// </summary>
    public void ApplyOutcome(ActionOutcome outcome)
    {
        lock (_lock)
        {
            var newInv = new Dictionary<string, int>(_belief.Inventory);

            foreach (var effect in outcome.Effects)
            {
                if (effect.Item is null) continue;

                switch (effect.Type)
                {
                    case "ItemCollected":
                    case "ItemCrafted":
                        newInv[effect.Item] = newInv.GetValueOrDefault(effect.Item) + (effect.Count ?? 1);
                        break;

                    case "ItemConsumed":
                    {
                        var have = newInv.GetValueOrDefault(effect.Item);
                        var after = Math.Max(0, have - (effect.Count ?? 1));
                        if (after == 0)
                            newInv.Remove(effect.Item);
                        else
                            newInv[effect.Item] = after;
                        break;
                    }
                }
            }

            _belief = new BeliefState(
                _belief.Health,
                _belief.Food,
                _belief.Position,
                newInv,
                _belief.ActiveBeliefs,
                DateTimeOffset.UtcNow);
        }
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

    /// <summary>
    /// Sprint 60 (TSK-0348): PredictPlace now deducts the placed block from inventory.
    /// Accepts either 'material' or 'block' arg key (PlaceBlockTool schema accepts both).
    /// Previous behavior returned inventory unchanged, which caused WorldStateDiff to
    /// report no expected inventory change for PlaceBlock actions.
    /// </summary>
    private static PredictionState PredictPlace(BeliefState b, IReadOnlyDictionary<string, object?> args)
    {
        var material = GetStrArg(args, "material") ?? GetStrArg(args, "block", "unknown");
        var count = GetIntArg(args, "count", 1);
        var newInv = new Dictionary<string, int>(b.Inventory);

        var have = newInv.GetValueOrDefault(material);
        if (have > 0)
        {
            var after = have - count;
            if (after <= 0)
                newInv.Remove(material);
            else
                newInv[material] = after;
        }

        return new PredictionState("place", args, b.Position, b.Health, b.Food, newInv,
            0.85, $"Place {count}x {material} — deduct {count} from inventory");
    }

    /// <summary>
    /// Sprint 60 (TSK-0348): PredictSmelt now predicts the output item and deducts
    /// the input from inventory. Uses a minimal smeltable-item lookup instead of
    /// duplicating the full SmeltableMapping from Agent.Planning (which Agent.Core
    /// cannot reference). For unknown inputs, returns current inventory unchanged
    /// with reduced confidence to signal uncertainty.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> _smeltInputToOutput =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["iron_ore"]        = "iron_ingot",
            ["raw_iron"]        = "iron_ingot",
            ["gold_ore"]        = "gold_ingot",
            ["raw_gold"]        = "gold_ingot",
            ["copper_ore"]      = "copper_ingot",
            ["raw_copper"]      = "copper_ingot",
            ["ancient_debris"]  = "netherite_scrap",
            ["sand"]            = "glass",
            ["cobblestone"]     = "stone",
            ["stone"]           = "smooth_stone",
            ["clay"]            = "brick",
            ["netherrack"]      = "nether_brick",
            ["cactus"]          = "cactus_green",
            ["oak_log"]         = "charcoal",
            ["spruce_log"]      = "charcoal",
            ["birch_log"]       = "charcoal",
            ["jungle_log"]      = "charcoal",
            ["acacia_log"]      = "charcoal",
            ["dark_oak_log"]    = "charcoal",
        };

    private static PredictionState PredictSmelt(BeliefState b, IReadOnlyDictionary<string, object?> args)
    {
        var input = GetStrArg(args, "item", "unknown");
        var count = GetIntArg(args, "count", 1);
        var newInv = new Dictionary<string, int>(b.Inventory);

        if (_smeltInputToOutput.TryGetValue(input, out var output))
        {
            // Deduct input
            var have = newInv.GetValueOrDefault(input);
            if (have > 0)
            {
                var after = have - count;
                if (after <= 0)
                    newInv.Remove(input);
                else
                    newInv[input] = after;
            }
            // Add output
            newInv[output] = newInv.GetValueOrDefault(output) + count;

            return new PredictionState("smelt", args, b.Position, b.Health, b.Food, newInv,
                0.80, $"Smelt {count}x {input} → +{count} {output}");
        }

        return new PredictionState("smelt", args, b.Position, b.Health, b.Food, b.Inventory,
            0.50, $"Smelt {input} — unknown output, low confidence");
    }

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
