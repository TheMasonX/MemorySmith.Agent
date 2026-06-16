# Agent Handoff — MemorySmith.Agent

**For:** Next agent session continuing MemorySmith.Agent development  
**From:** Session ending 2026-06-16 (second session of the day)  
**Repo:** https://github.com/TheMasonX/MemorySmith.Agent  
**Last green CI commit:** `ace0872248763d51c79cac40759e765d1541269a` (design docs only, build-and-test ✅ on `90b2cc8a`)

> Note: CI ran green on `90b2cc8a` (last code-change commit). The final commits after that (`b1397d17`, `568f8bd2`, `ace0872`) are docs/pages only — no C# changes, CI passes by default.

---

## Suggested Skills

- **improve-codebase-architecture** — apply before and after every significant change
- **handoff** — when completing a major milestone, produce a fresh handoff for the next agent
- **MemorySmith Council Review** — 6-seat structured review; run after every major set of changes; commit to `Data/Pages/council/`

---

## Project Context

MemorySmith.Agent is a modular autonomous Minecraft agent backed by the MemorySmith wiki as long-term memory. Three bounded contexts: **Agent Core** (planning, tools, goals), **MemorySmith Knowledge** (RestMemoryGateway → MemorySmith REST API), **World** (MinecraftAdapter + Node.js/Mineflayer).

Key docs:
- Architecture: `Data/Pages/architecture.md`
- Decisions: `Data/Pages/decisions.md` (ADR-001 through ADR-010)
- Council reviews: `Data/Pages/council/` (4 completed as of this session)
- Task tracking: `Data/Tasks/` (TSK-0001 through TSK-0010)
- Wiki home: `Data/Pages/home.md`

---

## What Was Completed This Session

### Refactoring Candidates 3–5 (all CI-verified, council-reviewed)

**Candidate 3 — WorldStateProjector extraction (commits `bd85979`, `36feb05`, `08147ac`)**

- Created `Agent.Core/WorldStateProjector.cs`: pure stateless class, `Apply(WorldState, WorldEvent) → WorldState`. Handles all 6 event types. Does NOT write `game.lastError`.
- `WebUI.Blazor/AgentBackgroundService.cs`: `ProcessEventsAsync` is now a 30-line loop (was 70). Three private state-mutation helpers removed. `Channel<string> _gameErrors` replaces the stringly-typed `game.lastError` fact. `DispatchActionsAsync` reads from the channel after settle delay.
- 15 `WorldStateProjectorTests` covering all event types, purity, raw-fact storage, `game.lastError` not written.

**Candidate 4 — HtnTask.cs tombstone (commits `c0df06c`, `9813510`)**

- `TaskDecomposer` delegate moved into `HtnTaskLibrary.cs` (its only consumer).
- `HtnTask.cs` replaced with namespace + comment tombstone. `HtnTask` record was never instantiated.
- `Phases` kept on `GatherWoodGoal` and `SurviveNightGoal` (part of `IGoal` interface; used by `HtnPlanner` for non-direct-decomposition goals; serves as documentation).
- ADR-009 added to `Data/Pages/decisions.md`.

**Candidate 5 — ActionProtocol constants (commits `20401ee` through `8aab71f`)**

- New `Agent.Tools/ActionProtocol.cs`: 6 constants (`move`, `mine`, `place`, `status`, `wander`, `chat`).
- `MineBlockTool`, `MoveToTool`, `WanderTool`, `StatusTool`, `PlaceBlockTool`: each updated to use `ActionProtocol.X` instead of a string literal.
- `WebSocketBridge.SendAsync`: removed `action.Tool.ToLowerInvariant()`. Tools are now the single source of truth for wire names.
- ADR-010 added to `Data/Pages/decisions.md`.

**Council review** (`Data/Pages/council/phase3-refactor-candidates-council-20260616.md`): all three accepted, no blocking findings. Two deferred: tombstone full deletion, error-channel integration test in `AgentBackgroundServiceTests`.

### TSK-0010 Design (design-only, no code yet)

- Design doc committed: `Data/Pages/Tasks/tsk-0010-generic-gather-goal-design.md`
- Design council committed: `Data/Pages/council/tsk-0010-design-council-20260616.md`
- **Decision: design accepted with one blocker fixed pre-implementation** (remove `LegacyBlockIds` from MVP `ItemSpec`).

---

## Immediate Work for Next Agent

### 1. Implement TSK-0010 Phase 4a

All design decisions are made. The design council has resolved all open questions. Read:
- `Data/Pages/Tasks/tsk-0010-generic-gather-goal-design.md` (full design)
- `Data/Pages/council/tsk-0010-design-council-20260616.md` (resolutions + acceptance criteria)

**6 acceptance criteria before writing any code:**

```
[ ] LegacyBlockIds field OMITTED from ItemSpec MVP (not nullable, just absent)
[ ] IReadOnlyList<string> used for SourceBlocks (not string[])
[ ] MemorySmithItemRegistry returns null for unknown pages — never calls LLM
[ ] ItemSpecParserTests added (unit tests against raw markdown strings)
[ ] Seed wiki pages use agreed front-matter schema
[ ] GatherWoodGoal kept as factory alias pointing to GenericGatherGoal
```

**Files to create (in dependency order):**

1. `Agent.Core/ItemSpec.cs` — record with `ItemId`, `DisplayName`, `SourceBlocks`, `RequiresSmelting`, `MinHarvestLevel`
2. `Agent.Core/Interfaces/IItemRegistry.cs` — `Task<ItemSpec?> GetAsync(string itemId, CancellationToken)`
3. `Agent.Memory/MemorySmithItemRegistry.cs` — searches wiki for `item-registry/{itemId}`, parses front-matter
4. `Agent.Planning/Goals/GenericGatherGoal.cs` — sums source blocks or checks smelted product per `RequiresSmelting`
5. `Agent.Planning/HtnTaskLibrary.cs` — add `GatherItemDecompose(ItemSpec, string[], WorldState)`; update `GatherWoodDecompose` to delegate to it
6. `Agent.Planning/GoalFactory.cs` — add `"GatherItem:{itemId}"` registration; `"GatherWood"` → alias
7. `Data/Pages/item-registry/oak-log.md`, `iron-ore.md`, `diamond.md` — seed wiki pages
8. `MemorySmith.Agent.Tests/GenericGatherGoalTests.cs` — 12+ tests
9. `MemorySmith.Agent.Tests/ItemRegistryTests.cs` — mock gateway, parser tests
10. `MemorySmith.Agent.Tests/ItemSpecParserTests.cs` — unit tests against markdown strings

**CI must be green after the implementation commit.**  
**Run a council review after the implementation.**

### 2. Deferred from this session (not blocking TSK-0010)

- `AgentBackgroundServiceTests`: add test for error-channel path (blockNotFound event → `_gameErrors.Reader.TryRead` → `_consecutiveFailures++`).
- `HtnTask.cs` tombstone: fully delete when a token with `workflow` scope is available (or via GitHub UI).

---

## Architecture State

```
Agent.Core:
  WorldState, WorldEvent, Position, ActionData, ActionQueue
  IGoal, IWorldAdapter, ITool, IToolCaller, IPlanner, IMemoryGateway
  WorldStateProjector (NEW — pure event projector)

Agent.Memory:
  RestMemoryGateway (calls MemorySmith REST /api/search, /api/pages)

Agent.Planning:
  HtnPlanner, HtnTaskLibrary (TaskDecomposer inside), GoalFactory
  Goals: GatherWoodGoal, SurviveNightGoal, SimpleGoal
  HtnTask.cs (TOMBSTONE — dead code removed, file kept as comment)

Agent.Tools:
  ToolDispatcher, ToolRegistry, ToolEngine (shim)
  ActionProtocol (NEW — wire-name constants)
  Tools: MineBlockTool, MoveToTool, WanderTool, StatusTool, PlaceBlockTool,
         SearchMemoryTool, CreatePageTool, GetPageTool

Agent.World.Minecraft:
  MinecraftAdapter, WebSocketBridge (no longer lowercases Tool)
  MineflayerAdapter/ (Node.js subprocess)

WebUI.Blazor:
  AgentBackgroundService (uses WorldStateProjector + Channel<string> error signal)
  Program.cs, REST endpoints, about.html
```

---

## Process Protocol

1. **Before any significant change:** Explore subagent audit, deletion test, surface deepening candidates
2. **After implementing each feature:** commit, wait for CI green
3. **After every 3-5 candidates or one major feature:** run full MemorySmith 6-seat council review, commit to `Data/Pages/council/`
4. **Design-first for new features**: design doc + design council BEFORE writing code
5. **Every session:** update `Data/Tasks/` JSON files, update memories, update roadmap
6. **Before finishing:** write fresh handoff doc to `Data/Pages/Tasks/`

---

## Key ADR-Level Constraints

- **D-002:** MemorySmith is the memory backend — no custom knowledge store
- **D-003:** Deterministic-first planning — LLM only for novel/unknown goals or failure recovery
- **D-006:** Blueprints are MemorySmith wiki pages
- **D-007:** `.slnx` solution format (not `.sln`)
- **D-008:** Node.js for Mineflayer (not .NET)
- **D-010:** ActionProtocol constants; WebSocketBridge forwards wire names as-is

---

## Immediate Start Checklist

```
[ ] Read Data/Pages/Tasks/tsk-0010-generic-gather-goal-design.md
[ ] Read Data/Pages/council/tsk-0010-design-council-20260616.md
[ ] Verify 6 acceptance criteria from design council (fix LegacyBlockIds before coding)
[ ] Implement ItemSpec.cs in Agent.Core
[ ] Implement IItemRegistry.cs + MemorySmithItemRegistry.cs
[ ] Implement GenericGatherGoal.cs
[ ] Update HtnTaskLibrary + GoalFactory
[ ] Create seed wiki pages (oak-log, iron-ore, diamond)
[ ] Add GenericGatherGoalTests + ItemRegistryTests + ItemSpecParserTests
[ ] CI green → commit council review
[ ] Write fresh handoff to Data/Pages/Tasks/
```
