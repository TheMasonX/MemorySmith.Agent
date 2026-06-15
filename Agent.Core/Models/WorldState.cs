namespace Agent.Core;

/// <summary>
/// Snapshot of the agent's known world at a point in time.
/// Updated by incoming WorldEvents from the adapter. The planner reads this
/// to evaluate goal completion and choose next actions.
/// </summary>
public record WorldState
{
    public string AgentId { get; init; } = string.Empty;
    public Position Position { get; init; } = new();
    public int Health { get; init; } = 20;
    public int Food { get; init; } = 20;
    public Dictionary<string, int> Inventory { get; init; } = [];
    public Dictionary<string, object?> Facts { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

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
        public Builder SetPosition(Position p) { _state = _state with { Position = p }; return this; }
        public Builder SetFact(string key, object? value)
        {
            var facts = new Dictionary<string, object?>(_state.Facts) { [key] = value };
            _state = _state with { Facts = facts, UpdatedAt = DateTimeOffset.UtcNow };
            return this;
        }
        public WorldState Build() => _state with { UpdatedAt = DateTimeOffset.UtcNow };
    }
}

public record Position(int X = 0, int Y = 64, int Z = 0);
