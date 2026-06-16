namespace Agent.Core;

using System.Text.Json;

/// <summary>
/// Pure, stateless projector that applies a <see cref="WorldEvent"/> to a
/// <see cref="WorldState"/> and returns the updated state.
///
/// Each call is a pure function: no I/O, no logging, no mutable shared state.
///
/// Callers are responsible for routing <c>error</c> and <c>blockNotFound</c>
/// events to a typed error channel. This projector updates only the canonical
/// state fields (position, health, inventory) and stores raw event facts for
/// debugging; it never writes <c>game.lastError</c>.
///
/// Testable without any hosted-service infrastructure.
/// </summary>
public sealed class WorldStateProjector
{
    /// <summary>
    /// Applies <paramref name="ev"/> to <paramref name="current"/> and returns
    /// the updated state. <paramref name="current"/> is never mutated.
    /// </summary>
    public WorldState Apply(WorldState current, WorldEvent ev)
    {
        // Store all payload fields as raw event facts for inspection / debugging.
        var next = current.With(b =>
        {
            foreach (var kv in ev.Payload)
                b.SetFact($"event:{ev.EventType}:{kv.Key}", kv.Value);
        });

        // Structured canonical state updates per event type.
        return ev.EventType switch
        {
            "health" =>
                ApplyHealthAndFood(next, ev.Payload),

            "spawn" =>
                ApplyHealthAndFood(ApplyPosition(next, ev.Payload), ev.Payload),

            "move" or "moveComplete" =>
                ApplyPosition(next, ev.Payload),

            "status" =>
                ApplyInventorySnapshot(
                    ApplyHealthAndFood(
                        ApplyPosition(next, ev.Payload), ev.Payload),
                    ev.Payload),

            "blockMined" =>
                ApplyBlockMined(next, ev.Payload),

            // "error", "blockNotFound", and unknown event types:
            // raw facts are already stored above; no structured state change here.
            // Callers (AgentBackgroundService) route these to a typed error channel.
            _ => next,
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static WorldState ApplyPosition(
        WorldState state, IReadOnlyDictionary<string, object?> payload)
    {
        if (payload.TryGetValue("x", out var ox) && ox is int px &&
            payload.TryGetValue("y", out var oy) && oy is int py &&
            payload.TryGetValue("z", out var oz) && oz is int pz)
        {
            state = state with { Position = new Position(px, py, pz) };
        }
        return state;
    }

    private static WorldState ApplyHealthAndFood(
        WorldState state, IReadOnlyDictionary<string, object?> payload)
    {
        if (payload.TryGetValue("hp",   out var ohp) && ohp is int hp)
            state = state with { Health = hp };
        if (payload.TryGetValue("food", out var of)  && of  is int food)
            state = state with { Food = food };
        return state;
    }

    private static WorldState ApplyBlockMined(
        WorldState state, IReadOnlyDictionary<string, object?> payload)
    {
        if (payload.TryGetValue("block", out var rawBlock) && rawBlock is string blockName)
        {
            var itemKey = blockName.Contains(':') ? blockName.Split(':')[1] : blockName;
            state = state.With(b => b.AddInventoryItem(itemKey, 1));
        }
        return state;
    }

    /// <summary>
    /// Parses the "inventory" JSON string from a status-event payload
    /// and replaces <see cref="WorldState.Inventory"/> with a full snapshot.
    /// Malformed JSON is silently ignored (the rest of the state is still applied).
    /// </summary>
    private static WorldState ApplyInventorySnapshot(
        WorldState state, IReadOnlyDictionary<string, object?> payload)
    {
        if (!payload.TryGetValue("inventory", out var invRaw) || invRaw is not string invJson)
            return state;
        try
        {
            using var doc = JsonDocument.Parse(invJson);
            var snap = new Dictionary<string, int>();
            foreach (var prop in doc.RootElement.EnumerateObject())
                if (prop.Value.TryGetInt32(out var qty) && qty > 0)
                    snap[prop.Name] = qty;
            return state.With(b => b.SetInventory(snap));
        }
        catch
        {
            return state; // ignore malformed inventory JSON
        }
    }
}
