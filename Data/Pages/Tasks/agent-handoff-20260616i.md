# Agent Handoff — MemorySmith.Agent

**For:** Next agent session
**From:** Session 2026-06-16 (ninth session — AGENTS.md + Sprint 2 end-to-end build)
**Repo:** https://github.com/TheMasonX/MemorySmith.Agent
**CI:** GREEN on cdc0d18 (B1 fix, conclusion: pending at handoff time — was green before B1)

---

## What Was Done This Session

### Preparation
- User directive: all timeouts/TTLs/retry counts/search radii must be configurable or named — no magic numbers
- Created `AGENTS.md` at repo root with full coding guidelines for AI contributors

### Sprint 2 — End-to-End Build

#### 2a — CraftItemTool Pathfinding (`MineflayerAdapter/index.js`)
- All JS magic numbers extracted to named constants at top of file:
  `CRAFT_TABLE_SEARCH_RADIUS = 8`, `CRAFT_TABLE_REACH_DISTANCE = 2`,
  `MINE_SEARCH_RADIUS_NEAR/FAR`, `MAX_MINE_PATH_FAILURES`,
  `FURNACE_SEARCH_RADIUS/REACH_DISTANCE`, `SMELT_TIMEOUT_MS`
- `craft` case now pathfinds to crafting_table before `bot.craft()` (for requiresTable recipes)
- Block re-fetched after navigation; `tableSearchRadius` overridable per-call via `args`

#### 2b — DecomposeBuild Crafting Chain (`Agent.Planning/HtnTaskLibrary.cs`)
- `TorchesPerCraft = 4` named constant
- `CraftingChainOrder` static list: planks → table → slab → door → chest
- `RequiresCraftingTable` static set: slab, door, chest (items needing 3×3 recipes)
- `BuildCraftingChain` private method emits CraftItem in dependency order
- `EmitCraftIfNeeded` helper: skips if not in blueprint or inventory sufficient
- **B1 fix**: if any `RequiresCraftingTable` item is in blueprint but `crafting_table` not listed,
  auto-emits `CraftItem(crafting_table, 1)` as a preparatory step
- Torch: explicit coal gather step + `CraftItem(stick, N)` intermediate before `CraftItem(torch, N)`
- 12 new tests in `HtnTaskLibraryCraftingTests.cs`

#### 2c — IItemRegistry TTL Cache (`Agent.Memory/`)
- `RestMemoryGatewayOptions.ItemCacheTtlSeconds = 60` (configurable via `Agent:Memory:ItemCacheTtlSeconds`)
- `MemorySmithItemRegistry` constructor now requires `(IMemoryGateway, RestMemoryGatewayOptions)`
- `ConcurrentDictionary<string, (ItemSpec?, DateTimeOffset Expires)>` cache keyed by slug
- Null results cached; `ItemCacheTtlSeconds = 0` disables caching for test isolation
- `Program.cs` updated: DI passes `RestMemoryGatewayOptions` to `MemorySmithItemRegistry`
- 4 new cache tests in `ItemRegistryTests.cs` (plus fixed constructor for existing tests)

**Council review:** `Data/Pages/council/sprint2-impl-council-20260616.md`
**CI green (Sprint 2 initial):** `7517e8e`
**CI green (B1 fix):** `cdc0d18` (pending at handoff)

---

## Current Architecture (updated)

```
AgentBackgroundService (Sprint 1):
  ExecuteAsync → retry loop (default 2/4/8/16/32s)
  ProcessEventsAsync → chat events written to _chatChannel
  ChatConsumerAsync → HandleChatEventAsync off the event loop
  DispatchActionsAsync → action queue + planner loop

HtnTaskLibrary.DecomposeBuild (Sprint 2b):
  Phase 1: gather raw materials (DirectMineBlocks + coal for torches)
  Phase 2: BuildCraftingChain (CraftItem actions in dependency order)
            B1: auto-craft table if slab/door/chest needed
  Phase 3: navigate to build site
  Phase 4: PlaceBlock actions
  Phase 5: GetStatus

MemorySmithItemRegistry (Sprint 2c):
  ConcurrentDictionary TTL cache (ItemCacheTtlSeconds from RestMemoryGatewayOptions)
  Null results cached to prevent repeated misses

MineflayerAdapter/index.js (Sprint 2a):
  All tunable constants at top; craft case pathfinds to crafting_table
```

---

## AGENTS.md key rules

1. **No magic numbers** — named constants or configurable options for all timeouts, TTLs,
   radii, retry counts
2. `*Options` classes must be `sealed record` (for `with {}` in tests)
3. `using` directives BEFORE file-scoped `namespace` in test files
4. No fully-qualified type names in `MemorySmith.Agent.Tests` namespace
5. JS: declare all constants at top of file, optional `args` override

---

## Next Sprint: Sprint 3 — Typed Events + FindFlatAreaTool

### 3a — Typed World Events (HIGH)
Replace `WorldEvent.Payload = Dictionary<string, object?>` with a sealed class hierarchy.

New types in `Agent.Core/Events/`:
```csharp
public abstract record WorldEvent(DateTimeOffset Timestamp);
public sealed record SpawnEvent(Position Pos, int Health, int Food) : WorldEvent(...);
public sealed record HealthEvent(int Health, int Food) : WorldEvent(...);
public sealed record MoveEvent(Position Pos) : WorldEvent(...);
public sealed record BlockMinedEvent(string Block, int Count, Position Pos) : WorldEvent(...);
public sealed record ChatEvent(string Username, string Message, int OnlinePlayers, Position? PlayerPos) : WorldEvent(...);
public sealed record ErrorEvent(string Action, string Message) : WorldEvent(...);
public sealed record CraftCompleteEvent(string Item, int Count) : WorldEvent(...);
public sealed record SmeltCompleteEvent(string Input, string Result, int Count) : WorldEvent(...);
public sealed record DeathEvent(Position Pos) : WorldEvent(...);
public sealed record StatusEvent(Position Pos, int Health, int Food, IReadOnlyDictionary<string,int> Inventory) : WorldEvent(...);
```

Update `WebSocketBridge.ParseEvent` to return typed events.
Update `WorldStateProjector.Apply` to use pattern matching on typed events.
Update `AgentBackgroundService.ProcessEventsAsync` switch to use typed events.
Remove all string-key dictionary lookups.

### 3b — FindFlatAreaTool (MEDIUM)
New tool: `FindFlatArea` — scans terrain and auto-sets build origin.
In `index.js`: scan grid within radius, find flattest NxM area matching blueprint dimensions.
In C# tool: dispatch `FindFlatArea`, handle `flatAreaFound` event, call `agent.SetBuildOrigin(...)`.
Wire into `HtnTaskLibrary.DecomposeBuild`: emit `FindFlatArea` before `MoveTo` when no origin facts.

---

## Process Reminders

- Each sprint: implement → push → CI green → council review → fix blockers → next sprint
- Council review: 6 seats, written to `Data/Pages/council/<topic>-council-<date>.md`
- GitHub MCP: `github__create_or_update_file` per-file, plain text, existing files need blob SHA
- AGENTS.md is now at repo root — keep it updated when new conventions are established
