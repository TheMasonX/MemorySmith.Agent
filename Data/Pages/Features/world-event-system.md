# World Event System

**Feature ID:** F-EVENTS  
**Status:** Core (Stable)  
**Location:** `Agent.Core/Events/WorldEvents.cs`, `Agent.Core/WorldStateProjector.cs`

The world event system is the bridge between the Node.js Mineflayer adapter and the C# agent core. Every action the bot takes in Minecraft produces events that flow through a projector to update the WorldState.

## Architecture

```
Node.js (Mineflayer)
  ↓ WebSocket: {"event":"blockMined", "block":"stone", ...}
WebSocketBridge.ParseEvent
  ↓ WorldEvent record
WorldStateProjector.Apply(current, event)
  ↓ WorldState (immutable record, new instance)
AgentBackgroundService.ProcessEventsAsync
  ↓ Correlation completion + goal evaluation
Agent state updated
```

## 25 Event Types

Events cover the full range of bot actions and world observations:

| Category | Events |
|----------|--------|
| **Lifecycle** | SpawnEvent, DeathEvent, KickedEvent |
| **Status** | StatusEvent, HealthEvent, GameModeChangedEvent |
| **Movement** | MoveEvent, WanderCompleteEvent, WanderFailedEvent |
| **Action Results** | BlockMinedEvent, BlockPlacedEvent, CraftCompleteEvent, SmeltCompleteEvent, MineCompleteEvent, MineAbortedEvent |
| **Inventory** | ItemCollectedEvent, ItemCraftedEvent, ItemConsumedEvent |
| **Chat** | ChatEvent |
| **Errors** | ErrorEvent, BlockNotFoundEvent, WanderFailedEvent |
| **Finders** | FlatAreaFoundEvent, ReachableBlockFoundEvent |
| **Control** | StopCompleteEvent |
| **Synthetic (C#)** | DamageTakenEvent (derived from HealthEvent deltas) |

## WorldState Projections

Each event projects specific state changes:
- **MoveEvent** → Position updated
- **BlockMinedEvent** → Inventory incremented (self-dropping blocks + ore mappings)
- **ItemCollectedEvent** → Authoritative inventory update
- **StatusEvent** → Full state sync + clears inventory stale flag
- **DamageTakenEvent** → Facts only (health already updated by HealthEvent)

## Inventory Staleness

A critical safety mechanism: `IsInventoryStale` is set `true` when a new goal starts, and cleared only when a fresh `StatusEvent` arrives. This prevents false goal completion after admin `/clear` or unobserved inventory changes.

## Related

- [World Event Catalog](../memories/Core/agent-world-event-catalog.json)
- [WorldState Projector](../memories/Core/agent-worldstate-projector.json)
- [WebSocket Bridge](../memories/Core/agent-websocket-bridge.json)
- [World Model Guide](../guides/world-model.md)
