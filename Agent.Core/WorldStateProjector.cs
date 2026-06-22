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
        // Sprint 35 P0-A: inventory update removed from ApplyBlockMined; inventory
        // truth now comes from ItemCollectedEvent. Only facts are stored here.
        BlockMinedEvent e => StoreFacts(current, e, SourceFor(e)),
        ItemCollectedEvent e => ApplyItemCollected(current, e),
        // Sprint 35 P0-B: MineCompleteEvent stored as facts; lifecycle handled by AgentBackgroundService.
        MineCompleteEvent e => StoreFacts(current, e),
        // Sprint 36 P1-B: ItemCraftedEvent now updates inventory (full wiring).
        // Sprint 38 P4-A: ItemConsumedEvent now updates inventory (full wiring).
        ItemCraftedEvent e => ApplyItemCrafted(current, e),
        ItemConsumedEvent e => ApplyItemConsumed(current, e),
        StatusEvent e => ApplyStatus(current, e),
        // All other events (Chat, Error, BlockNotFound, CraftComplete, SmeltComplete,
        // Death, BlockPlaced, WanderComplete, WanderFailed, Kicked, FlatAreaFound):
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
