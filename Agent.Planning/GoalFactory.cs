namespace Agent.Planning;

using Agent.Construction;
using Agent.Core;
using Agent.Planning.Goals;
using Microsoft.Extensions.Logging;

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
///
/// Sprint 32 P2-1:
/// - Replaced System.Diagnostics.Debug.WriteLine with structured ILogger warnings
///   so failures are visible in production telemetry (not just debug output).
/// </summary>
public sealed class GoalFactory : IGoalFactory
{
    private const string GatherItemPrefix = "GatherItem:";
    private const string BuildPrefix      = "Build:";
    private const string CraftItemPrefix  = "CraftItem:";
    private const string PlaceBlockPrefix = "PlaceBlock:";

    private static readonly Dictionary<string, Func<IReadOnlyDictionary<string, object?>?, IGoal>>
        Creators = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GatherWood"]   = p => new GatherWoodGoal(GetInt(p, "count", 10)),
        ["SurviveNight"] = _ => new SurviveNightGoal(),
    };

    private readonly IItemRegistry?        _itemRegistry;
    private readonly IBlueprintRepository? _blueprintRepository;
    private readonly ILogger<GoalFactory>  _logger;

    public GoalFactory(
        IItemRegistry?        itemRegistry        = null,
        IBlueprintRepository? blueprintRepository = null,
        ILogger<GoalFactory>? logger              = null)
    {
        _itemRegistry        = itemRegistry;
        _blueprintRepository = blueprintRepository;
        _logger              = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<GoalFactory>.Instance;
    }

    public IReadOnlyList<string> RegisteredGoals =>
        [.. Creators.Keys, "GatherItem:{itemId}", "Build:{blueprintId}", "CraftItem:{itemId}", "SmeltItem:{inputItem}"];

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

            if (spec is null)
            {
                // Sprint 32 P2-1: distinct warning per failure path for caller diagnostics.
                // Failure: item not found in registry and not a built-in direct-mine block.
                _logger.LogWarning(
                    "Cannot create GatherItem goal for '{ItemId}': " +
                    "item not found in registry and not a built-in direct-mine block.",
                    itemId);
                return null;
            }
            return new GenericGatherGoal(spec, GetInt(parameters, "count", 10));
        }

        // ── Build:{blueprintId} ───────────────────────────────────────────────
        if (goalName.StartsWith(BuildPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var blueprintId = goalName[BuildPrefix.Length..];
            if (string.IsNullOrWhiteSpace(blueprintId)) return null;

            if (_blueprintRepository is null)
            {
                // Sprint 32 P2-1: structured warning replaces Debug.WriteLine so this is
                // visible in production logs. Failure: service not provided to GoalFactory.
                _logger.LogWarning(
                    "Cannot create Build goal for '{BlueprintId}': " +
                    "IBlueprintRepository was not provided to GoalFactory (service unavailable).",
                    blueprintId);
                return null;
            }

            var blueprint = await _blueprintRepository.GetAsync(blueprintId, ct);
            if (blueprint is null)
            {
                _logger.LogWarning(
                    "Blueprint '{BlueprintId}' not found in repository — " +
                    "checked gateway and local fallback (Data/Pages/blueprints/{Slug}.md). " +
                    "Available blueprints can be listed via SearchMemory('blueprints/').",
                    blueprintId, blueprintId);
                return null;
            }

            var (_, blocks) = BlueprintParser.Parse(blueprint.RawMarkdown);

            // TSK-0103: extract optional origin coordinates from chat parameters
            // e.g. "build a house at 100 64 200" → BuildOrigin(100, 64, 200, Explicit)
            // When any coordinate is missing, Origin is null (auto-detect).
            var ox = GetInt(parameters, "originX", null);
            var oy = GetInt(parameters, "originY", null);
            var oz = GetInt(parameters, "originZ", null);
            var origin = BuildOrigin.FromNullable(ox, oy, oz, BuildOriginSource.Explicit);

            return new BuildGoal(blueprint, blocks, origin);
        }

        // ── CraftItem:{itemId} ────────────────────────────────────────────────
        if (goalName.StartsWith(CraftItemPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var itemId = goalName[CraftItemPrefix.Length..];
            if (string.IsNullOrWhiteSpace(itemId)) return null;
            var count = GetInt(parameters, "count", 1);
            return new CraftItemGoal(itemId, count);
        }

        // ── SmeltItem:{inputItem} (Sprint 44 TSK-0079) ────────────────────────
        const string SmeltItemPrefix = "SmeltItem:";
        if (goalName.StartsWith(SmeltItemPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var inputItem = goalName[SmeltItemPrefix.Length..];
            if (string.IsNullOrWhiteSpace(inputItem)) return null;
            var count = GetInt(parameters, "count", 1);
            return new Goals.SmeltGoal(inputItem, count);
        }

        // ── PlaceBlock:{item} (Sprint 54) ────────────────────────────────────
        if (goalName.StartsWith(PlaceBlockPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var item = goalName[PlaceBlockPrefix.Length..];
            if (string.IsNullOrWhiteSpace(item)) return null;
            var count = GetInt(parameters, "count", 1);
            var x = GetInt(parameters, "x", (int?)null);
            var y = GetInt(parameters, "y", (int?)null);
            var z = GetInt(parameters, "z", (int?)null);
            return new Goals.PlaceBlockGoal(item, count, x, y, z);
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

    private static int? GetInt(
        IReadOnlyDictionary<string, object?>? p, string key, int? defaultValue) =>
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
