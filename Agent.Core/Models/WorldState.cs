namespace Agent.Core;

/// <summary>
/// Snapshot of the agent's known world at a point in time.
/// Updated by incoming WorldEvents from the adapter. The planner reads this
/// to evaluate goal completion and choose next actions.
/// </summary>
public record WorldState
{
    /// <summary>Maximum number of structured facts retained before oldest entries are trimmed.</summary>
    public const int MaxFacts = 1000;

    public string AgentId { get; init; } = string.Empty;
    public Position Position { get; init; } = new();
    public int Health { get; init; } = 20;
    public int Food { get; init; } = 20;
    public string? GameMode { get; init; }
    public Dictionary<string, int> Inventory { get; init; } = [];
    /// <summary>Legacy flat fact map. Prefer <see cref="StructuredFacts"/> for new code.</summary>
    public Dictionary<string, object?> Facts { get; init; } = [];
    /// <summary>Time-ordered list of facts with provenance metadata. Oldest first.</summary>
    public IReadOnlyList<Fact> StructuredFacts { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Sprint 21 P0-A: true when inventory was marked potentially stale by
    /// <see cref="WebUI.Blazor.AgentBackgroundService.SetGoal"/>.
    /// Cleared by <see cref="WorldStateProjector"/> when a fresh <c>StatusEvent</c> arrives.
    /// <see cref="Agent.Planning.Goals.GenericGatherGoal.IsComplete"/> returns false while
    /// this flag is set, preventing false-completion after admin <c>/clear</c>.
    /// </summary>
    public bool IsInventoryStale { get; init; } = false;

    public bool IsCreativeMode => MatchesCreativeMode(GameMode)
        || (Facts.TryGetValue("world:gamemode", out var gm) && MatchesCreativeMode(Convert.ToString(gm)));

    public WorldState With(Action<WorldState.Builder> configure)
    {
        var b = new Builder(this);
        configure(b);
        return b.Build();
    }

    public sealed class Builder(WorldState source)
    {
        private WorldState _state = source;

        public Builder SetHealth(int hp) { _state = _state with { Health = hp }; return this; }
        public Builder SetFood(int food) { _state = _state with { Food = food }; return this; }
        public Builder SetPosition(Position p) { _state = _state with { Position = p }; return this; }
        public Builder SetGameMode(string? mode)
        {
            var facts = new Dictionary<string, object?>(_state.Facts);
            if (!string.IsNullOrWhiteSpace(mode)) facts["world:gamemode"] = mode;
            else facts.Remove("world:gamemode");
            _state = _state with { GameMode = mode, Facts = facts, UpdatedAt = DateTimeOffset.UtcNow };
            return this;
        }

        /// <summary>
        /// Sets a fact in the legacy <see cref="WorldState.Facts"/> dictionary only.
        /// For provenance-tracked facts use <see cref="SetFact(string,string,FactSource)"/>.
        ///
        /// Use <see cref="SetFact(string,string,FactSource)"/> or assign to
        /// <see cref="WorldState.Facts"/> directly for new code. The legacy write-path
        /// is not used in production; this method exists only for test convenience and
        /// backward compatibility.
        /// </summary>
        [Obsolete("Use SetFact(string, string, FactSource) or WorldState.Facts directly. " +
                  "This write-path is not used in production and exists only for backward " +
                  "compatibility. See WorldState.StructuredFacts for provenance-tracked facts.")]
        public Builder SetFact(string key, object? value)
        {
            var facts = new Dictionary<string, object?>(_state.Facts) { [key] = value };
            var gameMode = key.Equals("world:gamemode", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToString(value)
                : _state.GameMode;
            _state = _state with { Facts = facts, GameMode = gameMode, UpdatedAt = DateTimeOffset.UtcNow };
            return this;
        }

        /// <summary>
        /// Sets a fact with provenance metadata. Updates both <see cref="WorldState.Facts"/>
        /// (as a string value) and <see cref="WorldState.StructuredFacts"/>.
        /// Trims oldest entries when the count exceeds <see cref="WorldState.MaxFacts"/>.
        /// </summary>
        public Builder SetFact(string key, string value, FactSource source)
        {
            var facts = new Dictionary<string, object?>(_state.Facts) { [key] = value };
            var structured = new List<Fact>(_state.StructuredFacts)
            {
                new Fact(key, value, source, DateTimeOffset.UtcNow),
            };
            while (structured.Count > MaxFacts)
                structured.RemoveAt(0);
            var gameMode = key.Equals("world:gamemode", StringComparison.OrdinalIgnoreCase)
                ? value
                : _state.GameMode;
            _state = _state with { Facts = facts, StructuredFacts = structured, GameMode = gameMode, UpdatedAt = DateTimeOffset.UtcNow };
            return this;
        }

        /// <summary>
        /// Replaces the entire inventory with the supplied snapshot.
        /// Used when a full status event arrives from the world adapter.
        /// </summary>
        public Builder SetInventory(IReadOnlyDictionary<string, int> snapshot)
        {
            _state = _state with { Inventory = new Dictionary<string, int>(snapshot) };
            return this;
        }

        /// <summary>
        /// Increments (or decrements) a single inventory item by <paramref name="delta"/>.
        /// Removes the entry when the count drops to zero or below.
        /// </summary>
        public Builder AddInventoryItem(string item, int delta = 1)
        {
            var inv = new Dictionary<string, int>(_state.Inventory);
            inv.TryGetValue(item, out var cur);
            var next = cur + delta;
            if (next <= 0) inv.Remove(item);
            else           inv[item] = next;
            _state = _state with { Inventory = inv };
            return this;
        }

        /// <summary>
        /// Sprint 21 P0-A: marks the inventory as potentially stale (true) or confirmed
        /// fresh (false). Set to true by AgentBackgroundService.SetGoal; cleared by
        /// WorldStateProjector when a StatusEvent arrives from GetStatus.
        /// </summary>
        public Builder SetInventoryStale(bool stale)
        {
            _state = _state with { IsInventoryStale = stale };
            return this;
        }

        public WorldState Build() => _state with { UpdatedAt = DateTimeOffset.UtcNow };
    }

    private static bool MatchesCreativeMode(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (string.Equals(value.Trim(), "creative", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(value.Trim(), "creative mode", StringComparison.OrdinalIgnoreCase));
}

public record Position(int X = 0, int Y = 64, int Z = 0);
