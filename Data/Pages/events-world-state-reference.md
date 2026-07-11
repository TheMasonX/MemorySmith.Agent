# MemorySmith.Agent — Events & World State Reference

**Last Updated:** 2026-07-10
**Agent:** Agent 5 (Event System Deep-Dive)
**Confidence:** 95%

---

## Table of Contents

1. [Complete Event Catalog](#1-complete-event-catalog)
2. [Event → C# Handler Mapping (ProcessEventsAsync)](#2-event--c-handler-mapping-processeventsasync)
3. [Adapter sendEvent → C# Event Type Mapping](#3-adapter-sendevent--c-event-type-mapping)
4. [WebSocketBridge Event Parsing (JSON Deserialization)](#4-websocketbridge-event-parsing-json-deserialization)
5. [WorldStateProjector (Apply Methods)](#5-worldstateprojector-apply-methods)
6. [Action Correlation System](#6-action-correlation-system)
7. [Inventory Event-Sourcing](#7-inventory-event-sourcing)
8. [Game Mode Detection Chain](#8-game-mode-detection-chain)
9. [Entity Observation Events (Sprint 55)](#9-entity-observation-events-sprint-55)
10. [Missing Handlers & Fallthrough Risks](#10-missing-handlers--fallthrough-risks)
11. [Error Event Routing (TryRouteAsError)](#11-error-event-routing-tryrouteaserror)
12. [WorldState Model](#12-worldstate-model)
13. [WorldStateDiff (Observe→Compare→Evaluate)](#13-worldstatediff-observecompareaverageevaluate)
14. [Tags](#14-tags)

---

## 1. Complete Event Catalog

**File:** `Agent.Core/Events/WorldEvents.cs` (lines 1–370)

All events inherit from `abstract record WorldEvent(DateTimeOffset Timestamp)`.

### Lifecycle Events

| # | Record Type | Sprint | Wire Name | Fields | Description |
|---|-------------|--------|-----------|--------|-------------|
| 1 | `SpawnEvent` | 3a | `spawn` | `Pos(Position)`, `Health(int)`, `Food(int)`, `Timestamp` | Bot spawned into the world. First spawn only — **respawn gap exists** (`bot.once('spawn')` does not re-fire on death/respawn). |
| 2 | `DeathEvent` | 3a | `death` | `Pos(Position)`, `Timestamp` | Bot died. Triggers goal cancel, queue clear, inventory marked stale, `_correlatedActions` cleared. |
| 3 | `KickedEvent` | 3a | `kicked` | `Reason(string)`, `Timestamp` | Bot kicked from server. Triggers `_connectionCts.Cancel()` to force full reconnection cycle. |
| 4 | `GameModeChangedEvent` | 3a | `gameMode` | `Mode(string)`, `Timestamp` | Game mode changed (e.g. survival→creative). Updates `WorldState.GameMode` via `SetGameMode()`. |

### State Events

| # | Record Type | Sprint | Wire Name | Fields | Description |
|---|-------------|--------|-----------|--------|-------------|
| 5 | `HealthEvent` | 3a | `health` | `Health(int)`, `Food(int)`, `Timestamp` | Emitted on **any** health change — damage taken **and** healing (eating, regeneration). Drives the passive health-check gate and damage-delta computation. Not a damage interrupt by itself. |
| 6 | `DamageTakenEvent` | 23 P0-A | *(synthetic)* | `PreviousHealth(int)`, `Health(int)`, `Delta(int)`, `Food(int)`, `Timestamp` | **Synthesized C#-side** by `AgentBackgroundService` from consecutive `HealthEvent` comparisons (when new Health < previous Health). `Delta` is always **negative**. Drives the damage interrupt path. NOT received from the Node.js wire. |
| 7 | `MoveEvent` | 3a | `move` / `moveComplete` | `Pos(Position)`, `Timestamp` | Bot position update. Fires ~10x/second during movement — logged at `Trace` level (Sprint 51 Wave B+). Also used to complete `MoveTo` correlation. |
| 8 | `StatusEvent` | 3a | `status` | `Pos(Position)`, `Health(int)`, `Food(int)`, `Inventory(IReadOnlyDictionary<string,int>)`, `GameMode(string?)`, `Timestamp` | Full state dump. Updates position, health, food, inventory, game mode. Clears `IsInventoryStale`. Normalizes inventory keys (strips `minecraft:` prefix). Completes `GetStatus`/`Status` correlation. |
| 9 | `BlockBelowChangedEvent` | 55 Wave C | *(not wired)* | `Name(string)`, `Type(int)`, `X,Y,Z(int)`, `Timestamp` | Block below bot's feet changed. **⚠️ GAP: Not parsed in WebSocketBridge switch** — wire name `blockBelowChanged` is not registered. Event defined in `WorldEvents.cs` and handled in `ProcessEventsAsync` (stores `blockBelow`, `blockBelowType`, `blockBelowAt`, `blockBelowUpdatedAt` facts), but the adapter never sends it and the bridge cannot deserialize it. See [§10 Missing Handlers](#10-missing-handlers--fallthrough-risks). |

### Action Result Events

| # | Record Type | Sprint | Wire Name | Fields | Description |
|---|-------------|--------|-----------|--------|-------------|
| 10 | `BlockMinedEvent` | 3a | `blockMined` | `Block(string)`, `Count(int)`, `Pos(Position)`, `BlockPosition(Position)`, `Timestamp` | A single block was mined. Sprint 40 P0-B added `BlockPosition` — the actual coordinates of the mined block (not bot position). `WorldStateProjector.ApplyBlockMined` increments inventory for self-dropping blocks via `CommonMinecraftBlocks.ResolveBlockDrop()`. Completes `MineBlock` correlation. Also clears `IsInventoryStale` (Sprint 37 Issue A). |
| 11 | `MineCompleteEvent` | 35 P0-B | `mineComplete` | `Block(string)`, `Mined(int)`, `TargetCount(int)`, `BlockPosition(Position)`, `Timestamp` | Emitted at the end of the mine action loop in Mineflayer. Definitive "mining is done" signal. Stored as facts only (lifecycle handled by `AgentBackgroundService` correlation). Sprint 40 P0-B added `BlockPosition` (last mined block). |
| 12 | `MineAbortedEvent` | 40 P0-C | `mineAborted` | `Block(string)`, `Mined(int)`, `TargetCount(int)`, `BlockPosition(Position?)`, `Timestamp` | Mine action loop aborted by stop signal. Completes `MineBlock` correlation. Partial progress carried in event. |
| 13 | `StopCompleteEvent` | 40 P0-C | `stopComplete` | `Timestamp` | Emergency stop acknowledged by adapter. No correlation transition — purely informational. |
| 14 | `BlockPlacedEvent` | 3a | `blockPlaced` | `X,Y,Z(int)`, `Block(string)`, `Timestamp`, `CorrelationId(Guid?)` | Block confirmed placed in the world. Advances build checkpoint via `AdvanceBuildCheckpoint()`. Increments `_blocksPlacedThisCycle`. Completes `place` correlation. Sprint 42 (TSK-0075): checkpoint advanced only on confirmed placement, not on fire-and-forget dispatch. TSK-0128: uses event's `CorrelationId` for direct context lookup. |
| 15 | `BlockPlaceSkippedEvent` | 43 P0-4 | `blockPlaceSkipped` | `X,Y,Z(int)`, `Block(string)`, `ExistingBlock(string)`, `Timestamp` | PlaceBlock target position occupied by different block (terrain collision). Completes `place` correlation but does NOT advance build checkpoint. Sprint 51: bot-position skip handling — advances checkpoint when skip target equals bot position to prevent infinite skip loop. |
| 16 | `CraftCompleteEvent` | 3a | `craftComplete` | `Item(string)`, `Count(int)`, `Timestamp` | Item crafted on the server. TSK-0117: `WorldStateProjector.ApplyCraftComplete` adds the crafted item to C#-side inventory. Completes `CraftItem` correlation. |
| 17 | `SmeltCompleteEvent` | 3a | `smeltComplete` | `Input(string)`, `Result(string)`, `Count(int)`, `Timestamp` | Item smelted on the server. TSK-0117: `WorldStateProjector.ApplySmeltComplete` adds the result to C#-side inventory. Completes `SmeltItem` correlation. |
| 18 | `WanderCompleteEvent` | 3a | `wanderComplete` | `Pos(Position)`, `TargetX(int)`, `TargetZ(int)`, `Timestamp` | Wander reached target coordinates. Completes `Wander` correlation. |
| 19 | `WanderFailedEvent` | 3a | `wanderFailed` | `Message(string)`, `Pos(Position)`, `Timestamp` | Wander pathfinder failure. Transitions `Wander` correlation to `Failed`. |
| 20 | `ItemCollectedEvent` | 35 P0-A | `itemCollected` | `Item(string)`, `Count(int)`, `Timestamp` | **Authoritative inventory source.** Fired by Mineflayer `playerCollect` event. Provides the **true drop name** (e.g. `diamond` from mining `diamond_ore`, not `diamond_ore`). Guard in `index.js`: only fires when `collector.username === bot.username`. Normalizes item key (strips `minecraft:` prefix). |
| 21 | `BlockNotFoundEvent` | 3a | `blockNotFound` | `Block(string)`, `MinedCount(int)`, `Timestamp` | Bot cannot find requested block within search radius. Routed to `_gameErrors` channel by `TryRouteAsError`. Tracks per-block failure count for progressive wander radius (fact key: `event:BlockNotFound:Count:{Block}`). Completes `MineBlock` correlation via `FailCorrelatedActionByTool("MineBlock")`. |
| 22 | `FlatAreaFoundEvent` | 3a | `flatAreaFound` | `X,Y,Z(int)`, `Area(int)`, `MinX,MaxX,MinZ,MaxZ(int)`, `SearchedRadius(int)`, `Timestamp` | Result of a `FindFlatArea` scan. `Area` is 0 when no flat region found. Sprint 35 P0-C: added `SearchedRadius` for retry logic. `Area >= MinUsableFlatArea(25)` → auto-set build origin. 2 consecutive zero-area scans → fallback to bot current position (Sprint 37). Completes `FindFlatArea` correlation. |
| 23 | `ReachableBlockFoundEvent` | 40 P0-B | `reachableBlockFound` | `Block(string)`, `X,Y,Z(int)`, `EuclideanDistance(double)`, `PathDistance(int)`, `Timestamp` | `findReachableBlock` action found a pathfindable block. Completes `FindReachableBlock` correlation. |

### Error Events

| # | Record Type | Sprint | Wire Name | Fields | Description |
|---|-------------|--------|-----------|--------|-------------|
| 24 | `ErrorEvent` | 3a | `error` | `Action(string)`, `Message(string)`, `Timestamp`, `X?,Y?,Z?(int?)`, `Block?(string)`, `Material?(string)`, `Item?(string)`, `ReasonCode?(string)` | Generic action error. Sprint 41: added optional position/block context fields. Sprint 55 (TSK-0165): added `ReasonCode` — machine-readable error classification. Routed to `_gameErrors` channel. Transitions correlated action to `Failed` via `MapNodeActionToToolName()`. |
| 25 | `ActionFailedEvent` | 55 TSK-0165 | `actionFailed` | `Action(string)`, `ReasonCode(string)`, `Detail(string)`, `CorrelationId(string?)`, `Timestamp` | Machine-readable action failure with structured reason code. Enables C# evaluator to make structured decisions without parsing free-form error strings. Completes correlation by ID or tool name. |

### Telemetry Events (Sprint 55 TSK-0165)

| # | Record Type | Sprint | Wire Name | Fields | Description |
|---|-------------|--------|-----------|--------|-------------|
| 26 | `ActionStartedEvent` | 55 | `actionStarted` | `Action(string)`, `CorrelationId(string?)`, `Timestamp` | Confirms JS adapter received and began processing the action. Logged at Debug level. No correlation transition (action remains `Dispatched`). |
| 27 | `ActionProgressEvent` | 55 | `actionProgress` | `Action(string)`, `Completed(int)`, `TargetCount(int)`, `PercentComplete(int)`, `CorrelationId(string?)`, `Timestamp` | Mid-action progress report for long-running actions (mining). Logged at Debug. No correlation transition. |
| 28 | `ActionCompletedEvent` | 55 | `actionCompleted` | `Action(string)`, `CorrelationId(string?)`, `Timestamp` | Generic completion signal for actions without dedicated completion events (e.g. `chat`). **Safety net** that complements domain-specific events (`blockPlaced`, `moveComplete`, etc.). `CompleteCorrelatedActionByTool/ById` are idempotent. |

### Environment Query Events (Sprint 55 Wave B)

| # | Record Type | Sprint | Wire Name | Fields | Description |
|---|-------------|--------|-----------|--------|-------------|
| 29 | `BlocksQueriedEvent` | 55 | `blocksQueried` | `Blocks(IReadOnlyList<QueriedBlock>)`, `From(Position)`, `To(Position)`, `CorrelationId(string?)`, `Timestamp` | Result of a block query (single position or bounding box). Empty list = air/void. Completes `QueryBlocks` correlation. |
| 30 | `EntitiesQueriedEvent` | 55 | `entitiesQueried` | `Entities(IReadOnlyList<ObservedEntity>)`, `Radius(int)`, `EntityTypeFilter(string?)`, `BotPosition(Position)`, `CorrelationId(string?)`, `Timestamp` | Result of an entity query within radius, sorted by distance. Completes `QueryEntities` correlation. |
| 31 | `EntityObservedEvent` | 55 | `entityObserved` | `Entities(IReadOnlyList<ObservedEntity>)`, `BotPosition(Position)`, `Timestamp` | Passive entity observation (~3s cooldown, 32-block radius). Includes hostile/non-hostile mobs with hostility flag. Stores `nearbyHostiles`, `nearbyEntities`, `nearbyEntitiesRaw` in `WorldState.Facts`. |

### Supporting Record Types

**File:** `Agent.Core/Events/WorldEvents.cs` (lines 339–370)

```csharp
public sealed record QueriedBlock(int X, int Y, int Z, string Name, int Type);

public sealed record ObservedEntity(
    string Name, string Type, bool Hostile,
    int X, int Y, int Z, double Distance,
    int? Health = null, string? Username = null);
```

### Chat Events

| # | Record Type | Sprint | Wire Name | Fields | Description |
|---|-------------|--------|-----------|--------|-------------|
| 32 | `ChatEvent` | 3a | `chat` | `Username(string)`, `Message(string)`, `OnlinePlayers(int)`, `PlayerPos(Position?)`, `Timestamp` | Player chat message. Offloaded to `_chatChannel` → `ChatConsumerAsync` for LLM processing (never blocks the event loop). Includes `PlayerPos` for proximity checks and `OnlinePlayers` count. |

### Stub Events (Sprint 35 P2-A, deferred to Sprint 36+)

| # | Record Type | Sprint | Wire Name | Fields | Status |
|---|-------------|--------|-----------|--------|--------|
| 33 | `ItemCraftedEvent` | 35 P2-A | *(none)* | `Item(string)`, `Count(int)`, `Timestamp` | **WIRED** in Sprint 36 P1-B. `WorldStateProjector.ApplyItemCrafted` updates inventory. |
| 34 | `ItemConsumedEvent` | 35 P2-A | *(none)* | `Item(string)`, `Count(int)`, `Timestamp` | **WIRED** in Sprint 38 P4-A. `WorldStateProjector.ApplyItemConsumed` deducts ingredients from inventory. Clamps at zero. |

---

## 2. Event → C# Handler Mapping (ProcessEventsAsync)

**File:** `WebUI.Blazor/AgentBackgroundService.cs`, method `ProcessEventsAsync()` (lines 687–1120)

### Pre-Processing (All Events)

Before the main switch, every event passes through:

1. **Logging:** `MoveEvent` → `LogTrace`; all others → `LogDebug`
2. **Projection:** `_worldState = _projector.Apply(_worldState, worldEvent);`
3. **Damage Detection:** If health dropped since last event, synthesize `DamageTakenEvent`, project it, and attempt `TryInterruptOnDamageAsync()`
4. **Passive Health Check:** If health is >0 and < `HealthCriticalThreshold(6)`, enqueue `GetStatus` (rate-limited to once per `HealthCheckCooldownSeconds(2)`)
5. **Baseline Update:** `_previousHealth` updated if `currentHealthNow > 0`
6. **Goal Completion Check:** `TryCompleteCurrentGoalFromWorldUpdate()` after each event
7. **Dashboard Push:** `_ = PushStatusToDashboardAsync(CancellationToken.None)` (fire-and-forget)

### ProcessEventsAsync Switch

| `case` | Handler Behavior | Correlation | Log Level |
|--------|-----------------|-------------|-----------|
| `SpawnEvent` | Log info | — | Info |
| `StatusEvent` | Complete `GetStatus` + `Status` correlations. Reset `_consecutiveGetStatusTimeouts`. Log game mode, inventory count, stale flag, position. | ✅ `CompleteCorrelatedActionByTool("GetStatus")` + `("Status")` | Info |
| `BlockMinedEvent` | Clear `IsInventoryStale` (Sprint 37 Issue A). Complete `MineBlock` correlation. Log inventory delta + block position. | ✅ `CompleteCorrelatedActionByTool("MineBlock")` | Info |
| `CraftCompleteEvent` | Log crafted item. Complete `CraftItem` correlation. | ✅ `CompleteCorrelatedActionByTool("CraftItem")` | Info |
| `SmeltCompleteEvent` | Log smelted item. Complete `SmeltItem` correlation. | ✅ `CompleteCorrelatedActionByTool("SmeltItem")` | Info |
| `KickedEvent` | Log critical. Journal entry. Cancel `_connectionCts` to force reconnection. | — | Critical |
| `ChatEvent` | Offload to `_chatChannel.Writer.TryWrite(worldEvent)` → `ChatConsumerAsync` | — | (offloaded) |
| `FlatAreaFoundEvent` (area ≥ 25) | Reset `_consecutiveZeroAreaScans`. Auto-set build origin. Journal entry. Complete `FindFlatArea` correlation. | ✅ `CompleteCorrelatedActionByTool("FindFlatArea")` | Info |
| `FlatAreaFoundEvent` (area < 25) | Increment `_consecutiveZeroAreaScans`. Fallback to bot position after 2 zeros. Complete correlation. | ✅ `CompleteCorrelatedActionByTool("FindFlatArea")` | Info / Warning |
| `MoveEvent` | Complete `MoveTo` correlation. | ✅ `CompleteCorrelatedActionByTool("MoveTo")` | Trace |
| `BlockPlacedEvent` | Log confirmation. Increment `_blocksPlacedThisCycle`. Increment `PlaceBlockGoal.Dispatched` via `Interlocked.Increment`. Advance build checkpoint. Complete `place` correlation. Log build progress. | ✅ `CompleteCorrelatedActionByTool("place")` | Info |
| `BlockPlaceSkippedEvent` | Log skip reason (botPosition / noReference / occupiedBy_X). Mark skipped block. Complete `place` correlation. | ✅ `CompleteCorrelatedActionByTool("place")` | Warning |
| `WanderCompleteEvent` | Complete `Wander` correlation. | ✅ `CompleteCorrelatedActionByTool("Wander")` | Debug |
| `WanderFailedEvent` | Fail `Wander` correlation. | ❌ `FailCorrelatedActionByTool("Wander")` | Warning |
| `MineAbortedEvent` | Log abort. Complete `MineBlock` correlation. | ✅ `CompleteCorrelatedActionByTool("MineBlock")` | Warning |
| `StopCompleteEvent` | Log acknowledgment. | — | Debug |
| `ActionStartedEvent` | Log at Debug. | — | Debug |
| `ActionProgressEvent` | Log progress at Debug. | — | Debug |
| `ActionFailedEvent` | Log warning with reasonCode. Complete correlation by ID or tool name. | ✅ `CompleteCorrelatedActionById` or `CompleteCorrelatedActionByTool` | Warning |
| `ActionCompletedEvent` | Log at Debug. Complete correlation by ID or tool name (safety net). | ✅ `CompleteCorrelatedActionById` or `CompleteCorrelatedActionByTool` | Debug |
| `BlocksQueriedEvent` | Log block count + bounds. Complete `QueryBlocks` correlation. | ✅ `CompleteCorrelatedActionByTool(ActionProtocol.QueryBlocks)` | Info |
| `EntitiesQueriedEvent` | Log entity count + radius + filter. Complete `QueryEntities` correlation. | ✅ `CompleteCorrelatedActionByTool(ActionProtocol.QueryEntities)` | Info |
| `EntityObservedEvent` | Log all observed entities. Store `nearbyHostiles`, `nearbyEntities`, `nearbyEntitiesRaw` in `WorldState.Facts`. | — | Info |
| `BlockBelowChangedEvent` | Log changed block. Store `blockBelow`, `blockBelowType`, `blockBelowAt`, `blockBelowUpdatedAt` in `WorldState.Facts`. | — | Debug |
| `ReachableBlockFoundEvent` | Complete `FindReachableBlock` correlation. | ✅ `CompleteCorrelatedActionByTool("FindReachableBlock")` | Debug |
| `DeathEvent` | Log warning. Set `IsInventoryStale = true`. Clear `_pendingActions`. Clear `_correlatedActions`. Set `_currentGoal = null`. Reset `_consecutiveFailures`. Journal entry. | ❌ Purges ALL correlations | Warning |
| `default` | Log Debug (not Warning) for projector-only events. Call `TryRouteAsError(worldEvent)`. | — | Debug |

### Events Handled ONLY by WorldStateProjector (no switch case)

These events pass through the projector and `default` case but have no correlation/handler logic:

- `HealthEvent` — projector updates Health/Food; health delta computed in pre-processing
- `DamageTakenEvent` — facts stored; interrupt path is separate (`TryInterruptOnDamageAsync`)
- `GameModeChangedEvent` — projector updates game mode
- `ErrorEvent` — falls through to `default` → `TryRouteAsError` routes to `_gameErrors`
- `BlockNotFoundEvent` — falls through to `default` → `TryRouteAsError` routes to `_gameErrors`

---

## 3. Adapter sendEvent → C# Event Type Mapping

**File:** `MineflayerAdapter/index.js` (adapter), `WebSocketBridge.cs` (C# parsing)

### sendEvent Names Used by the Adapter

| Wire Event Name | C# Event | Adapter Source (JS) |
|----------------|----------|---------------------|
| `spawn` | `SpawnEvent` | `bot.once('spawn')` |
| `health` | `HealthEvent` | `bot.on('health')` |
| `gameMode` | `GameModeChangedEvent` | Game mode detection |
| `move` | `MoveEvent` | `bot.on('move')` |
| `moveComplete` | `MoveEvent` | Pathfinder goal reached |
| `blockMined` | `BlockMinedEvent` | Per-dig output |
| `blockPlaced` | `BlockPlacedEvent` | PlaceBlock completion |
| `blockPlaceSkipped` | `BlockPlaceSkippedEvent` | Terrain collision skip (Sprint 43) |
| `blockNotFound` | `BlockNotFoundEvent` | No block in search radius |
| `itemCollected` | `ItemCollectedEvent` | `bot.on('playerCollect')` (Sprint 35 P0-A) |
| `mineComplete` | `MineCompleteEvent` | End of mine loop (Sprint 35 P0-B) |
| `mineAborted` | `MineAbortedEvent` | Mine loop stopped (Sprint 40 P0-C) |
| `craftComplete` | `CraftCompleteEvent` | Craft action done |
| `smeltComplete` | `SmeltCompleteEvent` | Smelt action done |
| `wanderComplete` | `WanderCompleteEvent` | Wander reached target |
| `wanderFailed` | `WanderFailedEvent` | Wander pathfinder failure |
| `flatAreaFound` | `FlatAreaFoundEvent` | findFlatArea result |
| `reachableBlockFound` | `ReachableBlockFoundEvent` | findReachableBlock result (Sprint 40 P0-B) |
| `chat` | `ChatEvent` | `bot.on('chat')` (filtered via SYSTEM_MESSAGE_PATTERNS) |
| `death` | `DeathEvent` | `bot.on('death')` |
| `kicked` | `KickedEvent` | `bot.on('kicked')` |
| `status` | `StatusEvent` | GetStatus response |
| `stopComplete` | `StopCompleteEvent` | handleStop() finished (Sprint 40 P0-C) |
| `error` | `ErrorEvent` | Generic adapter error |
| `actionStarted` | `ActionStartedEvent` | Action dispatch confirmed (Sprint 55 TSK-0165) |
| `actionProgress` | `ActionProgressEvent` | Mid-action progress (Sprint 55) |
| `actionFailed` | `ActionFailedEvent` | Machine-readable failure (Sprint 55) |
| `actionCompleted` | `ActionCompletedEvent` | Generic completion (Sprint 55) |
| `blocksQueried` | `BlocksQueriedEvent` | Block query result (Sprint 55 Wave B) |
| `entitiesQueried` | `EntitiesQueriedEvent` | Entity query result (Sprint 55 Wave B) |
| `entityObserved` | `EntityObservedEvent` | Passive entity scan (Sprint 55 Wave B) |

### Not Sent by Adapter (though C# defines the type)

- `blockBelowChanged` — wire name not registered in `WebSocketBridge.cs`. `BlockBelowChangedEvent` is defined in `WorldEvents.cs` and handled in `ProcessEventsAsync`, but the adapter never sends it and the bridge cannot parse it.
- `ItemCraftedEvent` / `ItemConsumedEvent` — stub events; `CraftCompleteEvent` and `SmeltCompleteEvent` cover the actual adapter contract. `ItemCraftedEvent` is synthesized or used internally.

---

## 4. WebSocketBridge Event Parsing (JSON Deserialization)

**File:** `Agent.World.Minecraft/WebSocketBridge.cs` (lines 290–500, 560–700)

### Architecture

```
Mineflayer (Node.js) → JSON over WebSocket → WebSocketBridge (C#)
  → Channel<WorldEvent> _inbound (unbounded, SingleWriter)
    → IWorldAdapter.ReceiveEventsAsync()
      → AgentBackgroundService.ProcessEventsAsync()
```

### JSON Parsing Engine

- **Serializer:** `System.Text.Json` with `PropertyNameCaseInsensitive = true`
- **Buffer:** 32,768 bytes, with multi-frame message reassembly via `StringBuilder`
- **Channel:** `Channel.CreateUnbounded<WorldEvent>(new UnboundedChannelOptions { SingleWriter = true })`
- **Retry:** `RunReceiveLoopWithRetryAsync` — up to 3 retries with 5s delay, full reconnect including handshake re-send

### ParseEvent Switch

The main `ParseEvent` method in `WebSocketBridge.cs` (line 290) uses an `eventType switch` on the `"event"` field of the incoming JSON:

```csharp
eventType switch
{
    "spawn"             => new SpawnEvent(...),
    "health"            => new HealthEvent(...),
    "gameMode"          => new GameModeChangedEvent(...),
    "move" or "moveComplete" => new MoveEvent(...),
    "blockMined"        => new BlockMinedEvent(...),
    "itemCollected"     => new ItemCollectedEvent(...),
    "mineComplete"      => new MineCompleteEvent(...),
    "chat"              => new ChatEvent(...),
    "error"             => new ErrorEvent(...),
    "blockNotFound"     => new BlockNotFoundEvent(...),
    "craftComplete"     => new CraftCompleteEvent(...),
    "smeltComplete"     => new SmeltCompleteEvent(...),
    "death"             => new DeathEvent(...),
    "status"            => ParseStatus(root, now, instanceLogger),
    "blockPlaced"       => new BlockPlacedEvent(...),
    "blockPlaceSkipped" => new BlockPlaceSkippedEvent(...),
    "wanderComplete"    => new WanderCompleteEvent(...),
    "wanderFailed"      => new WanderFailedEvent(...),
    "kicked"            => new KickedEvent(...),
    "flatAreaFound"     => new FlatAreaFoundEvent(...),
    "mineAborted"       => new MineAbortedEvent(...),
    "stopComplete"      => new StopCompleteEvent(...),
    "reachableBlockFound" => new ReachableBlockFoundEvent(...),
    "actionStarted"     => new ActionStartedEvent(...),
    "actionProgress"    => new ActionProgressEvent(...),
    "actionFailed"      => new ActionFailedEvent(...),
    "actionCompleted"   => new ActionCompletedEvent(...),
    "blocksQueried"     => ParseBlocksQueried(root, now),
    "entitiesQueried"   => ParseEntitiesQueried(root, now),
    "entityObserved"    => ParseEntityObserved(root, now),
    _                   => null,   // unknown — silently ignored
};
```

### JSON Helper Methods

**File:** `Agent.World.Minecraft/WebSocketBridge.cs` (lines 570–700)

| Method | Purpose | Default |
|--------|---------|---------|
| `GetInt(root, key, defaultValue=0)` | Parse integer from JSON property | 0 |
| `GetString(root, key, defaultValue=null)` | Parse string from JSON property | null |
| `GetIntOrNull(root, key)` | Parse optional integer (returns null for missing/non-numeric) | null |
| `GetDouble(root, key, defaultValue=0)` | Parse double from JSON property | 0.0 |
| `TryGetGuid(root, key)` | Parse optional Guid from JSON string property | null |
| `ParseStatus(root, now, logger)` | Complex status parser — handles both string-encoded and object-encoded inventory JSON | — |
| `ParseBlocksQueried(root, now)` | Parse block query results array | — |
| `ParseEntitiesQueried(root, now)` | Parse entity query results array | — |
| `ParseEntityObserved(root, now)` | Parse passive entity observation array | — |
| `ParseObservedEntities(root)` | Shared entity array parser (used by both queries and observations) | — |

### StatusEvent Inventory Parsing

The `ParseStatus` method (line 560) handles two inventory wire formats:

1. **String-encoded** (legacy): `{"inventory": "{\"oak_log\": 5, \"dirt\": 3}"}`
2. **Object-encoded** (modern): `{"inventory": {"oak_log": 5, "dirt": 3}}`

Both normalize to `Dictionary<string, int>`. Non-positive counts are filtered out.

### Handshake Protocol (Sprint 32 SEC-02)

```
C# → Node: {"type":"handshake","secret":"<shared_secret>"}
```

Sent on initial connect and every reconnect. Skipped when `AdapterSecret` is null/empty (dev mode).

---

## 5. WorldStateProjector (Apply Methods)

**File:** `Agent.Core/WorldStateProjector.cs` (lines 1–280+)

### Architecture

Pure, stateless `Apply(WorldState current, WorldEvent ev) → WorldState` — no I/O, no logging, no mutable shared state. Returns a NEW `WorldState` record (immutable projection). Caller is responsible for routing errors to typed error channel.

### Apply Method Dispatch

```csharp
public WorldState Apply(WorldState current, WorldEvent ev) => ev switch
{
    SpawnEvent e          => ApplySpawn(current, e),
    HealthEvent e         => ApplyHealth(current, e),
    GameModeChangedEvent e => ApplyGameModeChanged(current, e),
    DamageTakenEvent e    => StoreFacts(current, e),
    MoveEvent e           => ApplyMove(current, e),
    BlockMinedEvent e     => ApplyBlockMined(current, e),     // Sprint 40 P0-B fix
    ItemCollectedEvent e  => ApplyItemCollected(current, e),  // Sprint 35 P0-A
    MineCompleteEvent e   => StoreFacts(current, e),          // Sprint 35 P0-B
    ItemCraftedEvent e    => ApplyItemCrafted(current, e),    // Sprint 36 P1-B
    ItemConsumedEvent e   => ApplyItemConsumed(current, e),   // Sprint 38 P4-A
    StatusEvent e         => ApplyStatus(current, e),
    CraftCompleteEvent e  => ApplyCraftComplete(current, e),  // TSK-0117
    SmeltCompleteEvent e  => ApplySmeltComplete(current, e),  // TSK-0117
    _ => StoreFacts(current, ev, SourceFor(ev)),
};
```

### Per-Event Apply Details

| Method | Structured State Change | Fact Keys Written | Sprint |
|--------|----------------------|-------------------|--------|
| `ApplySpawn` | `Position = e.Pos`, `Health = e.Health`, `Food = e.Food` | `event:Spawn:Pos`, `:Health`, `:Food` | 3a |
| `ApplyHealth` | `Health = e.Health`, `Food = e.Food` | `event:Health:Health`, `:Food` | 3a |
| `ApplyGameModeChanged` | `SetGameMode(e.Mode)` via `WorldState.Builder` | `event:GameModeChanged:Mode` | 3a |
| `ApplyMove` | `Position = e.Pos` | `event:Move:Pos` | 3a |
| `ApplyBlockMined` | **Inventory increment** via `CommonMinecraftBlocks.ResolveBlockDrop(e.Block)` — restores pre-Sprint-35 behavior for self-dropping blocks while retaining `ItemCollectedEvent` for ore drops | `event:BlockMined:Block`, `:Count`, `:Pos` | 40 P0-B fix |
| `ApplyItemCollected` | **Inventory increment** for the true drop name. Normalizes `minecraft:` prefix. | `event:ItemCollected:Item`, `:Count` | 35 P0-A |
| `ApplyItemCrafted` | **Inventory increment** for crafted output. Normalizes prefix. | `event:ItemCrafted:Item`, `:Count` | 36 P1-B |
| `ApplyItemConsumed` | **Inventory deduction** for ingredients. Clamps at zero. Uses `SetInventory()` with updated dictionary. | `event:ItemConsumed:Item`, `:Count` | 38 P4-A |
| `ApplyStatus` | `SetPosition`, `SetHealth`, `SetFood`, `SetInventory(NormalizeInventory)`, `SetInventoryStale(false)`, `SetGameMode` (if non-null) | `event:Status:Pos`, `:Health`, `:Food` | 3a |
| `ApplyCraftComplete` | **Inventory increment** for crafted output (post-craft sync) | `event:CraftComplete:Item`, `:Count` | TSK-0117 |
| `ApplySmeltComplete` | **Inventory increment** for smelted result (post-smelt sync) | `event:SmeltComplete:Input`, `:Result`, `:Count` | TSK-0117 |

### StoreFacts — All Other Events

Events without structured state changes are stored as raw facts for debugging:

| Event | Fact Keys |
|-------|-----------|
| `DamageTakenEvent` | `event:DamageTaken:PreviousHealth`, `:Health`, `:Delta`, `:Food` |
| `MineCompleteEvent` | `event:MineComplete:Block`, `:Mined`, `:TargetCount` |
| `ChatEvent` | `event:Chat:Username`, `:Message`, `:OnlinePlayers`, `:PlayerPos` (if set) |
| `ErrorEvent` | `event:Error:Action`, `:Message` |
| `BlockNotFoundEvent` | `event:BlockNotFound:Block`, `:MinedCount` |
| `DeathEvent` | `event:Death:Pos` |
| `BlockPlacedEvent` | `event:BlockPlaced:X`, `:Y`, `:Z`, `:Block` |
| `WanderCompleteEvent` | `event:WanderComplete:Pos`, `:TargetX`, `:TargetZ` |
| `WanderFailedEvent` | `event:WanderFailed:Message`, `:Pos` |
| `KickedEvent` | `event:Kicked:Reason` |
| `FlatAreaFoundEvent` | `event:FlatAreaFound:X`, `:Y`, `:Z`, `:Area`, `:MinX`, `:MaxX`, `:MinZ`, `:MaxZ`, `:SearchedRadius` + `BuildFactKeys.LastFlatArea` |

### Block-to-Item Drop Resolution

**File:** `Agent.Core/CommonMinecraftBlocks.cs` (TSK-0108)

`CommonMinecraftBlocks.ResolveBlockDrop(string blockName)` is the shared mapping used by both `WorldStateProjector.ApplyBlockMined` and `WorldModel.PredictMine`:

1. Check `BlockToItemDrop` dictionary (e.g. `stone → cobblestone`, `diamond_ore → diamond`)
2. Check `SelfDroppingBlocks` set (e.g. `dirt`, `sand`, `oak_log`)
3. Fallback: return the block name itself

### Inventory Normalization

`WorldStateProjector.NormalizeInventory()` (lines 200-220) strips the `minecraft:` namespace prefix from all inventory keys. Fast-paths when no keys contain `:`.

---

## 6. Action Correlation System

**File:** `WebUI.Blazor/AgentBackgroundService.cs`

### Core Data Structures

| Component | Type | Location (in ABS) | Purpose |
|-----------|------|-------------------|---------|
| `_correlatedActions` | `ConcurrentDictionary<Guid, PendingAction>` | line ~205 | Tracks dispatched action lifecycle |
| `_placeBlockContexts` | `ConcurrentDictionary<Guid, (string BlueprintId, int BlockIndex)>` | line ~215 | Build context for PlaceBlock correlation |
| `_blockTimeoutCounts` | `ConcurrentDictionary<int, int>` | line ~270 | Per-block consecutive timeout tracking |
| `_cycleOutcomes` | `ConcurrentQueue<ActionOutcome>` | line ~225 | Accumulated outcomes for LLM evaluator |

### PendingAction

**File:** `Agent.Core/Models/PendingAction.cs`

```csharp
public sealed record PendingAction(
    Guid CorrelationId,
    string ToolName,
    DateTimeOffset DispatchedAt,
    ActionLifecycle State);
```

### ActionLifecycle States

**File:** `Agent.Core/Models/ActionLifecycle.cs`

```
Dispatched → Completed (result event received)
Dispatched → Failed    (error event / actionFailed / router failure)
Dispatched → TimedOut  (sweep timeout exceeded)
Dispatched → Acknowledged → Completed (future: wire-level ACK)
Dispatched → Acknowledged → Failed    (future: wire-level ACK)
```

### CAS Transition (Thread-Safe)

```csharp
// TransitionCorrelatedAction — CAS loop for atomic read-modify-write
while (_correlatedActions.TryGetValue(correlationId, out var current))
{
    var updated = current.WithState(newState);
    if (_correlatedActions.TryUpdate(correlationId, updated, current))
        return; // success
    // Another thread updated first — spin and retry
}
```

### Transition Methods

| Method | Signature | Purpose |
|--------|-----------|---------|
| `CompleteCorrelatedActionByTool` | `(string toolName)` | Finds first Dispatched action matching tool name, transitions to Completed |
| `CompleteCorrelatedActionById` | `(Guid correlationId)` | Transitions specific action by ID (more precise, Sprint 55 TSK-0165) |
| `FailCorrelatedActionByTool` | `(string toolName)` | Finds first Dispatched action matching tool name, transitions to Failed |
| `SweepTimedOutActions` | `()` | Scans all Dispatched actions, transitions those past timeout to TimedOut |

### Per-Tool Timeout Overrides

**File:** `WebUI.Blazor/AgentBackgroundService.cs` (lines 120-150)

| Tool | Timeout (s) | Rationale | Sprint |
|------|-------------|-----------|--------|
| `place` | 15 | Pathfinding + adapter queue = 2-5s per block (increased from 5s, TSK-0270) | 56 |
| `GetStatus` / `Status` | 10 | Stale inventory guard; adapter should respond quickly | 54 |
| `MoveTo` | 10 | Pathfinding timeout | 41 |
| `Wander` | 15 | Wander range | 41 |
| `SmeltItem` | 45 | Must exceed JS SMELT_TIMEOUT_MS (40s) | 44 |
| `mine` | 15 | Most mines complete in 2-5s; faster fail+retry (TSK-0332) | 58 |
| Default | 30 | All other tools | 25 |

### Fire-and-Forget Tools

These tools dispatch to Node.js asynchronously and receive results via world events:

`MoveTo`, `MineBlock`, `place`, `GetStatus`, `Status`, `Wander`, `CraftItem`, `SmeltItem`, `FindFlatArea`, `FindReachableBlock`

Non-fire-and-forget tools (Chat, SearchMemory, GetPage, CreatePage, QueryBlocks, QueryEntities) complete synchronously within `CallAsync`.

### PlaceBlock Context Correlation (TSK-0075, TSK-0128)

```
Dispatch:  ActionData → correlationId generated → stored in _placeBlockContexts[correlationId]
                                              → stored in _correlatedActions[correlationId]
           (checkpoint NOT advanced at dispatch)

Complete:  BlockPlacedEvent arrives → lookup _placeBlockContexts by event.CorrelationId
           → AdvanceBuildCheckpoint(blueprintId, blockIndex)
           → CompleteCorrelatedActionByTool("place")
           → Increment PlaceBlockGoal.Dispatched (Interlocked, Sprint 58 TSK-0330)
           → LogBuildProgress()

Skip:      BlockPlaceSkippedEvent → MarkSkippedBlock(skipReason)
           → CompleteCorrelatedActionByTool("place")
           (checkpoint NOT advanced)

Timeout:   SweepTimedOutActions → if timed out 3× consecutively → auto-skip block
           (TSK-0226: MaxConsecutivePlaceTimeouts = 3)
           _placeBlockContexts NOT removed for TimedOut (late events may arrive)
```

### Consecutive GetStatus Timeout Escalation (TSK-0221)

After 2 consecutive `GetStatus` timeouts, `IsInventoryStale` is force-cleared to prevent indefinite blocking on an unresponsive adapter.

### Sweep Cleanup (Sprint 44 P1-2)

The sweep also cleans up stale `_placeBlockContexts` entries whose correlationIds no longer exist in `_correlatedActions`. An orphan safety check ensures entries exist for at least 1 second before being treated as orphans.

---

## 7. Inventory Event-Sourcing

**ADR D-013:** Inventory is event-sourced via `ItemCollectedEvent`.

### Authoritative Inventory Sources (by priority)

| Priority | Source | Event | Characteristics |
|----------|--------|-------|-----------------|
| 1 | **StatusEvent** | Full inventory snapshot | Absolute truth — overwrites all. Delivered via `GetStatus` tool. Clears `IsInventoryStale`. |
| 2 | **ItemCollectedEvent** | `playerCollect` | True drop name (diamond, not diamond_ore). Guarded by `collector.username === bot.username`. |
| 3 | **BlockMinedEvent** | Per-dig | Self-dropping blocks only (via `CommonMinecraftBlocks.ResolveBlockDrop()`). Restored in Sprint 40 P0-B fix. |
| 4 | **CraftCompleteEvent** / **SmeltCompleteEvent** | Post-craft/post-smelt | Adds output to C#-side inventory (TSK-0117). |
| 5 | **ItemCraftedEvent** | Internal/C# (Sprint 36) | Adds crafted items. |
| 6 | **ItemConsumedEvent** | Internal/C# (Sprint 38) | Deducts ingredients. Clamps at zero. |

### Inventory Staleness Guard (Sprint 21 P0-A)

- `WorldState.IsInventoryStale` = `true` after `SetGoal()` or `DeathEvent`
- Cleared by `StatusEvent` (via projector) or `BlockMinedEvent` (Sprint 37 Issue A)
- `GenericGatherGoal.IsComplete` returns `false` while stale — prevents false-completion after admin `/clear`
- After 2 consecutive `GetStatus` timeouts, stale flag is force-cleared (TSK-0221)

### Inventory Normalization

All inventory paths normalize `minecraft:` prefixed keys:
- `"minecraft:oak_log"` → `"oak_log"`
- Duplicate keys (one prefixed, one bare) are merged

---

## 8. Game Mode Detection Chain

**File:** `Agent.Core/Models/WorldState.cs`, `WebUI.Blazor/AgentBackgroundService.cs`

### Flow

1. **`GameModeChangedEvent`** (wire: `gameMode`) → `WorldStateProjector.ApplyGameModeChanged` → `WorldState.Builder.SetGameMode(mode)` — writes to both `WorldState.GameMode` and `Facts["world:gamemode"]`
2. **`StatusEvent`** (wire: `status`) → `ApplyStatus` — if `e.GameMode` is non-null, calls `SetGameMode(e.GameMode)`. Sprint 37: `StatusEvent` now carries game mode.
3. **`IsCreativeMode`** property — checks `WorldState.GameMode` first, then falls back to `Facts["world:gamemode"]`:

```csharp
public bool IsCreativeMode => MatchesCreativeMode(GameMode)
    || (Facts.TryGetValue("world:gamemode", out var gm)
        && MatchesCreativeMode(Convert.ToString(gm)));
```

4. **Match function**: case-insensitive match on `"creative"` or `"creative mode"`

### Key Paths

| Path | File | Lines |
|------|------|-------|
| `GameModeChangedEvent` definition | `Agent.Core/Events/WorldEvents.cs` | ~40 |
| WebSocketBridge parse: `"gameMode"` | `Agent.World.Minecraft/WebSocketBridge.cs` | ~313 |
| `WorldStateProjector.ApplyGameModeChanged` | `Agent.Core/WorldStateProjector.cs` | ~70 |
| `WorldState.IsCreativeMode` | `Agent.Core/Models/WorldState.cs` | ~50 |
| `WorldState.Builder.SetGameMode` | `Agent.Core/Models/WorldState.cs` | ~85 |

---

## 9. Entity Observation Events (Sprint 55)

### Event Types

| Event | Wire Name | Trigger | Cooldown | Purpose |
|-------|-----------|---------|----------|---------|
| `EntityObservedEvent` | `entityObserved` | Passive scan (periodic) | ~3s | Threat detection, LLM prompt enrichment |
| `EntitiesQueriedEvent` | `entitiesQueried` | On-demand `QueryEntities` tool | None | Specific entity type query |

### EntityObservedEvent Handler (ProcessEventsAsync)

- Logs all entities with name + distance
- Separates hostiles from non-hostiles
- Stores in `WorldState.Facts`:
  - `nearbyHostiles` — formatted hostile entity summary
  - `nearbyHostilesUpdatedAt` — ISO 8601 timestamp
  - `nearbyEntities` — formatted all-entity summary
  - `nearbyEntitiesUpdatedAt` — ISO 8601 timestamp
  - `nearbyEntitiesRaw` — full JSON serialization of all entities (including positions, health, username)

### ObservedEntity Record

```csharp
public sealed record ObservedEntity(
    string Name,       // e.g. "zombie", "cow"
    string Type,       // e.g. "mob", "player"
    bool Hostile,      // true for zombies, skeletons, etc.
    int X, int Y, int Z,
    double Distance,   // Euclidean distance from bot
    int? Health = null,
    string? Username = null);  // for player entities
```

### FormatEntitySummary

Groups entities by name, sorts by distance, limits to 10, includes nearest position:

```
"2x zombie (@10blk 100,64,200), 1x skeleton (@15blk 105,64,210)"
```

---

## 10. Missing Handlers & Fallthrough Risks

### ⚠️ GAP 1: BlockBelowChangedEvent — Not Wired in WebSocketBridge

**Severity:** Medium
**Description:** `BlockBelowChangedEvent` is defined in `WorldEvents.cs` (Sprint 55 Wave C) and handled in `ProcessEventsAsync` (stores `blockBelow`, `blockBelowType`, etc. facts). However, the wire name `blockBelowChanged` is **not registered** in the `WebSocketBridge.ParseEvent` switch, and the Mineflayer adapter (`MineflayerAdapter/index.js`) has **no code** emitting this event.

**Impact:** The handler code exists but never executes. The `WorldState.Facts["blockBelow*"]` keys are never populated.

**Fix:** Either:
- Implement and wire `blockBelowChanged` in the JS adapter + register in WebSocketBridge, or
- Remove the dead handler code

### ⚠️ GAP 2: Respawn Gap

**Severity:** Medium
**Description:** `bot.once('spawn')` only fires on first spawn. If bot dies and respawns, no `SpawnEvent` re-triggers. C# side receives `DeathEvent` (which clears `_correlatedActions`, `_currentGoal`, marks inventory stale) but must wait for next `StatusEvent` to get fresh spawn state.

**Impact:** Brief window between death and next GetStatus where the agent has no confirmed goal or inventory.

### ⚠️ GAP 3: blockNotFound — Dual Purpose, Single Event

**Severity:** Low
**Description:** The adapter sends `blockNotFound` for both:
1. Mine action: no blocks found in search radius
2. `findReachableBlock` action: no reachable blocks

The C# side only maps `BlockNotFoundEvent` → fail `MineBlock`. If `findReachableBlock` fails with `blockNotFound`, the correlation may not be properly resolved.

### ⚠️ GAP 4: ItemCraftedEvent / ItemConsumedEvent — No Adapter Source

**Severity:** Low
**Description:** `ItemCraftedEvent` and `ItemConsumedEvent` are defined in `WorldEvents.cs` and wired in `WorldStateProjector`, but they have **no wire name** in `WebSocketBridge.cs`. They appear to be internal/C#-side events only. The actual adapter communication uses `CraftCompleteEvent` / `SmeltCompleteEvent` (TSK-0117) for inventory reconciliation.

### ⚠️ GAP 5: MoveEvent Flooding

**Severity:** Low (mitigated)
**Description:** `MoveEvent` fires ~10x/second during movement. Sprint 51 Wave B+ mitigates by logging at `Trace` level. The event is still projected and completes `MoveTo` correlation on every tick, which is correct (the first tick after arrival completes the correlation).

---

## 11. Error Event Routing (TryRouteAsError)

**File:** `WebUI.Blazor/AgentBackgroundService.cs` (lines 1762–1840)

### Flow

```
ProcessEventsAsync default: → TryRouteAsError(worldEvent)
```

### Route Logic

```csharp
private void TryRouteAsError(WorldEvent worldEvent)
{
    if (worldEvent is BlockNotFoundEvent bnf && bnf.MinedCount == 0)
    {
        // Route to _gameErrors channel
        // Track per-block failure count (fact: event:BlockNotFound:Count:{Block})
        // Fail correlated MineBlock
        FailCorrelatedActionByTool("MineBlock");
    }
    else if (worldEvent is ErrorEvent err)
    {
        // Include position/block context in log line
        // Route to _gameErrors channel
        // Map Node.js wire action → C# tool name
        // Fail correlated action
        FailCorrelatedActionByTool(MapNodeActionToToolName(err.Action));
    }
}
```

### Node.js → C# Action Name Mapping

**File:** `WebUI.Blazor/AgentBackgroundService.cs` (lines 1830–1860)

| Wire Name (Node.js) | C# Tool Name |
|---------------------|-------------|
| `mine` | `MineBlock` |
| `move` | `MoveTo` |
| `place` | `PlaceBlock` |
| `wander` | `Wander` |
| `chat` | `Chat` |
| `findflatarea` | `FindFlatArea` |
| `findreachableblock` | `FindReachableBlock` |
| `craft` | `CraftItem` |
| `smelt` | `SmeltItem` |
| `getstatus` / `status` | `GetStatus` |
| *(fallback)* | Pass through unchanged |

### Error Event Fields (Sprint 41, Sprint 55)

`ErrorEvent` now carries (all optional):
- `X, Y, Z` — position of the failed action
- `Block`, `Material`, `Item` — contextual block/item info
- `ReasonCode` — machine-readable classification (Sprint 55 TSK-0165)

---

## 12. WorldState Model

**File:** `Agent.Core/Models/WorldState.cs` (lines 1–150)

### Core Record

```csharp
public record WorldState
{
    public const int MaxFacts = 1000;

    public string AgentId { get; init; } = string.Empty;
    public Position Position { get; init; } = new();     // defaults to (0, 64, 0)
    public int Health { get; init; } = 20;
    public int Food { get; init; } = 20;
    public string? GameMode { get; init; }
    public Dictionary<string, int> Inventory { get; init; } = [];
    public Dictionary<string, object?> Facts { get; init; } = [];
    public IReadOnlyList<Fact> StructuredFacts { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public bool IsInventoryStale { get; init; } = false;
}
```

### Position

```csharp
public record Position(int X = 0, int Y = 64, int Z = 0);
```

### Builder Pattern

`WorldState.With(Action<Builder>)` returns a new `WorldState`. Builder methods:

| Method | Effect |
|--------|--------|
| `SetHealth(int)` | Sets health |
| `SetFood(int)` | Sets food |
| `SetPosition(Position)` | Sets position |
| `SetGameMode(string?)` | Sets GameMode + writes `Facts["world:gamemode"]` |
| `SetFact(string, object?)` | Legacy: sets fact in `Facts` dictionary **[Obsolete]** |
| `SetFact(string, string, FactSource)` | Sets fact + appends to `StructuredFacts`. Trims oldest at MaxFacts (1000). |
| `ClearFactsByPrefix(string)` | Removes all facts (legacy + structured) with matching prefix (TSK-0125) |
| `SetInventory(IReadOnlyDictionary)` | Replaces entire inventory snapshot |
| `AddInventoryItem(string, int delta)` | Increments/decrements single item. Removes entry when count ≤ 0. |
| `SetInventoryStale(bool)` | Sets stale flag |
| `Build()` | Returns final `WorldState` with `UpdatedAt = UtcNow` |

### IsCreativeMode

Checks `WorldState.GameMode` first, then falls back to `Facts["world:gamemode"]`. Matches `"creative"` or `"creative mode"` (case-insensitive).

### Fact

**File:** `Agent.Core/Models/Fact.cs`

```csharp
public record Fact(string Key, string Value, FactSource Source, DateTimeOffset Timestamp);
```

### FactSource Enum

| Value | Usage |
|-------|-------|
| `Observed` | Directly from world event (spawn, move, health, block mined, etc.) |
| `Inferred` | Deduced (error messages, constraint violations) — used for `ErrorEvent` facts |
| `Durable` | Persisted across sessions |
| `PlayerInstruction` | From player chat command (Sprint 36 P1-A) |
| `Memory` | From MemorySmith search result (Sprint 36 P1-A) |
| `Scan` | From FindFlatArea scan — maps to `BuildOriginSource.AutoScanned` |
| `Recovery` | Recovery action / replan provenance (Sprint 36 P1-A) |

---

## 13. WorldStateDiff (Observe→Compare→Evaluate)

**File:** `Agent.Core/Models/WorldStateDiff.cs` (Sprint 55 TSK-0155)

### Purpose

Captures delta between expected and observed world state after action execution. Drives the observe→compare→evaluate replan loop:

```
Plan → Dispatch → Observe → Compare (WorldStateDiff) → Replan?
```

### Record

```csharp
public sealed record WorldStateDiff(
    IReadOnlyDictionary<string, int>? InventoryGained,
    IReadOnlyDictionary<string, int>? InventoryLost,
    IReadOnlyDictionary<string, int>? ActualInventoryDelta,
    Position? ExpectedPosition,
    Position? ActualPosition,
    int HealthDelta,
    IReadOnlyList<string>? NewThreats,
    string OutcomeSummary);
```

### Key Properties

| Property | Logic |
|----------|-------|
| `HasMismatch` | `HasInventoryMismatch` OR `HasPositionMismatch` OR `HealthDelta < 0` OR `NewThreats.Count > 0` |
| `HasInventoryMismatch` | Expected gains not met, expected losses not found, OR unexpected changes detected (Sprint 59 TSK-0344) |
| `HasUnexpectedChanges` | Inventory items changed that were not in expected gains/losses |

---

## 14. Tags

- `event-system`
- `world-state`
- `action-correlation`
- `inventory-sourcing`
- `ProcessEventsAsync`
- `WorldStateProjector`
- `WebSocketBridge`
- `pending-action-lifecycle`
- `game-mode-detection`
- `entity-observation`
- `damage-interrupt`
- `sprint-55`
- `reference`
