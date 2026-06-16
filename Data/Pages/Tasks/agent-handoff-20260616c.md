# Agent Handoff — MemorySmith.Agent

**For:** Next agent session continuing MemorySmith.Agent development  
**From:** Session 2026-06-16 (third session of the day)  
**Repo:** https://github.com/TheMasonX/MemorySmith.Agent  
**Last green CI commit:** `8e179d241d088ba88c2d2af0824e10b9b2f68b53` (build-and-test ✅)  
**Last docs commit:** `1e028c978004f210d69c8227b0ad2eb6a95df9c7` (council review, no CI re-run needed)

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
- Council reviews: `Data/Pages/council/` (5 completed as of this session)
- Task tracking: `Data/Tasks/` (TSK-0001 through TSK-0010)
- Wiki home: `Data/Pages/home.md`

---

## What Was Completed This Session

### TSK-0010 Phase 4a — GenericGatherGoal + ItemRegistry (CI-verified, council-reviewed)

**Implementation commits:** 68b47a7 → 8e179d2 (13 code files + council review, ~30 commits total due to encoding fix)  
**Council review:** `Data/Pages/council/tsk-0010-impl-council-20260616.md`

**New files:**
- `Agent.Core/ItemSpec.cs` — sealed record: ItemId, DisplayName, SourceBlocks (IReadOnlyList<string>), RequiresSmelting, MinHarvestLevel. LegacyBlockIds omitted per council blocker fix.
- `Agent.Core/Interfaces/IItemRegistry.cs` — `Task<ItemSpec?> GetAsync(string, CancellationToken)`
- `Agent.Memory/MemorySmithItemRegistry.cs` — direct page lookup (slug: underscore→hyphen), search fallback (query `"item-registry/{itemId}"`), `public static ParseItemSpec(string)` front-matter parser
- `Agent.Planning/Goals/GenericGatherGoal.cs` — `Name = "Gather:{itemId}"`, `Spec` property exposed, IsComplete sums SourceBlocks (non-smelting) or checks product (smelting)
- `Data/Pages/item-registry/oak-log.md`, `iron-ore.md`, `diamond.md` — vanilla seed pages
- `MemorySmith.Agent.Tests/GenericGatherGoalTests.cs` — 20 tests
- `MemorySmith.Agent.Tests/ItemRegistryTests.cs` — 8 tests
- `MemorySmith.Agent.Tests/ItemSpecParserTests.cs` — 14 tests

**Modified files:**
- `Agent.Planning/HtnTaskLibrary.cs` — OakLogSpec constant; `GatherItemDecompose(ItemSpec, ...)` private static; `GatherWoodDecompose` delegates to it; `DecomposeGatherItem(ItemSpec, ...)` public method
- `Agent.Planning/HtnPlanner.cs` — `else if (goal is GenericGatherGoal gg)` branch → `library.DecomposeGatherItem(gg.Spec, [], state)`
- `Agent.Planning/GoalFactory.cs` — optional `IItemRegistry?` constructor param; `CreateAsync` handles `"GatherItem:{itemId}"`; sync `Create` unchanged

**GatherWoodGoal backward compat:** Unchanged. `GoalFactory.Create("GatherWood")` still returns `GatherWoodGoal`; `HtnTaskLibrary` GatherWood decomposition now delegates internally to `GatherItemDecompose(OakLogSpec, ...)`.

---

## Architecture State

```
Agent.Core:
  WorldState, WorldEvent, Position, ActionData, ActionQueue
  IGoal, IWorldAdapter, ITool, IToolCaller, IPlanner, IMemoryGateway
  WorldStateProjector (pure event projector)
  ItemSpec (NEW — data model for item acquisition)
  IItemRegistry (NEW — interface for item lookup)

Agent.Memory:
  RestMemoryGateway (calls MemorySmith REST /api/search, /api/pages)
  MemorySmithItemRegistry (NEW — wiki-backed IItemRegistry with front-matter parser)

Agent.Planning:
  HtnPlanner (updated — GenericGatherGoal branch via type check)
  HtnTaskLibrary (updated — OakLogSpec + GatherItemDecompose + DecomposeGatherItem)
  GoalFactory (updated — IItemRegistry param + CreateAsync)
  Goals: GatherWoodGoal, SurviveNightGoal, SimpleGoal, GenericGatherGoal (NEW)
  HtnTask.cs (tombstone)

Agent.Tools:
  ToolDispatcher, ToolRegistry, ToolEngine (shim)
  ActionProtocol (wire-name constants)
  Tools: MineBlockTool, MoveToTool, WanderTool, StatusTool, PlaceBlockTool,
         SearchMemoryTool, CreatePageTool, GetPageTool

Agent.World.Minecraft:
  MinecraftAdapter, WebSocketBridge
  MineflayerAdapter/ (Node.js subprocess)

WebUI.Blazor:
  AgentBackgroundService (uses WorldStateProjector + Channel<string> error signal)
  Program.cs, REST endpoints, about.html

Data/Pages/item-registry/:
  oak-log.md, iron-ore.md, diamond.md (NEW — vanilla seed pages)
```

---

## Immediate Work for Next Agent

### 1. Phase 4b deferred items (pick from list)

The following were deferred from TSK-0010 Phase 4a and are the natural next steps:

**D1 (easy, 10 min):** GoalFactory.RegisteredGoals should surface "GatherItem:" prefix capability. Currently returns only ["GatherWood", "SurviveNight"].

**D3 (easy, 15 min):** GoalFactory.CreateAsync should log/warn when itemRegistry is null and a GatherItem goal is requested, rather than silently returning null.

**D4 (medium, 30 min):** AgentBackgroundServiceTests — add test for error-channel path (blockNotFound event → `_gameErrors.Reader.TryRead` → `_consecutiveFailures++`). Deferred from prior session.

**D2 (medium, 1 hr):** `IItemSpecGoal` marker interface — add before a second type-checked goal is introduced in HtnPlanner. Prevents the `goal is ConcreteType` pattern from spreading.

**D6 (large, Phase 4b):** FurnaceTool + smelting chain. GenericGatherGoal.IsComplete already handles `RequiresSmelting=true` correctly — it just can't DRIVE the smelting yet.

### 2. HtnTask.cs tombstone deletion

Waiting for a token with `workflow` scope. Can be done via GitHub UI: delete `Agent.Planning/HtnTask.cs`. The file is already a comment-only tombstone.

### 3. Future: Phase 4b

- Node.js mine loop reading block variants from ActionData arguments (requires running Minecraft server)
- LLM-driven `CreatePage` for unknown item IDs (GoalFactory fallback after registry miss)
- `FurnaceTool` + smelting chain automation

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

## Encoding Lesson (Important!)

When pushing files via `github__create_or_update_file` MCP action, pass plain text content in the `content` field (NOT base64). The MCP tool handles base64 encoding before sending to GitHub. If you base64-encode first and pass that, files will be stored double-encoded and compile with "Expected expression at line 1" errors. Always use `open(path, 'r').read()` (text mode), not `base64.b64encode(open(path, 'rb').read()).decode()`.

---

## Immediate Start Checklist

```
[ ] Read this handoff
[ ] Decide which Phase 4b deferred item to tackle first (recommend D1 + D3 as warm-up)
[ ] Implement, commit, CI green
[ ] Run council review if change is significant
[ ] Write fresh handoff
```
