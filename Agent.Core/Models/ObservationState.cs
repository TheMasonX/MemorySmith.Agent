namespace Agent.Core;

/// <summary>
/// State directly observed from world events — ground truth at the moment of observation.
/// Updated by WorldStateProjector when events arrive.
/// </summary>
public sealed record ObservationState(
    int Health,
    int Food,
    Position Position,
    IReadOnlyDictionary<string, int> Inventory,
    IReadOnlyList<Fact> RecentObservations,
    DateTimeOffset LastUpdated);
