namespace Agent.Core;

/// <summary>
/// The agent's belief about world state — may include inferences not directly observed.
/// Currently a thin wrapper around the projection of observations. Will deepen as
/// inference capabilities grow (e.g., block persistence, entity tracking).
/// </summary>
public sealed record BeliefState(
    int Health,
    int Food,
    Position Position,
    IReadOnlyDictionary<string, int> Inventory,
    IReadOnlyList<Fact> ActiveBeliefs,
    DateTimeOffset LastUpdated);
