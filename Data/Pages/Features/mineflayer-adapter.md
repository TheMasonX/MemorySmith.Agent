# Mineflayer Adapter

**Feature ID:** F-ADAPTER  
**Status:** Core (Active Development)  
**Location:** `MineflayerAdapter/` (Node.js), `Agent.World.Minecraft/` (C#)

The Mineflayer adapter is the bridge between C# agent logic and the Minecraft game world. It runs as a Node.js subprocess using Mineflayer (Minecraft bot library) and communicates with the C# host via WebSocket.

## Architecture

```
C# Host (WebUI.Blazor)
  → MinecraftAdapter (spawns Node.js, manages lifecycle)
    → WebSocketBridge (JSON message transport)
      → WebSocket (port 3000 default)
        → MineflayerAdapter/index.js (ESM module)
          → Mineflayer bot → Minecraft Server
```

## Command Handlers

| Handler | Action | Description |
|---------|--------|-------------|
| `mineBlock` | mine | Dig blocks with 3-pass scoring (same-Y → nearby → fallback) |
| `moveTo` | move | A* pathfinding via mineflayer-pathfinder |
| `placeBlock` | place | Place blocks in creative or survival mode |
| `craftItem` | craft | Craft items (with crafting table if needed, 2x2+ grid) |
| `smeltItem` | smelt | Smelt items in furnace |
| `wander` | wander | Random exploration within radius |
| `findFlatArea` | findFlatArea | Height-map scan for flat building areas |
| `getStatus` | status | Full state snapshot (pos, health, inventory) |
| `chat` | chat | Send in-game chat message |
| `findReachableBlock` | findReachableBlock | Find blocks reachable via pathfinder |
| `stop` | stop | Emergency stop (bypasses queue) |

## Key Design Decisions

- **Sequential command queue**: Actions enqueued and dispatched one at a time
- **No magic numbers**: All constants named at top of index.js (AGENTS.md Rule)
- **Action correlation**: Each action carries a correlationId echoed in result events
- **Block aliases**: BLOCK_MINING_ALIASES maps alternate block names (dirt ← grass_block)

## Critical Lessons Learned

- `bot.registry.blocksById` does NOT exist in Mineflayer 4.x — use `Object.values(bot.registry.blocks).find(b => b.id === block.type).name` for reverse lookup
- `toVec3()` position objects must include full Vec3 API or `bot.dig()` crashes internally
- `playerCollect` guard: check `collector.username !== bot.username` to filter other players' pickups
- `mineComplete` event must be emitted at end of every mine-block loop for correlation cleanup

## Related

- [Adapter State Memory](../memories/Core/agent-mineflayer-adapter-state.json)
- [Non-Mining Handlers](../memories/Core/agent-mineflayer-other-handlers.json)
- [Emergency Stop](emergency-stop.md)
- [WebSocket Bridge Memory](../memories/Core/agent-websocket-bridge.json)
- [Adapter Research Paper](../Tasks/mineflayer_adapter_research_paper.md)
