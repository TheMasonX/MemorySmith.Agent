namespace Agent.Planning;

using Agent.Core;
using Agent.Planning.Goals;

/// <summary>
/// Default IGoalFactory. Maps goal names to constructor delegates.
///
/// Synchronous goals (GatherWood, SurviveNight) are created via <see cref="Create"/>.
/// Goals that require an async registry lookup (GatherItem:{itemId}) are created via
/// <see cref="CreateAsync"/>. Callers that need dynamic item support should prefer
/// <see cref="CreateAsync"/>, which falls back to <see cref="Create"/> for sync goals.
///
/// The constructor accepts an optional <see cref="IItemRegistry"/>. When omitted,
/// <see cref="CreateAsync"/> with a "GatherItem:" prefix returns null (registry required).
/// </summary>
public sealed class GoalFactory : IGoalFactory
{
    private const string GatherItemPrefix = "GatherItem:";

    private static readonly Dictionary<string, Func<IReadOnlyDictionary<string, object?>?, IGoal>>
        Creators = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GatherWood"]   = p => new GatherWoodGoal(GetInt(p, "count", 10)),
        ["SurviveNight"] = _ => new SurviveNightGoal(),
    };

    private readonly IItemRegistry? _itemRegistry;

    /// <param name="itemRegistry">
    /// Optional registry used by <see cref="CreateAsync"/> to look up ItemSpecs for
    /// dynamic "GatherItem:{itemId}" goals. Pass null to disable dynamic goal creation.
    /// </param>
    public GoalFactory(IItemRegistry? itemRegistry = null)
    {
        _itemRegistry = itemRegistry;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> RegisteredGoals => [.. Creators.Keys];

    /// <inheritdoc/>
    public IGoal? Create(
        string goalName,
        IReadOnlyDictionary<string, object?>? parameters = null) =>
        Creators.TryGetValue(goalName, out var create) ? create(parameters) : null;

    /// <summary>
    /// Async goal creation. Handles "GatherItem:{itemId}" goals via the injected
    /// <see cref="IItemRegistry"/>; delegates all other goal names to the synchronous
    /// <see cref="Create"/> method.
    ///
    /// Returns null if the goal name is not registered or the registry returns null
    /// for an unknown item.
    /// </summary>
    public async Task<IGoal?> CreateAsync(
        string goalName,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        if (goalName.StartsWith(GatherItemPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var itemId = goalName[GatherItemPrefix.Length..];
            if (string.IsNullOrWhiteSpace(itemId) || _itemRegistry is null)
                return null;

            var spec = await _itemRegistry.GetAsync(itemId, ct);
            if (spec is null) return null;

            var count = GetInt(parameters, "count", 10);
            return new GenericGatherGoal(spec, count);
        }

        // All non-dynamic goals are sync — no allocation overhead for the async path.
        return Create(goalName, parameters);
    }

    private static int GetInt(
        IReadOnlyDictionary<string, object?>? p, string key, int defaultValue) =>
        p?.TryGetValue(key, out var v) == true
            ? v switch
            {
                int i                                         => i,
                long l                                        => (int)l,
                string s when int.TryParse(s, out var parsed) => parsed,
                _                                             => defaultValue,
            }
            : defaultValue;
}
