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
        MoveEvent e => ApplyMove(current, e),
        BlockMinedEvent e => ApplyBlockMined(current, e),
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

    private static WorldState ApplyMove(WorldState current, MoveEvent e)
    {
        var result = current with { Position = e.Pos };
        return StoreFacts(result, e);
    }

    private static WorldState ApplyBlockMined(WorldState current, BlockMinedEvent e)
    {
        var itemKey = e.Block.Contains(':') ? e.Block.Split(':')[1] : e.Block;
        // Sprint 15 P0: use e.Count (actual blocks mined) instead of hardcoded 1.
        // Mineflayer can report multiple blocks mined in a single event; hardcoding 1
        // caused inventory under-counting and gather goals that never completed.
        var result = current.With(b => b.AddInventoryItem(itemKey, e.Count));
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
            case MoveEvent e:
                result = result.With(b => b.SetFact($"{prefix}Pos", e.Pos.ToString(), source));
                break;
            case BlockMinedEvent e:
                result = result.With(b =>
                {
                    b.SetFact($"{prefix}Block", e.Block, source);
                    b.SetFact($"{prefix}Count", e.Count.ToString(), source);
                    b.SetFact($"{prefix}Pos", e.Pos.ToString(), source);
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
