namespace Agent.Planning;

using Agent.Construction;
using Agent.Core;
using System.Text.Json;

/// <summary>
/// Decomposes a compound HTN task into a sequence of atomic <see cref="ActionData"/> items
/// given optional string parameters and the current <see cref="WorldState"/>.
/// </summary>
public delegate IReadOnlyList<ActionData> TaskDecomposer(
    string[] parameters, WorldState state);

/// <summary>
/// Registry of named HTN task decompositions.
///
/// Sprint 9 A3: DecomposeBuild reads auto-origin from WorldState.Facts (via BuildFactKeys).
/// Sprint 10 D3: BuildCraftingChain uses GroupBy to merge duplicate blueprint materials.
/// Sprint 10 B4: Expanded CraftingChainOrder with more tools and plank variants.
/// Sprint 10 B2: DecomposeBuild reads checkpoint and resumes from last placed block.
/// Sprint 11 B1-v2: DecomposeBuild accepts a requireOrigin flag.
/// Sprint 13 D2: TryGetIntFact now handles JsonElement values (from JSON deserialization).
/// Sprint 13: Added DecomposeCraftItem for CraftItemGoal decomposition.
/// Sprint 14 P0: DecomposeCraftItem pre-gathers iron ingots (iron tools) and cobblestone (stone tools).
/// Sprint 14 P1a: DirectMineBlocks now delegates to CommonMinecraftBlocks.DirectMineBlocks.
/// Sprint 16 D3-S15: Extracted crafting-table bootstrap into AddCraftingTableIfNeeded helper.
/// Sprint 36 P0-C: DecomposeBuild retry gated on SearchedRadius &lt; FlatAreaRetryRadius (48).
/// Sprint 38 P0-A: GetStatus removed from GatherItemDecompose.
/// </summary>
public sealed class HtnTaskLibrary
{
    // ── Crafting constants ────────────────────────────────────────────────────

    /// <summary>Vanilla: 1 stick + 1 coal -> 4 torches.</summary>
    private const int TorchesPerCraft = 4;

    /// <summary>Vanilla: 1 log -> 4 planks.</summary>
    private const int PlanksPerLog = 4;

    /// <summary>
    /// Maps plank item IDs to their source log item IDs for raw-material prerequisite
    /// gathering before crafting. Used by <see cref="EmitCraftIfNeeded"/> to ensure
    /// logs are mined before planks are crafted in the build crafting chain.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> PlankToLogMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["oak_planks"]       = "oak_log",
        ["birch_planks"]     = "birch_log",
        ["spruce_planks"]    = "spruce_log",
        ["dark_oak_planks"]  = "dark_oak_log",
        ["jungle_planks"]    = "jungle_log",
        ["acacia_planks"]    = "acacia_log",
        ["mangrove_planks"]  = "mangrove_log",
        ["cherry_planks"]    = "cherry_log",
    };

    /// <summary>Radius passed to FindFlatArea when DecomposeBuild has no origin set.</summary>
    private const int PreflightFlatAreaRadius = 30;

    /// <summary>Minimum qualifying flat area (cells) passed to FindFlatArea during preflight.</summary>
    private const int PreflightFlatAreaMin = 25;

    /// <summary>
    /// Sprint 36 P0-C: Retry radius for FindFlatArea when the initial scan at
    /// <see cref="PreflightFlatAreaRadius"/> returned area=0. Matches the JS
    /// FLAT_AREA_RETRY_RADIUS = 48 constant in MineflayerAdapter/index.js.
    /// Used as the gate condition — if the last search was at this radius or
    /// larger and found nothing, no further retry is emitted (prevents infinite loops).
    /// </summary>
    private const int FlatAreaRetryRadius = 48;

    private static readonly ItemSpec OakLogSpec = new()
    {
        ItemId          = "oak_log",
        DisplayName     = "Oak Log",
        SourceBlocks    = ["oak_log", "birch_log", "spruce_log",
                           "dark_oak_log", "jungle_log", "acacia_log", "cherry_log"],
        RequiresSmelting = false,
        MinHarvestLevel  = 0,
    };

    /// <summary>
    /// Blocks the bot can mine directly (no crafting required).
    /// Delegates to <see cref="CommonMinecraftBlocks.DirectMineBlocks"/> — single source of truth.
    /// Sprint 14 P1a: was a private copy; now shared with GoalFactory.
    /// </summary>
    private static HashSet<string> DirectMineBlocks => CommonMinecraftBlocks.DirectMineBlocks;

    // Sprint 10 B4: expanded crafting chain.
    private static readonly IReadOnlyList<string> CraftingChainOrder =
    [
        "oak_planks", "birch_planks", "spruce_planks", "dark_oak_planks",
        "jungle_planks", "acacia_planks", "mangrove_planks", "cherry_planks",
        "crafting_table", "stick",
        "oak_slab", "oak_stairs", "oak_door", "oak_fence", "oak_fence_gate", "chest",
        "wooden_pickaxe", "wooden_axe", "wooden_shovel",
        "stone_pickaxe", "stone_axe", "stone_shovel", "stone_sword",
    ];

    // Items in CraftingChainOrder that require a crafting table.
    private static readonly HashSet<string> RequiresCraftingTable = new(StringComparer.OrdinalIgnoreCase)
    {
        "oak_slab", "oak_stairs", "oak_door", "oak_fence", "oak_fence_gate",
        "chest", "wooden_pickaxe", "wooden_axe", "wooden_shovel",
        "stone_pickaxe", "stone_axe", "stone_shovel", "stone_sword",
        // Iron tools also require a crafting table
        "iron_pickaxe", "iron_axe", "iron_shovel", "iron_sword", "iron_hoe",
        "iron_helmet", "iron_chestplate", "iron_leggings", "iron_boots",
    };

    /// <summary>
    /// Iron ingots required to craft each iron tool or armour piece.
    /// Sprint 14 P0: used by DecomposeCraftItem to pre-gather iron before crafting.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> IronIngotRequirements =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["iron_pickaxe"]    = 3,
        ["iron_axe"]        = 3,
        ["iron_shovel"]     = 1,
        ["iron_sword"]      = 2,
        ["iron_hoe"]        = 2,
        ["iron_helmet"]     = 5,
        ["iron_chestplate"] = 8,
        ["iron_leggings"]   = 7,
        ["iron_boots"]      = 4,
    };

    /// <summary>
    /// Cobblestone required to craft each stone tool.
    /// Sprint 14 P0: used by DecomposeCraftItem to pre-gather cobblestone before crafting.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> CobblestoneRequirements =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["stone_pickaxe"] = 3,
        ["stone_axe"]     = 3,
        ["stone_shovel"]  = 1,
        ["stone_sword"]   = 2,
        ["stone_hoe"]     = 2,
    };

    private readonly Dictionary<string, TaskDecomposer> _methods;

    public HtnTaskLibrary()
    {
        _methods = new Dictionary<string, TaskDecomposer>(StringComparer.OrdinalIgnoreCase)
        {
            ["GatherWood"]      = GatherWoodDecompose,
            ["FindTree"]        = FindTreeDecompose,
            ["MineWood"]        = MineWoodDecompose,
            ["Collect"]         = CollectDecompose,
            ["SurviveNight"]    = SurviveNightDecompose,
            ["FindShelter"]     = FindShelterDecompose,
            ["LightArea"]       = LightAreaDecompose,
            ["WaitForSunrise"]  = WaitDecompose,
            ["Wander"]          = WanderDecompose,
            ["Explore"]         = ExploreDecompose,
            ["FindFlatArea"]    = FindFlatAreaDecompose,
        };
    }

    public bool HasTask(string taskName) => _methods.ContainsKey(taskName);

    public IReadOnlyList<ActionData> Decompose(
        string taskName, string[] parameters, WorldState state)
    {
        if (!_methods.TryGetValue(taskName, out var decompose))
            throw new InvalidOperationException(
                $"No decomposition registered for task '{taskName}'. " +
                "Add a method to HtnTaskLibrary or use the LLM fallback.");
        return decompose(parameters, state);
    }

    public IReadOnlyList<ActionData> DecomposeGatherItem(
        ItemSpec spec, string[] parameters, WorldState state) =>
        GatherItemDecompose(spec, parameters, state);

    /// <summary>
    /// Decomposes a <see cref="Goals.CraftItemGoal"/> into a sequence of prerequisite
    /// and crafting actions.
    ///
    /// Sprint 13: first implementation — ensures a crafting table is present for
    /// table-requiring recipes.
    ///
    /// Sprint 14 P0: pre-gathers materials before attempting the craft.
    /// - Iron tools: mines iron_ore and smelts to iron_ingots if inventory is short.
    /// - Stone tools: mines cobblestone if inventory is short.
    /// - Crafting table: mines oak_log and crafts planks/table if none in inventory.
    ///
    /// Sprint 16 D3-S15: crafting-table bootstrap extracted to
    /// <see cref="AddCraftingTableIfNeeded"/> so it is visually distinct from
    /// material pre-gather (iron_ore, cobblestone). The MineBlock(oak_log) emitted
    /// by that helper is for the TABLE prerequisite, not for the item being crafted.
    /// </summary>
    public IReadOnlyList<ActionData> DecomposeCraftItem(
        string itemId, int count, WorldState state)
    {
        var actions = new List<ActionData>();

        // ── Pre-gather iron ingots for iron tools ─────────────────────────────
        if (IronIngotRequirements.TryGetValue(itemId, out var ingotCount))
        {
            var haveIngots = state.Inventory.GetValueOrDefault("iron_ingot");
            var needIngots = (ingotCount * count) - haveIngots; // TSK-0112: scale by count
            if (needIngots > 0)
            {
                var haveOre  = state.Inventory.GetValueOrDefault("iron_ore")
                             + state.Inventory.GetValueOrDefault("deepslate_iron_ore");
                var needOre  = Math.Max(0, needIngots - haveOre);
                if (needOre > 0)
                {
                    actions.Add(MakeAction("Wander",
                        ("radius", (object?)30), ("maxDistanceFromSpawn", (object?)150)));
                    actions.Add(MakeAction("MineBlock",
                        ("block", "iron_ore"), ("count", (object?)needOre)));
                }

                // Sprint 15 P0: ensure coal is available before smelting.
                // 1 coal smelts up to 8 items; use ceiling(needIngots/8) coal.
                var coalNeeded = Math.Max(1, (needIngots + 7) / 8);
                var haveCoal   = state.Inventory.GetValueOrDefault("coal");
                if (haveCoal < coalNeeded)
                {
                    var coalToMine = coalNeeded - haveCoal;
                    actions.Add(MakeAction("MineBlock",
                        ("block", "coal_ore"), ("count", (object?)coalToMine)));
                }

                actions.Add(MakeAction("SmeltItem",
                    ("item", "iron_ore"), ("count", (object?)needIngots), ("fuel", "coal")));
            }
        }

        // ── Pre-gather cobblestone for stone tools ────────────────────────────
        if (CobblestoneRequirements.TryGetValue(itemId, out var cobbleCount))
        {
            var haveCobble = state.Inventory.GetValueOrDefault("cobblestone");
            var needCobble = (cobbleCount * count) - haveCobble; // TSK-0112: scale by count
            if (needCobble > 0)
            {
                actions.Add(MakeAction("MineBlock",
                    ("block", "stone"), ("count", (object?)needCobble)));
            }
        }

        // ── Pre-gather logs for plank items ───────────────────────────────────
        // TSK-0020: when the target item is planks, ensure enough logs are available.
        // 1 log = 4 planks; mine logs first if inventory is short.
        if (PlankToLogMap.TryGetValue(itemId, out var logType))
        {
            var logsNeeded = (count + PlanksPerLog - 1) / PlanksPerLog;
            var haveLogs = state.Inventory.GetValueOrDefault(logType);
            var logsToMine = logsNeeded - haveLogs;
            if (logsToMine > 0)
            {
                actions.Add(MakeAction("MineBlock",
                    ("block", logType), ("count", (object?)logsToMine)));
            }
        }

        // ── Ensure crafting table is available (Sprint 16: extracted helper) ──
        // Note: any MineBlock(oak_log) emitted here is for the TABLE PREREQUISITE,
        // not for the item being crafted. See AddCraftingTableIfNeeded for rationale.
        AddCraftingTableIfNeeded(itemId, state, actions);

        // ── The craft ─────────────────────────────────────────────────────────
        // Materials should now be in inventory; CraftItemTool returns failure if not.
        actions.Add(MakeAction("CraftItem", ("item", itemId), ("count", (object?)count)));
        actions.Add(MakeAction("GetStatus"));
        return actions;
    }

    /// <summary>
    /// Sprint 44 (TSK-0079): Decomposes a <see cref="Goals.SmeltGoal"/> into a sequence
    /// of prerequisite and smelting actions.
    ///
    /// Previously, smelt intents were routed through <c>DecomposeCraftItem</c>, which
    /// emitted <c>CraftItem</c> actions — never exercising the adapter's dedicated
    /// <c>case 'smelt':</c> handler. This method emits <c>SmeltItem</c> actions.
    ///
    /// Pre-gathers fuel (coal) and ensures the input item is available before smelting.
    /// The adapter's smelt handler handles furnace interaction and item output.
    /// </summary>
    public IReadOnlyList<ActionData> DecomposeSmeltItem(
        string inputItem, int count, WorldState state)
    {
        var actions = new List<ActionData>();

        // Sprint 44: pre-gather fuel (coal) — 1 coal smelts up to 8 items.
        var coalNeeded = Math.Max(1, (count + 7) / 8);
        var haveCoal   = state.Inventory.GetValueOrDefault("coal");
        if (haveCoal < coalNeeded)
        {
            var coalToMine = coalNeeded - haveCoal;
            actions.Add(MakeAction("MineBlock",
                ("block", "coal_ore"), ("count", (object?)coalToMine)));
        }

        // Sprint 44: pre-gather the input item if it's a mineable block.
        var inputBlock = inputItem switch
        {
            "iron_ingot"    => "iron_ore",
            "gold_ingot"    => "gold_ore",
            "copper_ingot"  => "copper_ore",
            "netherite_scrap" => "ancient_debris",
            _               => inputItem,
        };

        var haveInput = state.Inventory.GetValueOrDefault(inputBlock);
        var needInput = count - haveInput;
        if (needInput > 0 && IsMineableBlock(inputBlock))
        {
            actions.Add(MakeAction("MineBlock",
                ("block", inputBlock), ("count", (object?)needInput)));
        }

        // Sprint 44: the actual smelt action.
        actions.Add(MakeAction("SmeltItem",
            ("item", inputBlock), ("count", (object?)count), ("fuel", "coal")));
        actions.Add(MakeAction("GetStatus"));
        return actions;
    }

    /// <summary>
    /// Sprint 44 (TSK-0079): Returns true if the given block ID can be mined directly.
    /// </summary>
    private static bool IsMineableBlock(string block)
    {
        if (DirectMineBlocks.Contains(block)) return true;
        return block switch
        {
            "iron_ore" or "deepslate_iron_ore" or
            "gold_ore" or "deepslate_gold_ore" or
            "copper_ore" or "deepslate_copper_ore" or
            "ancient_debris" or "nether_gold_ore" or
            "coal_ore" or "deepslate_coal_ore" => true,
            _ => false,
        };
    }

    /// <summary>
    /// Emits the minimum actions needed to bootstrap a crafting table into the bot's
    /// inventory before crafting <paramref name="itemId"/>.
    ///
    /// A crafting table is needed when <paramref name="itemId"/> is in
    /// <see cref="RequiresCraftingTable"/> and the bot's inventory shows zero tables.
    ///
    /// Steps emitted (each guarded against unnecessary work):
    /// <list type="number">
    ///   <item><c>MineBlock(oak_log, 1)</c>   — only when oak_log == 0 AND oak_planks &lt; 4</item>
    ///   <item><c>CraftItem(oak_planks, 4)</c> — only when oak_planks &lt; 4</item>
    ///   <item><c>CraftItem(crafting_table, 1)</c> — always (pre-condition: crafting_table == 0)</item>
    /// </list>
    ///
    /// <b>This is a separate concern from material pre-gather</b> (iron_ore, cobblestone).
    /// It bootstraps the <em>tool needed for the craft</em>, not the material being crafted.
    ///
    /// Sprint 16 D3-S15: extracted from the body of <see cref="DecomposeCraftItem"/> so that
    /// the MineBlock(oak_log) emitted here is clearly labelled as a table prerequisite
    /// and not mistaken for an iron/stone material pre-gather step.
    /// </summary>
    private static void AddCraftingTableIfNeeded(string itemId, WorldState state, List<ActionData> actions)
    {
        if (!RequiresCraftingTable.Contains(itemId)) return;
        if (state.Inventory.GetValueOrDefault("crafting_table") > 0) return;

        // Need 4 oak_planks for the table; planks need 1 oak_log each.
        if (state.Inventory.GetValueOrDefault("oak_planks") < 4)
        {
            if (state.Inventory.GetValueOrDefault("oak_log") < 1)
                actions.Add(MakeAction("MineBlock", ("block", "oak_log"), ("count", (object?)1)));
            actions.Add(MakeAction("CraftItem", ("item", "oak_planks"), ("count", (object?)4)));
        }
        actions.Add(MakeAction("CraftItem", ("item", "crafting_table"), ("count", (object?)1)));
    }

    /// <summary>
    /// Decomposes a <see cref="Goals.BuildGoal"/> into a phased action plan.
    ///
    /// Sprint 11 B1-v2: accepts <paramref name="requireOrigin"/> — when true and no valid
    /// origin is resolvable, returns a single FindFlatArea action.
    /// Sprint 10 B2: resumes from checkpoint.
    /// Sprint 36 P0-C: retry gated on SearchedRadius &lt; FlatAreaRetryRadius.
    /// </summary>
    public IReadOnlyList<ActionData> DecomposeBuild(
        Blueprint blueprint,
        IReadOnlyList<PlacementBlock> blocks,
        int originX, int originY, int originZ,
        WorldState state,
        bool requireOrigin = false)
    {
        if (originX == 0 && originY == 0 && originZ == 0)
            ResolveAutoOrigin(state, ref originX, ref originY, ref originZ);

        if (requireOrigin && originX == 0 && originY == 0 && originZ == 0)
        {
            var lastAreaStr = state.Facts.TryGetValue(BuildFactKeys.LastFlatArea, out var la) ? la : null;
            var lastArea = lastAreaStr is string las && int.TryParse(las, out var parsed) ? parsed : -1;

            // Sprint 36 P0-C: gate retry on SearchedRadius < FlatAreaRetryRadius.
            // Previously the retry always fired when area=0, even after searching at
            // FlatAreaRetryRadius — creating an infinite retry loop. Now we only retry
            // if the last search used a smaller radius than the maximum retry radius.
            // FlatAreaRetryRadius = 48 matches the JS FLAT_AREA_RETRY_RADIUS constant.
            if (lastArea == 0)
            {
                var searchedRadiusStr = state.Facts.TryGetValue(
                    "event:FlatAreaFound:SearchedRadius", out var sr) ? sr : null;
                var lastSearchedRadius = searchedRadiusStr is string srs
                    && int.TryParse(srs, out var parsedR) ? parsedR : 0;

                if (lastSearchedRadius >= FlatAreaRetryRadius)
                {
                    // Already searched at or beyond max retry radius and still found nothing.
                    // Return empty plan — goal will fail via the consecutive-failures counter.
                    return Array.Empty<ActionData>();
                }
            }

            // Sprint 19: expand search radius if the last scan returned area=0.
            // Sprint 36 P0-C: only reached when SearchedRadius < FlatAreaRetryRadius.
            var searchRadius = lastArea == 0 ? FlatAreaRetryRadius : PreflightFlatAreaRadius;

            return [MakeAction("FindFlatArea",
                ("radius", (object?)searchRadius),
                ("minFlatArea", (object?)PreflightFlatAreaMin))];
        }

        var actions = new List<ActionData>();

        if (state.IsCreativeMode)
        {
            // Creative mode grants the agent the requested materials up front, so skip
            // mining, smelting, and crafting pre-gather actions entirely.
            actions.Add(MakeAction("MoveTo", ("x", (object?)originX), ("y", (object?)originY), ("z", (object?)originZ)));

            var creativeProgressKey     = BuildFactKeys.BuildProgressIndex(blueprint.Name);
            var creativeCheckpointIndex = 0;
            if (TryGetIntFact(state, creativeProgressKey, out var creativeLastPlaced))
                creativeCheckpointIndex = creativeLastPlaced + 1;

            var creativeExecutor     = new BlueprintExecutor();
            var creativeBlockActions = creativeExecutor.Execute(blocks, originX, originY, originZ);

            for (int i = creativeCheckpointIndex; i < creativeBlockActions.Count; i++)
            {
                var placeAction = creativeBlockActions[i];
                placeAction.Context[BuildFactKeys.PlaceBlockProgressBlueprintId] = blueprint.Name;
                placeAction.Context[BuildFactKeys.PlaceBlockProgressBlockIndex]  = i;
                actions.Add(placeAction);
            }

            actions.Add(MakeAction("GetStatus"));
            return actions;
        }
        else
        {
            var materials = blueprint.Materials
                .GroupBy(m => m.Block, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity), StringComparer.OrdinalIgnoreCase);

            foreach (var (block, quantity) in materials)
            {
                if (!DirectMineBlocks.Contains(block)) continue;
                var have   = state.Inventory.GetValueOrDefault(block);
                var needed = quantity - have;
                if (needed <= 0) continue;
                actions.Add(MakeAction("Wander", ("radius", (object?)30), ("maxDistanceFromSpawn", (object?)150)));
                actions.Add(MakeAction("MineBlock", ("block", block), ("count", (object?)needed)));
            }

            var torchEntry = materials.TryGetValue("torch", out var tq) ? (int?)tq : null;
            if (torchEntry is not null)
            {
                var torchNeeded = torchEntry.Value - state.Inventory.GetValueOrDefault("torch");
                if (torchNeeded > 0)
                {
                    var coalNeeded = Math.Max(1, (torchNeeded + TorchesPerCraft - 1) / TorchesPerCraft);
                    var haveCoal   = state.Inventory.GetValueOrDefault("coal");
                    if (haveCoal < coalNeeded)
                    {
                        actions.Add(MakeAction("MineBlock", ("block", "coal_ore"), ("count", (object?)(coalNeeded - haveCoal))));
                    }
                }
            }

            if (materials.TryGetValue("iron_ingot", out var ironNeeded))
            {
                var haveIron = state.Inventory.GetValueOrDefault("iron_ingot");
                var toSmelt  = ironNeeded - haveIron;
                if (toSmelt > 0)
                {
                    actions.Add(MakeAction("MineBlock", ("block", "iron_ore"), ("count", (object?)toSmelt)));
                    actions.Add(MakeAction("SmeltItem", ("item", "iron_ore"), ("count", (object?)toSmelt), ("fuel", "coal")));
                }
            }

            actions.AddRange(BuildCraftingChain(blueprint, materials, state,
                hasTorch: torchEntry is not null, torchNeeded: torchEntry ?? 0));
        }

        actions.Add(MakeAction("MoveTo", ("x", (object?)originX), ("y", (object?)originY), ("z", (object?)originZ)));

        var progressKey     = BuildFactKeys.BuildProgressIndex(blueprint.Name);
        var checkpointIndex = 0;
        if (TryGetIntFact(state, progressKey, out var lastPlaced))
            checkpointIndex = lastPlaced + 1;

        var executor     = new BlueprintExecutor();
        var blockActions = executor.Execute(blocks, originX, originY, originZ);

        for (int i = checkpointIndex; i < blockActions.Count; i++)
        {
            var placeAction = blockActions[i];
            placeAction.Context[BuildFactKeys.PlaceBlockProgressBlueprintId] = blueprint.Name;
            placeAction.Context[BuildFactKeys.PlaceBlockProgressBlockIndex]  = i;
            actions.Add(placeAction);
        }

        actions.Add(MakeAction("GetStatus"));
        return actions;
    }

    // ── Auto-origin resolution ────────────────────────────────────────────────

    private static void ResolveAutoOrigin(WorldState state, ref int x, ref int y, ref int z)
    {
        if (TryGetIntFact(state, BuildFactKeys.AutoOriginX, out var ax)) x = ax;
        if (TryGetIntFact(state, BuildFactKeys.AutoOriginY, out var ay)) y = ay;
        if (TryGetIntFact(state, BuildFactKeys.AutoOriginZ, out var az)) z = az;
    }

    /// <summary>
    /// Reads an integer from <see cref="WorldState.Facts"/>, handling the common
    /// boxed types stored by different code paths.
    ///
    /// Sprint 13 D2: added <see cref="JsonElement"/> branch so checkpoint facts
    /// that arrive via JSON deserialization (e.g. from a saved state payload)
    /// are coerced correctly instead of silently returning false.
    /// </summary>
    private static bool TryGetIntFact(WorldState state, string key, out int result)
    {
        result = 0;
        if (!state.Facts.TryGetValue(key, out var v)) return false;
        return v switch
        {
            int i    => (result = i)      != int.MinValue,
            long l   => (result = (int)l) != int.MinValue,
            double d => (result = (int)d) != int.MinValue,
            string s => int.TryParse(s, out result),
            // Sprint 13 D2: JsonElement values from deserialized state payloads
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.TryGetInt32(out result),
            _        => false,
        };
    }

    // ── Crafting chain helper ─────────────────────────────────────────────────

    private static IReadOnlyList<ActionData> BuildCraftingChain(
        Blueprint blueprint,
        IReadOnlyDictionary<string, int> materials,
        WorldState state,
        bool hasTorch,
        int torchNeeded)
    {
        var actions = new List<ActionData>();

        bool anyTableRequired = materials.Keys.Any(RequiresCraftingTable.Contains);
        if (anyTableRequired
            && !materials.ContainsKey("crafting_table")
            && state.Inventory.GetValueOrDefault("crafting_table") == 0)
        {
            actions.Add(MakeAction("CraftItem", ("item", "crafting_table"), ("count", (object?)1)));
        }

        foreach (var item in CraftingChainOrder)
            EmitCraftIfNeeded(item, materials, state, actions);

        if (hasTorch && torchNeeded > 0)
        {
            var needed = torchNeeded - state.Inventory.GetValueOrDefault("torch");
            if (needed > 0)
            {
                var sticksNeeded = Math.Max(1, (needed + TorchesPerCraft - 1) / TorchesPerCraft);
                var haveSticks   = state.Inventory.GetValueOrDefault("stick");
                if (haveSticks < sticksNeeded)
                    actions.Add(MakeAction("CraftItem", ("item", "stick"), ("count", (object?)(sticksNeeded - haveSticks))));
                actions.Add(MakeAction("CraftItem", ("item", "torch"), ("count", (object?)needed)));
            }
        }

        return actions;
    }

    private static void EmitCraftIfNeeded(
        string item,
        IReadOnlyDictionary<string, int> materials,
        WorldState state,
        List<ActionData> actions)
    {
        if (!materials.TryGetValue(item, out var needed)) return;
        var have    = state.Inventory.GetValueOrDefault(item);
        var toCraft = needed - have;
        if (toCraft <= 0) return;

        // BuildGoal decomposition should stay on the crafting path for crafted
        // prerequisites. It should not emit mining actions for those items;
        // explicit craft goals remain responsible for any raw-material pre-gather.
        actions.Add(MakeAction("CraftItem", ("item", item), ("count", (object?)toCraft)));
    }

    // ── Decomposers ───────────────────────────────────────────────────────────

    private static IReadOnlyList<ActionData> GatherWoodDecompose(
        string[] parameters, WorldState state) =>
        GatherItemDecompose(OakLogSpec, parameters, state);

    /// <summary>
    /// Sprint 19: Wander is now conditional on a recent BlockNotFound for one of this
    /// spec's source blocks. The JS adapter's <c>bot.findBlock({maxDistance:64})</c> already
    /// locates the nearest matching block — wandering BEFORE mining just moves the bot
    /// away from resources it could mine locally. Default plan: SearchMemory → MineBlock
    /// → GetStatus (3 actions). Wander is inserted only when the WorldState indicates
    /// the bot failed to find the target block on a previous cycle.
    ///
    /// TSK-0021 enhancement: progressive wander radius — each consecutive BlockNotFound
    /// for a source block increases the wander radius (40 → 80 → 120) so the bot
    /// searches further on each retry. After 3 failures, all source variants are tried
    /// with an expanded radius search.
    /// </summary>
    private static IReadOnlyList<ActionData> GatherItemDecompose(
        ItemSpec spec, string[] parameters, WorldState state)
    {
        var count = parameters.Length > 0 && int.TryParse(parameters[0], out var c) ? c : 10;
        var actions = new List<ActionData>
        {
        };

        // TSK-0021: progressive wander — check if any source block was recently not found
        // and track the failure count to increase search radius.
        var blockNotFound = false;
        string? missedBlockType = null;
        foreach (var block in spec.SourceBlocks)
        {
            if (state.Facts.TryGetValue($"event:BlockNotFound:Block:{block}", out var _))
            {
                blockNotFound = true;
                missedBlockType = block;
                break;
            }
        }
        // Fallback: check the generic BlockNotFound fact if per-block facts aren't set.
        if (!blockNotFound
            && state.Facts.TryGetValue("event:BlockNotFound:Block", out var lastMissed)
            && lastMissed is string missedStr
            && spec.SourceBlocks.Any(b =>
                string.Equals(b, missedStr, StringComparison.OrdinalIgnoreCase)))
        {
            blockNotFound = true;
            missedBlockType = missedStr;
        }

        if (blockNotFound)
        {
            // Determine failure count from the tracked fact, defaulting to 1.
            var failCount = 1;
            if (missedBlockType is not null
                && state.Facts.TryGetValue($"event:BlockNotFound:Count:{missedBlockType}", out var fc)
                && fc is string fcs && int.TryParse(fcs, out var parsed))
            {
                failCount = parsed;
            }

            // Progressive wander radius: 40 → 80 → 120 based on consecutive failures.
            var wanderRadius = Math.Min(40 + (failCount - 1) * 40, 120);
            var maxDist = Math.Min(100 + (failCount - 1) * 50, 300);
            actions.Add(MakeAction("Wander", ("radius", (object?)wanderRadius), ("maxDistanceFromSpawn", (object?)maxDist)));
        }

        foreach (var block in spec.SourceBlocks)
            actions.Add(MakeAction("MineBlock", ("block", block), ("count", (object?)count)));
        // Sprint 38 P0-A: GetStatus removed — inventory truth now comes from
        // ItemCollectedEvent (playerCollect) which fires after each pickup.
        // ApplyStatus replaces the entire inventory snapshot, wiping additive
        // ApplyItemCollected increments. Stale-flag clearing and MineBlock
        // correlation completion are both handled in the BlockMinedEvent handler
        // (added in Sprint 37). Periodic GetStatus for drift reconciliation is
        // triggered separately by the health-check and status-check paths.
        return actions;
    }

    private static IReadOnlyList<ActionData> FindTreeDecompose(string[] _, WorldState state) =>
    [
        MakeAction("GetStatus"),
    ];

    private static IReadOnlyList<ActionData> MineWoodDecompose(
        string[] parameters, WorldState state)
    {
        var count = parameters.Length > 0 && int.TryParse(parameters[0], out var c) ? c : 10;
        return
        [
            MakeAction("MineBlock", ("block", "minecraft:oak_log"),   ("count", (object?)count)),
            MakeAction("MineBlock", ("block", "minecraft:birch_log"), ("count", (object?)count)),
        ];
    }

    private static IReadOnlyList<ActionData> CollectDecompose(
        string[] _, WorldState __) => [MakeAction("GetStatus")];

    private static IReadOnlyList<ActionData> SurviveNightDecompose(
        string[] _, WorldState state) =>
    [
        MakeAction("GetStatus"),
    ];

    private static IReadOnlyList<ActionData> FindShelterDecompose(
        string[] _, WorldState state) =>
    [
        MakeAction("GetStatus"),
    ];

    private static IReadOnlyList<ActionData> LightAreaDecompose(
        string[] _, WorldState __) => [MakeAction("GetStatus")];

    private static IReadOnlyList<ActionData> WaitDecompose(
        string[] _, WorldState __) => [MakeAction("GetStatus")];

    private static IReadOnlyList<ActionData> WanderDecompose(
        string[] parameters, WorldState state)
    {
        var radius  = parameters.Length > 0 && int.TryParse(parameters[0], out var r) ? r : 20;
        var maxDist = parameters.Length > 1 && int.TryParse(parameters[1], out var m) ? m : 100;
        return
        [
            MakeAction("Wander",    ("radius", (object?)radius), ("maxDistanceFromSpawn", (object?)maxDist)),
            MakeAction("GetStatus"),
        ];
    }

    private static IReadOnlyList<ActionData> ExploreDecompose(
        string[] parameters, WorldState state)
    {
        var maxDist = parameters.Length > 0 && int.TryParse(parameters[0], out var m) ? m : 100;
        return
        [
            MakeAction("Wander",       ("radius", (object?)30), ("maxDistanceFromSpawn", (object?)maxDist)),
            MakeAction("GetStatus"),
            MakeAction("Wander",       ("radius", (object?)30), ("maxDistanceFromSpawn", (object?)maxDist)),
            MakeAction("GetStatus"),
        ];
    }

    private static IReadOnlyList<ActionData> FindFlatAreaDecompose(
        string[] parameters, WorldState state)
    {
        var radius      = parameters.Length > 0 && int.TryParse(parameters[0], out var r) ? r : 20;
        var minFlatArea = parameters.Length > 1 && int.TryParse(parameters[1], out var a) ? a : 25;
        return
        [
            MakeAction("FindFlatArea", ("radius", (object?)radius), ("minFlatArea", (object?)minFlatArea)),
            MakeAction("GetStatus"),
        ];
    }

    // ── Factory helper ────────────────────────────────────────────────────────

    private static ActionData MakeAction(
        string tool, params (string key, object? value)[] args)
    {
        var action = new ActionData { Tool = tool };
        foreach (var (key, value) in args)
            action.Arguments[key] = value;
        return action;
    }
}
