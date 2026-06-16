namespace Agent.Planning;

using Agent.Construction;
using Agent.Core;
using Agent.Planning.Goals;

/// <summary>
/// Default IGoalFactory. Maps goal names to constructor delegates.
///
/// Synchronous goals (GatherWood, SurviveNight) are created via <see cref="Create"/>.
/// Goals that require an async registry lookup (GatherItem:{itemId}, Build:{blueprintId})
/// are created via <see cref="CreateAsync"/>. Callers that need dynamic item or build
/// support should prefer <see cref="CreateAsync"/>, which falls back to
/// <see cref="Create"/> for sync goals.
///
/// Optional constructor parameters:
/// - <see cref="IItemRegistry"/> — enables "GatherItem:{itemId}" dynamic goals.
/// - <see cref="IBlueprintRepository"/> — enables "Build:{blueprintId}" dynamic goals.
/// When either dependency is null, the corresponding prefix returns null from CreateAsync
/// with a diagnostic warning (D3, TSK-0011).
///
/// D1 (TSK-0011): <see cref="RegisteredGoals"/> now exposes the "GatherItem:{itemId}"
/// and "Build:{blueprintId}" dynamic prefixes alongside the static goal names.
/// </summary>
public sealed class GoalFactory : IGoalFactory
{
    private const string GatherItemPrefix = "GatherItem:";
    private const string BuildPrefix      = "Build:";

    private static readonly Dictionary<string, Func<IReadOnlyDictionary<string, object?>?, IGoal>>
        Creators = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GatherWood"]   = p => new GatherWoodGoal(GetInt(p, "count", 10)),
        ["SurviveNight"] = _ => new SurviveNightGoal(),
    };

    private readonly IItemRegistry?        _itemRegistry;
    private readonly IBlueprintRepository? _blueprintRepository;

    /// <param name="itemRegistry">
    /// Optional registry for "GatherItem:{itemId}" goals. Pass null to disable.
    /// </param>
    /// <param name="blueprintRepository">
    /// Optional repository for "Build:{blueprintId}" goals. Pass null to disable.
    /// </param>
    public GoalFactory(
        IItemRegistry?        itemRegistry        = null,
        IBlueprintRepository? blueprintRepository = null)
    {
        _itemRegistry        = itemRegistry;
        _blueprintRepository = blueprintRepository;
    }

    // D1: Expose all registered goal patterns for API discovery.
    // Static goal names are listed first; dynamic prefix patterns follow.
    /// <inheritdoc/>
    public IReadOnlyList<string> RegisteredGoals =>
        [.. Creators.Keys, "GatherItem:{itemId}", "Build:{blueprintId}"];

    /// <inheritdoc/>
    public IGoal? Create(
        string goalName,
        IReadOnlyDictionary<string, object?>? parameters = null) =>
        Creators.TryGetValue(goalName, out var create) ? create(parameters) : null;

    /// <summary>
    /// Async goal creation. Handles dynamic goal prefixes via injected repositories;
    /// delegates static goal names to the synchronous <see cref="Create"/> method.
    ///
    /// Returns null if the goal name is not registered, the required repository is not
    /// injected (with a diagnostic warning), or the repository returns null for the id.
    /// </summary>
    public async Task<IGoal?> CreateAsync(
        string goalName,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        // ── GatherItem:{itemId} ───────────────────────────────────────────────
        if (goalName.StartsWith(GatherItemPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var itemId = goalName[GatherItemPrefix.Length..];
            if (string.IsNullOrWhiteSpace(itemId)) return null;

            // D3: Emit a diagnostic warning when the registry is missing.
            if (_itemRegistry is null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[GoalFactory] WARNING: Cannot create GatherItem goal for '{itemId}': " +
                    "IItemRegistry was not provided to GoalFactory. " +
                    "Inject IItemRegistry to enable dynamic item goals.");
                return null;
            }

            var spec = await _itemRegistry.GetAsync(itemId, ct);
            if (spec is null) return null;

            var count = GetInt(parameters, "count", 10);
            return new GenericGatherGoal(spec, count);
        }

        // ── Build:{blueprintId} ───────────────────────────────────────────────
        if (goalName.StartsWith(BuildPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var blueprintId = goalName[BuildPrefix.Length..];
            if (string.IsNullOrWhiteSpace(blueprintId)) return null;

            // D3: Emit a diagnostic warning when the repository is missing.
            if (_blueprintRepository is null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[GoalFactory] WARNING: Cannot create Build goal for '{blueprintId}': " +
                    "IBlueprintRepository was not provided to GoalFactory. " +
                    "Inject IBlueprintRepository to enable construction goals.");
                return null;
            }

            var blueprint = await _blueprintRepository.GetAsync(blueprintId, ct);
            if (blueprint is null) return null;

            var (_, blocks) = BlueprintParser.Parse(blueprint.RawMarkdown);
            return new BuildGoal(blueprint, blocks);
        }

        // ── Static sync goals ─────────────────────────────────────────────────
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
