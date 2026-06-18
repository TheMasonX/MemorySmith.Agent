namespace Agent.Planning;

using Agent.Construction;
using Agent.Core;
using Agent.Planning.Goals;

/// <summary>
/// Default IGoalFactory. Maps goal names to constructor delegates.
///
/// Synchronous goals (GatherWood, SurviveNight) are created via <see cref="Create"/>.
/// Goals that require an async registry lookup (GatherItem:{itemId}, Build:{blueprintId},
/// CraftItem:{itemId}) are created via <see cref="CreateAsync"/>.
///
/// Sprint 13:
/// - Added <c>CraftItem:{itemId}</c> prefix — returns a <see cref="CraftItemGoal"/>.
/// - Added built-in fallback for direct-mine blocks (dirt, sand, iron_ore, oak_log, etc.)
///   so gather goals work without MemorySmith wiki pages for common items.
///
/// Sprint 14 P1a:
/// - BuiltInDirectMineItems removed; now delegates to <see cref="CommonMinecraftBlocks.DirectMineBlocks"/>
///   to eliminate the manual-sync requirement flagged in Sprint 13 council D1.
/// </summary>
public sealed class GoalFactory : IGoalFactory
{
    private const string GatherItemPrefix = "GatherItem:";
    private const string BuildPrefix      = "Build:";
    private const string CraftItemPrefix  = "CraftItem:";

    private static readonly Dictionary<string, Func<IReadOnlyDictionary<string, object?>?, IGoal>>
        Creators = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GatherWood"]   = p => new GatherWoodGoal(GetInt(p, "count", 10)),
        ["SurviveNight"] = _ => new SurviveNightGoal(),
    };

    private readonly IItemRegistry?        _itemRegistry;
    private readonly IBlueprintRepository? _blueprintRepository;

    public GoalFactory(
        IItemRegistry?        itemRegistry        = null,
        IBlueprintRepository? blueprintRepository = null)
    {
        _itemRegistry        = itemRegistry;
        _blueprintRepository = blueprintRepository;
    }

    public IReadOnlyList<string> RegisteredGoals =>
        [.. Creators.Keys, "GatherItem:{itemId}", "Build:{blueprintId}", "CraftItem:{itemId}"];

    public IGoal? Create(
        string goalName,
        IReadOnlyDictionary<string, object?>? parameters = null) =>
        Creators.TryGetValue(goalName, out var create) ? create(parameters) : null;

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

            // Try the registry first (wiki pages have richer specs: source blocks, smelting flag, etc.)
            ItemSpec? spec = null;
            if (_itemRegistry is not null)
                spec = await _itemRegistry.GetAsync(itemId, ct);

            // Sprint 13 fallback: return a minimal spec for common direct-mine blocks so
            // "gather dirt", "gather sand", "gather iron_ore" etc. work without wiki pages.
            spec ??= TryMakeBuiltInSpec(itemId);

            if (spec is null) return null;
            return new GenericGatherGoal(spec, GetInt(parameters, "count", 10));
        }

        // ── Build:{blueprintId} ───────────────────────────────────────────────
        if (goalName.StartsWith(BuildPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var blueprintId = goalName[BuildPrefix.Length..];
            if (string.IsNullOrWhiteSpace(blueprintId)) return null;

            if (_blueprintRepository is null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[GoalFactory] WARNING: Cannot create Build goal for '{blueprintId}': " +
                    "IBlueprintRepository was not provided to GoalFactory.");
                return null;
            }

            var blueprint = await _blueprintRepository.GetAsync(blueprintId, ct);
            if (blueprint is null) return null;

            var (_, blocks) = BlueprintParser.Parse(blueprint.RawMarkdown);
            return new BuildGoal(blueprint, blocks);
        }

        // ── CraftItem:{itemId} ────────────────────────────────────────────────
        if (goalName.StartsWith(CraftItemPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var itemId = goalName[CraftItemPrefix.Length..];
            if (string.IsNullOrWhiteSpace(itemId)) return null;
            var count = GetInt(parameters, "count", 1);
            return new CraftItemGoal(itemId, count);
        }

        return Create(goalName, parameters);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a minimal <see cref="ItemSpec"/> for common direct-mine blocks that
    /// do not require a MemorySmith wiki page. Returns null for unknown items.
    ///
    /// Sprint 14 P1a: now delegates to <see cref="CommonMinecraftBlocks.DirectMineBlocks"/>
    /// instead of maintaining a separate private set.
    /// </summary>
    /// <summary>
    /// Items whose drop differs from the mined block. When the user asks to gather
    /// the key item, SourceBlocks must include both the block name AND the drop name
    /// so <see cref="GenericGatherGoal.IsComplete"/> counts inventory correctly.
    ///
    /// Sprint 19: fixes "get stone" — mining stone drops cobblestone, so both
    /// "stone" (the block) and "cobblestone" (the drop) count toward completion.
    /// </summary>
    private static readonly Dictionary<string, string[]> YieldSourceBlocks =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["stone"] = ["stone", "cobblestone"],
    };

    private static ItemSpec? TryMakeBuiltInSpec(string itemId)
    {
        var key = itemId.ToLowerInvariant().Replace('-', '_');
        if (!CommonMinecraftBlocks.DirectMineBlocks.Contains(key)) return null;

        // Sprint 19: use yield-aware SourceBlocks when the drop differs from the block
        var sourceBlocks = YieldSourceBlocks.TryGetValue(key, out var yieldBlocks)
            ? yieldBlocks
            : new[] { key };

        return new ItemSpec
        {
            ItemId           = key,
            DisplayName      = System.Globalization.CultureInfo.InvariantCulture
                                   .TextInfo.ToTitleCase(key.Replace('_', ' ')),
            SourceBlocks     = sourceBlocks,
            RequiresSmelting = false,
            MinHarvestLevel  = 0,
        };
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
