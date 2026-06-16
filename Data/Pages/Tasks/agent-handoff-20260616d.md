# Agent Handoff — MemorySmith.Agent

**For:** Next agent session continuing MemorySmith.Agent development  
**From:** Session 2026-06-16 (fourth session of the day)  
**Repo:** https://github.com/TheMasonX/MemorySmith.Agent  
**Last CI-green commit:** `8e179d241d088ba88c2d2af0824e10b9b2f68b53` (Phase 4a)  
**Phase 4b commits:** pushed, CI pending (expect green — no breaking changes to existing tests)

---

## Suggested Skills

- **improve-codebase-architecture** — apply before and after every significant change
- **handoff** — when completing a major milestone, produce a fresh handoff for the next agent
- **MemorySmith Council Review** — 6-seat structured review; run after every major set of changes; commit to `Data/Pages/council/`

---

## What Was Completed This Session (Phase 4b)

### TSK-0011 — Blueprint Construction System (CI pending at handoff time)

**13 code files changed/created + 2 csproj updates + 5 doc/wiki files**

#### Quick Fixes (D1, D2, D3 from TSK-0010 deferred items):

**D1 — GoalFactory.RegisteredGoals** now returns dynamic prefix patterns:
```
["GatherWood", "SurviveNight", "GatherItem:{itemId}", "Build:{blueprintId}"]
```

**D2 — IItemSpecGoal marker interface** (`Agent.Core/Interfaces/IItemSpecGoal.cs`)
- `GenericGatherGoal` now implements `IItemSpecGoal`
- `HtnPlanner` dispatches on `goal is IItemSpecGoal` instead of `goal is GenericGatherGoal`
- Future ItemSpec-based goals (CraftItemGoal etc.) get planner support for free

**D3 — Null-registry diagnostic warnings** in `GoalFactory.CreateAsync`:
- `Debug.WriteLine` warning when `IItemRegistry` is null for GatherItem goals
- `Debug.WriteLine` warning when `IBlueprintRepository` is null for Build goals
- Returns null gracefully (no exception)

#### New Construction System:

| File | Purpose |
|------|----------|
| `Agent.Construction/BlueprintSchema.cs` | Added `PlacementBlock(X,Y,Z,BlockId)` record |
| `Agent.Construction/BlueprintParser.cs` | Wiki markdown → (Blueprint, IReadOnlyList\<PlacementBlock\>) |
| `Agent.Construction/Interfaces/IBlueprintExecutor.cs` | Interface for block-list → ActionData conversion |
| `Agent.Construction/BlueprintExecutor.cs` | Emits PlaceBlock actions, Y-ascending order |
| `Agent.Planning/Goals/BuildGoal.cs` | New goal: phases GatherMaterials/Build/Verify |
| `Agent.Planning/GoalFactory.cs` | Build: prefix, D1, D3 |
| `Agent.Planning/HtnPlanner.cs` | BuildGoal branch, IItemSpecGoal interface dispatch |
| `Agent.Planning/HtnTaskLibrary.cs` | DecomposeBuild method |
| `Agent.Memory/MemorySmithBlueprintRepository.cs` | Wiki-backed IBlueprintRepository |

#### Project Reference Changes:
- `Agent.Planning.csproj` → added `Agent.Construction` reference
- `Agent.Memory.csproj` → added `Agent.Construction` reference
- `MemorySmith.Agent.Tests.csproj` → added `Agent.Construction` reference

#### Small House Blueprint:
- `Data/Pages/blueprints/small-house.md` — 9W×5H×7D house
- Door, walls, oak log corners, glass pane windows, 2 torches, crafting table, double chest, bed, slab roof
- Encoded as 5 Y-layer grids with symbol legend

#### Item Registry Pages (9 new):
`cobblestone`, `oak-planks`, `glass-pane`, `torch`, `crafting-table`, `chest`, `oak-slab`, `oak-door`, `red-bed`

#### Tests (+60 new, total ~150):
- `BuildGoalTests.cs` — 14 tests
- `BlueprintParserTests.cs` — 18 tests
- `GoalFactoryBuildTests.cs` — 14 tests
- `HtnPlannerBuildTests.cs` — 14 tests

---

## Architecture State (after Phase 4b)

```
Agent.Core:
  WorldState, WorldEvent, Position, ActionData, ActionPlan
  IGoal, IItemSpecGoal (NEW), IItemRegistry, IBlueprintRepository*
  IWorldAdapter, ITool, IToolCaller, IPlanner, IMemoryGateway, IPlan
  WorldStateProjector, ItemSpec

  * IBlueprintRepository lives in Agent.Construction (not Agent.Core)
    because Blueprint model is defined there.

Agent.Memory:
  RestMemoryGateway
  MemorySmithItemRegistry
  MemorySmithBlueprintRepository (NEW)

Agent.Planning:
  HtnPlanner (updated: IItemSpecGoal dispatch + BuildGoal branch)
  HtnTaskLibrary (updated: DecomposeBuild added)
  GoalFactory (updated: D1, D3, Build: prefix)
  Goals: GatherWoodGoal, SurviveNightGoal, SimpleGoal
         GenericGatherGoal (updated: implements IItemSpecGoal)
         BuildGoal (NEW)

Agent.Construction:
  Blueprint, MaterialEntry, Dimensions, PlacementBlock (NEW)
  BlueprintParser (NEW)
  IBlueprintExecutor (NEW), BlueprintExecutor (NEW)
  IArchitect (interface, not implemented)
  IBlueprintRepository (interface)

Agent.Tools:
  PlaceBlockTool, MineBlockTool, MoveToTool, WanderTool, StatusTool
  SearchMemoryTool, CreatePageTool, GetPageTool
  ToolDispatcher, ToolRegistry, ActionProtocol

Agent.World.Minecraft:
  MinecraftAdapter, WebSocketBridge
  MineflayerAdapter/ (Node.js subprocess)
```

---

## Immediate Work for Next Agent

### 1. Verify CI green

Check CI on the last Phase 4b commit. Expected: all green (no regressions).
If any test fails, run check-runs/annotations to identify the failing test.

### 2. Set build origin before live testing

Before running a real Build:small-house goal, inject origin facts into WorldState:
```
build:small-house:origin:x = <target world X>
build:small-house:origin:y = <target world Y (ground level)>
build:small-house:origin:z = <target world Z>
```
This is currently done by setting WorldState.Facts directly. A REST endpoint or `SetBuildOriginTool` would improve UX (Phase 5 task P5-11).

### 3. Phase 5 priority items (from phase4b-tasks.md)

| Priority | Item | Description |
|----------|------|-------------|
| P1 | CraftItemTool | Craft planks, slabs, doors, torches, chests, beds from raw logs + materials |
| P1 | FurnaceTool | Smelt sand → glass, iron ore → iron ingot |
| P2 | SetBuildOriginTool | REST endpoint or tool to set world-space build origin |
| P2 | BlueprintChunker | Split large blueprints (330+ actions) into multi-plan segments |
| P3 | Door/bed facing args | Pass facing direction to PlaceBlock for correct door/bed orientation |
| P3 | IBlueprintExecutor injection | Inject executor into HtnTaskLibrary for testability |
| P4 | LLM-driven CreatePage | GoalFactory fallback for unknown item IDs |

### 4. Live Minecraft test sequence

When connected to a Minecraft server, test in this order:
1. `GatherItem:cobblestone` with count=63 — verifies gather pipeline
2. `GatherItem:oak_log` with count=50 — verifies tree chopping
3. Manually craft: planks, slabs, door, torches, crafting table, chests, bed
4. Set build origin facts in WorldState
5. `Build:small-house` — triggers DecomposeBuild → PlaceBlock sequence
6. Observe construction execution

---

## Key ADR Constraints (unchanged)

- D-002: MemorySmith is the memory backend
- D-003: Deterministic-first planning; LLM only for novel/unknown goals
- D-006: Blueprints are MemorySmith wiki pages
- D-007: .slnx solution format
- D-008: Node.js for Mineflayer
- D-010: ActionProtocol constants; WebSocket forwards wire names as-is

---

## Known Gaps / Bugs

- Build origin defaults to (0,0,0) if facts not set — house builds at world origin
- Crafted items (planks, slabs, etc.) must be pre-crafted; agent doesn't auto-craft
- Glass panes require manual smelting (FurnaceTool not yet implemented)
- Door and bed facing depend on bot yaw at placement time, not blueprint spec
- Large blueprints (~330 actions) have no resume capability if connection drops
- Node.js mine loop doesn't read block variants from ActionData arguments yet

---

## Encoding Reminder

When pushing files via `github__create_or_update_file`, pass plain text content (NOT base64).
The MCP tool handles encoding. See existing memory for full details.