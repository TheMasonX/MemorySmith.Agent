namespace Agent.Core;

/// <summary>
/// Pure, stateless projector that applies a typed <see cref="WorldEvent"/> to a
/// <see cref="WorldState"/> and returns the updated state.
///
/// Each call is a pure function: no I/O, no logging, no mutable shared state.
///
/// Callers are responsible for routing <c>error</c> and <c>blockNotFound</c>
/// events to a typed error channel. This projector updates only the canonical
/// state fields (position, health, inventory) and stores raw event facts for
/// debugging; it never writes <c>game.lastError</c>.
///
/// Sprint 3a: Now uses pattern matching on typed event subtypes.
/// Sprint 9:  FlatAreaFoundEvent also writes <see cref="BuildFactKeys.LastFlatArea"/>
///            so planners can read the last scan result without accessing per-event keys.
/// Sprint 14 P1b: ApplyStatus normalizes inventory keys by stripping the "minecraft:"
///            namespace prefix so "minecraft:oak_log" and "oak_log" map to the same slot.
/// Sprint 21 P0-A: ApplyStatus clears WorldState.IsInventoryStale so GenericGatherGoal
///            knows inventory is fresh after a GetStatus response.
/// Sprint 23 P0-A: DamageTakenEvent is a synthetic event computed by AgentBackgroundService
///            from consecutive HealthEvent health-delta comparisons; the projector stores its
///            facts under event:DamageTaken: for planner/diagnostics use.
/// Sprint 35 P0-A: ApplyBlockMined no longer updates inventory directly. Inventory truth
///            now comes from ItemCollectedEvent (Mineflayer playerCollect) which provides
///            the actual drop name (diamond, not diamond_ore). Periodic GetStatus reconciles
///            any drift. ApplyItemCollected added for the new event.
/// Sprint 35 P0-B: MineCompleteEvent stored as facts only (lifecycle handled by AgentBackgroundService).
/// Sprint 35 P2-A stubs: ItemCraftedEvent and ItemConsumedEvent stored as facts; full wiring Sprint 36.
/// Sprint 36 P1-B: ApplyItemCrafted added — ItemCraftedEvent now updates inventory.
/// Sprint 38 P4-A: ApplyItemConsumed added — ItemConsumedEvent now updates inventory.
/// TSK-0117: ApplyCraftComplete and ApplySmeltComplete added — CraftCompleteEvent and
/// SmeltCompleteEvent from the Node.js adapter now update inventory post-craft/post-smelt.
/// Sprint 40 P0-B (Fix C): Restored inventory increment for BlockMinedEvent for self-dropping blocks.
///   The Sprint 35 change removed ALL inventory updates from BlockMinedEvent, relying entirely
///   on ItemCollectedEvent (playerCollect) for inventory truth. However, if the item drop entity
///   is not collected (falls through a hole, too far away), playerCollect never fires and the
///   inventory stays at 0 forever. This fix adds ApplyBlockMined which increments inventory
///   for blocks that drop themselves (dirt, sand, logs, cobblestone, etc.) while still relying
///   on ItemCollectedEvent for ores with different drop names (diamond_ore -> diamond).
///   The block-to-item map covers known mappings; unknown blocks fall through to the bare
///   block name as a best-effort default.
/// </summary>
public sealed class WorldStateProjector
{
    /// <summary>
    /// Applies <paramref name="ev"/> to <paramref name="current"/> and returns
    /// the updated state. <paramref name="current"/> is never mutated.
    /// </summary>
    public WorldState Apply(WorldState current, WorldEvent ev) => ev switch
    {
        SpawnEvent e => ApplySpawn(current, e),
        HealthEvent e => ApplyHealth(current, e),
        GameModeChangedEvent e => ApplyGameModeChanged(current, e),
        DamageTakenEvent e => StoreFacts(current, e),  // Sprint 23: store facts only; health already updated via HealthEvent
        MoveEvent e => ApplyMove(current, e),
        // Sprint 40 P0-B: ApplyBlockMined replaces the Sprint 35 bare StoreFacts call.
        // Increments inventory for self-dropping blocks; falls back to ItemCollectedEvent
        // for ores with different drop names (diamond_ore -> diamond).
        BlockMinedEvent e => ApplyBlockMined(current, e),
        ItemCollectedEvent e => ApplyItemCollected(current, e),
        // Sprint 35 P0-B: MineCompleteEvent stored as facts; lifecycle handled by AgentBackgroundService.
        MineCompleteEvent e => StoreFacts(current, e),
        // Sprint 36 P1-B: ItemCraftedEvent now updates inventory (full wiring).
        // Sprint 38 P4-A: ItemConsumedEvent now updates inventory (full wiring).
        ItemCraftedEvent e => ApplyItemCrafted(current, e),
        ItemConsumedEvent e => ApplyItemConsumed(current, e),
        StatusEvent e => ApplyStatus(current, e),
        // TSK-0117: Post-craft/post-smelt inventory reconciliation.
        // CraftCompleteEvent and SmeltCompleteEvent arrive from the Node.js adapter
        // when crafting/smelting finishes. Ingredients are consumed server-side;
        // we add the output to our C#-side inventory to keep it in sync.
        CraftCompleteEvent e => ApplyCraftComplete(current, e),
        SmeltCompleteEvent e => ApplySmeltComplete(current, e),
        // All other events (Chat, Error, BlockNotFound, Death, BlockPlaced,
        // WanderComplete, WanderFailed, Kicked, FlatAreaFound):
        // no structured state change; store raw facts for debugging only.
        _ => StoreFacts(current, ev, SourceFor(ev)),
    };

    // ── Private helpers ───────────────────────────────────────────────────────

    private static WorldState ApplySpawn(WorldState current, SpawnEvent e)
    {
        var result = current with { Position = e.Pos, Health = e.Health, Food = e.Food };
        return StoreFacts(result, e);
    }

    private static WorldState ApplyHealth(WorldState current, HealthEvent e)
    {
        var result = current with { Health = e.Health, Food = e.Food };
        return StoreFacts(result, e);
    }

    private static WorldState ApplyGameModeChanged(WorldState current, GameModeChangedEvent e)
    {
        var result = current.With(b => b.SetGameMode(e.Mode));
        return StoreFacts(result, e);
    }

    private static WorldState ApplyMove(WorldState current, MoveEvent e)
    {
        var result = current with { Position = e.Pos };
        return StoreFacts(result, e);
    }

    // ── Sprint 40 P0-B: Block-to-item drop mapping for self-dropping blocks ───────
    // Minecraft blocks that drop themselves when mined without silk touch.
    // For these blocks, ApplyBlockMined can safely increment inventory for the block name.
    // Extended list covers all common overworld/terrain blocks.

    private static readonly HashSet<string> SelfDroppingBlocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "dirt", "grass_block", "podzol", "mycelium", "coarse_dirt", "rooted_dirt",
        "sand", "red_sand", "suspicious_sand", "suspicious_gravel",
        "gravel", "clay",
        "cobblestone", "mossy_cobblestone",
        "stone",  // drops cobblestone - handled by BlockToItemDrop map below
        "netherrack", "end_stone",
        "snow_block", "ice", "packed_ice", "blue_ice",
        // Logs (all overworld types)
        "oak_log", "birch_log", "spruce_log", "dark_oak_log",
        "jungle_log", "acacia_log", "cherry_log", "mangrove_log",
        // Planks
        "oak_planks", "birch_planks", "spruce_planks", "dark_oak_planks",
        "jungle_planks", "acacia_planks", "cherry_planks", "mangrove_planks",
        // Stone variants
        "granite", "diorite", "andesite", "tuff", "calcite", "dripstone_block",
        "deepslate", "cobbled_deepslate",
        // Other
        "obsidian", "crying_obsidian",
        "sandstone", "red_sandstone",
        "terracotta", "white_terracotta", "bricks",
        "nether_brick", "soul_sand", "soul_soil",
    };

    /// <summary>
    /// Maps block names to their dropped item names when the block does NOT drop itself.
    /// Key = block name, Value = item name that drops.
    /// Only blocks with different drop names need entries here.
    /// </summary>
    private static readonly Dictionary<string, string> BlockToItemDrop = new(StringComparer.OrdinalIgnoreCase)
    {
        // Stone -> cobblestone
        ["stone"] = "cobblestone",
        ["stone_slab"] = "stone_slab",
        // Grass/mycelium -> dirt
        ["grass_block"] = "dirt",
        ["mycelium"] = "dirt",
        // Ores (drop raw materials, not the ore block)
        ["diamond_ore"] = "diamond",
        ["deepslate_diamond_ore"] = "diamond",
        ["coal_ore"] = "coal",
        ["deepslate_coal_ore"] = "coal",
        ["emerald_ore"] = "emerald",
        ["deepslate_emerald_ore"] = "emerald",
        ["redstone_ore"] = "redstone",
        ["deepslate_redstone_ore"] = "redstone",
        ["lapis_ore"] = "lapis_lazuli",
        ["deepslate_lapis_ore"] = "lapis_lazuli",
        ["iron_ore"] = "raw_iron",
        ["deepslate_iron_ore"] = "raw_iron",
        ["copper_ore"] = "raw_copper",
        ["deepslate_copper_ore"] = "raw_copper",
        ["gold_ore"] = "raw_gold",
        ["deepslate_gold_ore"] = "raw_gold",
        ["nether_gold_ore"] = "gold_nugget",
        // Netherite
        ["ancient_debris"] = "netherite_scrap",
        // Glass (silk touch only normally, but handle gracefully)
        ["glass"] = "glass",
    };

    /// <summary>
    /// Sprint 40 P0-B: Restored inventory increment for BlockMinedEvent.
    /// Maps the mined block name to its item drop and increments inventory.
    /// For blocks that drop themselves (dirt, sand, logs), uses the block name directly.
    /// For blocks with different drops (stone->cobblestone, diamond_ore->diamond),
    /// uses the BlockToItemDrop map. Falls back to the block name for unknown blocks.
    /// ItemCollectedEvent (playerCollect) remains the authoritative source and will
    /// add a second increment when the item IS collected — but this is acceptable
    /// because: (a) it's rare to mine a block and not collect it, (b) periodic
    /// GetStatus reconciles any drift, and (c) the alternative (stuck at 0 forever
    /// when drop falls through a hole) is far worse than occasional double-count.
    /// </summary>
    private static WorldState ApplyBlockMined(WorldState current, BlockMinedEvent e)
    {
        var blockName = e.Block.Contains(':') ? e.Block.Split(':', 2)[1] : e.Block;

        // Determine the item drop name for this block
        string itemDrop;
        if (BlockToItemDrop.TryGetValue(blockName, out var mappedDrop))
            itemDrop = mappedDrop;
        else if (SelfDroppingBlocks.Contains(blockName))
            itemDrop = blockName;
        else
            itemDrop = blockName; // best-effort fallback for unknown blocks

        var result = current.With(b => b.AddInventoryItem(itemDrop, e.Count));
        return StoreFacts(result, e, SourceFor(e));
    }

    /// <summary>
    /// Sprint 35 P0-A: Updates inventory with the actual item drop name.
    /// Fired by Mineflayer playerCollect event — provides the true item name
    /// (e.g. "diamond" from mining "diamond_ore", not "diamond_ore").
    /// Guard in index.js ensures only the bot's own collections fire this event.
    /// </summary>
    private static WorldState ApplyItemCollected(WorldState current, ItemCollectedEvent e)
    {
        // Normalize: strip minecraft: prefix if present (defensive; playerCollect typically returns bare names)
        var itemKey = e.Item.Contains(':') ? e.Item.Split(':', 2)[1] : e.Item;
        var result = current.With(b => b.AddInventoryItem(itemKey, e.Count));
        return StoreFacts(result, e);
    }

    /// <summary>
    /// Sprint 36 P1-B: Updates inventory with the crafted item output.
    /// Fired when an ItemCraftedEvent arrives (from craftComplete → ItemCraftedEvent).
    /// Normalizes the item key (strips "minecraft:" prefix) for consistency with
    /// the rest of the inventory system.
    /// ItemConsumedEvent (ingredient deduction) is Sprint 37.
    /// </summary>
    private static WorldState ApplyItemCrafted(WorldState current, ItemCraftedEvent e)
    {
        // Normalize: strip minecraft: prefix if present
        var itemKey = e.Item.Contains(':') ? e.Item.Split(':', 2)[1] : e.Item;
        var result = current.With(b => b.AddInventoryItem(itemKey, e.Count));
        return StoreFacts(result, e);
    }

    /// <summary>
    /// Sprint 38 P4-A: Deducts consumed ingredient quantities from inventory.
    /// Fired when an ItemConsumedEvent arrives (e.g. ingredients used during crafting).
    /// Normalizes the item key (strips "minecraft:" prefix) for consistency with
    /// the rest of the inventory system. Clamps at zero — never goes negative.
    /// ItemConsumedEvent wiring: Sprint 35 P2-A stub → Sprint 38 P4-A full wiring.
    /// </summary>
    private static WorldState ApplyItemConsumed(WorldState current, ItemConsumedEvent e)
    {
        // Normalize: strip minecraft: prefix if present
        var itemKey = e.Item.Contains(':') ? e.Item.Split(':', 2)[1] : e.Item;
        var have    = current.Inventory.GetValueOrDefault(itemKey);
        var after   = Math.Max(0, have - e.Count);

        // Build updated inventory with the deducted count
        var newInv = new Dictionary<string, int>(
            current.Inventory, StringComparer.OrdinalIgnoreCase);
        if (after == 0)
            newInv.Remove(itemKey);
        else
            newInv[itemKey] = after;

        var result = current.With(b => b.SetInventory(newInv));
        return StoreFacts(result, e);
    }

    /// <summary>
    /// TSK-0117: Post-craft inventory reconciliation.
    /// When CraftCompleteEvent arrives from the adapter, the crafted items have
    /// been added server-side. This method syncs our C#-side inventory by adding
    /// the crafted item count. Ingredients were already consumed server-side.
    /// </summary>
    private static WorldState ApplyCraftComplete(WorldState current, CraftCompleteEvent e)
    {
        var itemKey = e.Item.Contains(':') ? e.Item.Split(':', 2)[1] : e.Item;
        var result = current.With(b => b.AddInventoryItem(itemKey, e.Count));
        return StoreFacts(result, e);
    }

    /// <summary>
    /// TSK-0117: Post-smelt inventory reconciliation.
    /// When SmeltCompleteEvent arrives from the adapter, the input has been
    /// consumed and the output added server-side. This method syncs our C#-side
    /// inventory by adding the result item. Input was already consumed server-side.
    /// </summary>
    private static WorldState ApplySmeltComplete(WorldState current, SmeltCompleteEvent e)
    {
        var resultKey = e.Result.Contains(':') ? e.Result.Split(':', 2)[1] : e.Result;
        var result = current.With(b => b.AddInventoryItem(resultKey, e.Count));
        return StoreFacts(result, e);
    }

    private static WorldState ApplyStatus(WorldState current, StatusEvent e)
    {
        var result = current.With(b =>
        {
            b.SetPosition(e.Pos);
            b.SetHealth(e.Health);
            b.SetFood(e.Food);
            b.SetInventory(NormalizeInventory(e.Inventory));
            // Sprint 21 P0-A: a GetStatus response confirms the current server-side inventory.
            // Clearing the stale flag allows GenericGatherGoal.IsComplete to proceed normally.
            b.SetInventoryStale(false);
            // Sprint 37: StatusEvent now carries game mode, so GetStatus responses also
            // update the confirmed game mode. Previously only GameModeChangedEvent (async)
            // set this, which could be missed on startup — causing false creative detection.
            if (!string.IsNullOrWhiteSpace(e.GameMode))
                b.SetGameMode(e.GameMode);
        });
        return StoreFacts(result, e);
    }

    /// <summary>
    /// Strips the Minecraft namespace prefix (e.g. "minecraft:") from inventory item keys
    /// so that "minecraft:oak_log" and "oak_log" map to the same inventory slot.
    ///
    /// Sprint 14 P1b: Mineflayer's status event may return fully-qualified item IDs.
    /// Without normalization, IsComplete checks in GenericGatherGoal and CraftItemGoal
    /// would fail silently because the inventory key never matched the bare item ID.
    /// </summary>
    private static IReadOnlyDictionary<string, int> NormalizeInventory(
        IReadOnlyDictionary<string, int> raw)
    {
        // Fast path: no namespaced keys → return original dictionary unchanged.
        bool needsWork = false;
        foreach (var key in raw.Keys)
        {
            if (key.Contains(':')) { needsWork = true; break; }
        }
        if (!needsWork) return raw;

        var result = new Dictionary<string, int>(raw.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in raw)
        {
            var normalized = key.Contains(':') ? key.Split(':', 2)[1] : key;
            result[normalized] = result.TryGetValue(normalized, out var existing)
                ? existing + value : value;
        }
        return result;
    }

    /// <summary>
    /// Records all named properties of the typed event as raw facts
    /// (e.g. <c>event:Spawn:Health=20</c>). Fact keys use the event type name
    /// (without "Event" suffix) as the namespace.
    /// </summary>
    private static WorldState StoreFacts(WorldState current, WorldEvent ev,
        FactSource source = FactSource.Observed)
    {
        var prefix = $"event:{GetEventKind(ev)}:";
        var result = current;

        switch (ev)
        {
            case SpawnEvent e:
                result = result.With(b =>
                {
                    b.SetFact($"{prefix}Pos", e.Pos.ToString(), source);
                    b.SetFact($"{prefix}Health", e.Health.ToString(), source);
                    b.SetFact($"{prefix}Food", e.Food.ToString(), source);
                });
                break;
            case HealthEvent e:
                result = result.With(b =>
                {
                    b.SetFact($"{prefix}Health", e.Health.ToString(), source);
                    b.SetFact($"{prefix}Food", e.Food.ToString(), source);
                });
                break;
            case GameModeChangedEvent e:
                result = result.With(b => b.SetFact($"{prefix}Mode", e.Mode, source));
                break;
            case DamageTakenEvent e:
                // Sprint 23 P0-A: store damage facts for planner access and diagnostics.
                result = result.With(b =>
                {
                    b.SetFact($"{prefix}PreviousHealth", e.PreviousHealth.ToString(), source);
                    b.SetFact($"{prefix}Health", e.Health.ToString(), source);
                    b.SetFact($"{prefix}Delta", e.Delta.ToString(), source);
                    b.SetFact($"{prefix}Food", e.Food.ToString(), source);
                });
                break;
            case MoveEvent e:
                result = result.With(b => b.SetFact($"{prefix}Pos", e.Pos.ToString(), source));
                break;
            case BlockMinedEvent e:
                // Sprint 35 P0-A: facts only — inventory update removed; ItemCollectedEvent
                // provides the authoritative inventory update with the correct drop name.
                result = result.With(b =>
                {
                    b.SetFact($"{prefix}Block", e.Block, source);
                    b.SetFact($"{prefix}Count", e.Count.ToString(), source);
                    b.SetFact($"{prefix}Pos", e.Pos.ToString(), source);
                });
                break;
            case ItemCollectedEvent e:
                // Sprint 35 P0-A: the actual inventory update is done in ApplyItemCollected above;
                // here we only record the fact for diagnostics.
                result = result.With(b =>
                {
                    b.SetFact($"{prefix}Item", e.Item, source);
                    b.SetFact($"{prefix}Count", e.Count.ToString(), source);
                });
                break;
            case MineCompleteEvent e:
                // Sprint 35 P0-B: final mining summary for diagnostics.
                result = result.With(b =>
                {
                    b.SetFact($"{prefix}Block", e.Block, source);
                    b.SetFact($"{prefix}Mined", e.Mined.ToString(), source);
                    b.SetFact($"{prefix}TargetCount", e.TargetCount.ToString(), source);
                });
                break;
            case ItemCraftedEvent e:
                // Sprint 36 P1-B: inventory update done in ApplyItemCrafted above;
                // here we only record diagnostic facts.
                result = result.With(b =>
                {
                    b.SetFact($"{prefix}Item", e.Item, source);
                    b.SetFact($"{prefix}Count", e.Count.ToString(), source);
                });
                break;
            case ItemConsumedEvent e:
                // Sprint 38 P4-A: inventory deduction done in ApplyItemConsumed above;
                // here we only record diagnostic facts.
                result = result.With(b =>
                {
                    b.SetFact($"{prefix}Item", e.Item, source);
                    b.SetFact($"{prefix}Count", e.Count.ToString(), source);
                });
                break;
            case ChatEvent e:
                result = result.With(b =>
                {
                    b.SetFact($"{prefix}Username", e.Username, source);
                    b.SetFact($"{prefix}Message", e.Message, source);
                    b.SetFact($"{prefix}OnlinePlayers", e.OnlinePlayers.ToString(), source);
                    if (e.PlayerPos is { } pp)
                        b.SetFact($"{prefix}PlayerPos", pp.ToString(), source);
                });
                break;
            case ErrorEvent e:
                result = result.With(b =>
                {
                    b.SetFact($"{prefix}Action", e.Action, source);
                    b.SetFact($"{prefix}Message", e.Message, source);
                });
                break;
            case BlockNotFoundEvent e:
                result = result.With(b =>
                {
                    b.SetFact($"{prefix}Block", e.Block, source);
                    b.SetFact($"{prefix}MinedCount", e.MinedCount.ToString(), source);
                });
                break;
            case CraftCompleteEvent e:
                result = result.With(b =>
                {
                    b.SetFact($"{prefix}Item", e.Item, source);
                    b.SetFact($"{prefix}Count", e.Count.ToString(), source);
                });
                break;
            case SmeltCompleteEvent e:
                result = result.With(b =>
                {
                    b.SetFact($"{prefix}Input", e.Input, source);
                    b.SetFact($"{prefix}Result", e.Result, source);
                    b.SetFact($"{prefix}Count", e.Count.ToString(), source);
                });
                break;
            case DeathEvent e:
                result = result.With(b => b.SetFact($"{prefix}Pos", e.Pos.ToString(), source));
                break;
            case StatusEvent e:
                result = result.With(b =>
                {
                    b.SetFact($"{prefix}Pos", e.Pos.ToString(), source);
                    b.SetFact($"{prefix}Health", e.Health.ToString(), source);
                    b.SetFact($"{prefix}Food", e.Food.ToString(), source);
                });
                break;
            case BlockPlacedEvent e:
                result = result.With(b =>
                {
                    b.SetFact($"{prefix}X", e.X.ToString(), source);
                    b.SetFact($"{prefix}Y", e.Y.ToString(), source);
                    b.SetFact($"{prefix}Z", e.Z.ToString(), source);
                    b.SetFact($"{prefix}Block", e.Block, source);
                });
                break;
            case WanderCompleteEvent e:
                result = result.With(b =>
                {
                    b.SetFact($"{prefix}Pos", e.Pos.ToString(), source);
                    b.SetFact($"{prefix}TargetX", e.TargetX.ToString(), source);
                    b.SetFact($"{prefix}TargetZ", e.TargetZ.ToString(), source);
                });
                break;
            case WanderFailedEvent e:
                result = result.With(b =>
                {
                    b.SetFact($"{prefix}Message", e.Message, source);
                    b.SetFact($"{prefix}Pos", e.Pos.ToString(), source);
                });
                break;
            case KickedEvent e:
                result = result.With(b => b.SetFact($"{prefix}Reason", e.Reason, source));
                break;
            case FlatAreaFoundEvent e:
                result = result.With(b =>
                {
                    b.SetFact($"{prefix}X", e.X.ToString(), source);
                    b.SetFact($"{prefix}Y", e.Y.ToString(), source);
                    b.SetFact($"{prefix}Z", e.Z.ToString(), source);
                    b.SetFact($"{prefix}Area", e.Area.ToString(), source);
                    b.SetFact($"{prefix}MinX", e.MinX.ToString(), source);
                    b.SetFact($"{prefix}MaxX", e.MaxX.ToString(), source);
                    b.SetFact($"{prefix}MinZ", e.MinZ.ToString(), source);
                    b.SetFact($"{prefix}MaxZ", e.MaxZ.ToString(), source);
                    // Sprint 35 P0-C: record searched radius for BuildGoalDecomposer retry logic.
                    b.SetFact($"{prefix}SearchedRadius", e.SearchedRadius.ToString(), source);
                    // Sprint 9: cross-event summary key — lets planners read the last
                    // flat-area scan result via BuildFactKeys without parsing per-event keys.
                    b.SetFact(BuildFactKeys.LastFlatArea, e.Area.ToString(), source);
                });
                break;
            // TSK-0117: store diagnostic facts for craft/smelt completion events
        }

        return result;
    }

    /// <summary>Determines the provenance for facts derived from an event.</summary>
    private static FactSource SourceFor(WorldEvent ev) => ev switch
    {
        ErrorEvent => FactSource.Inferred,
        _ => FactSource.Observed,
    };

    /// <summary>Strips "Event" suffix from the type name for fact key namespaces.</summary>
    private static string GetEventKind(WorldEvent ev) => ev.GetType().Name switch
    {
        var n when n.EndsWith("Event") => n[..^5],
        var n => n,
    };
}
