# Phase 6 Tasks

**Tracking file:** `Data/Pages/Tasks/phase6-tasks.md`  
**Phase 6 start:** 2026-06-16 (session 8, after TSK-0014 refactor)

---

## Sprints 1–8 (COMPLETE ✅)

See previous entries in commit history. Key sprint council reviews in `Data/Pages/council/`.

Summary:
- Sprint 1: Reliability (non-blocking LLM + reconnect)
- Sprint 2: End-to-end build (crafting chain, TTL cache)
- Sprint 3: Typed events + FindFlatAreaTool
- Sprint 4: SignalR dashboard + chat history
- Sprint 5: Tool safety (schema validation, memory lifecycle)
- Sprint 6: Journal, WorldModel, PlannerRouter + decomposers
- Sprint 7: Chat reliability, observability APIs, Serilog fix
- Sprint 8: Correctness polish (WorldModel lock, JournalEntryDto, error recovery)

---

## Sprint 9 — Flat-Area Scanner Depth (COMPLETE ✅)

**CI commit:** `2427c0b` (green)  
**Pre-sprint council:** `Data/Pages/council/sprint9-pre-council-20260617.md`  
**End council:** `Data/Pages/council/sprint9-10-council-20260617.md`

| ID | Task | Commit | Status |
|----|------|--------|--------|
| Vec3-fix | `bot.blockAt({x,y,z})` — eliminated undefined Vec3 runtime error | `d491c8c` | ✅ Done |
| A1 | Vertical scan widened: +10/-16 blocks, named constants, per-call override | `d491c8c` | ✅ Done |
| A2 | Composite scoring: `FLAT_SCORE_WEIGHTS` named object (area 50%, compact 30%, flat 20%) | `d491c8c` | ✅ Done |
| A5 | Slope penalty: components with yRange > maxSlope=3 rejected before scoring | `d491c8c` | ✅ Done |
| Liquid | `LIQUID_BLOCK_NAMES` set — water/lava/flowing excluded from heightMap | `d491c8c` | ✅ Done |
| Yield | `setImmediate` every 200 columns — keeps Mineflayer event loop responsive | `d491c8c` | ✅ Done |
| BuildFactKeys | `Agent.Core/BuildFactKeys.cs` — named constants for all shared fact key strings | `c874c7d` | ✅ Done |
| A3 | AgentBackgroundService: `FlatAreaFoundEvent` → `SetBuildOrigin("auto",...)` when area≥25 | `ff34ccb` | ✅ Done |
| A3-htn | `HtnTaskLibrary.DecomposeBuild`: reads auto-origin from `BuildFactKeys` facts | `e38c91a` | ✅ Done |
| A4 | `WorldStateProjectorTests`: 2 new FlatAreaFoundEvent tests + BuildFactKeys.LastFlatArea assert | `08c56c0` | ✅ Done |
| WSP | `WorldStateProjector`: stores `BuildFactKeys.LastFlatArea` in FlatAreaFoundEvent case | `b6d211` | ✅ Done |
| S7-D3 | `/api/agent/worldmodel?detail=false` — lightweight summary mode | `130701e` | ✅ Done |
| Guides | `Data/Pages/Guides/running-the-agent.md` — quickstart, config, API, issues | `de68751` | ✅ Done |
| Guides | `Data/Pages/Guides/features-reference.md` — architecture, tools, chat, build | `2427c0b` | ✅ Done |
| Pre-council | `Data/Pages/council/sprint9-pre-council-20260617.md` | `c097a6b` | ✅ Done |

---

## Sprint 10 — Build Robustness & Matt Pocock Handoff (COMPLETE ✅)

**CI commit:** `866d637` (green — Phase 0 early-return fix)  
**Council:** `Data/Pages/council/sprint9-10-council-20260617.md`

| ID | Task | Commit | Status |
|----|------|--------|--------|
| D3 | `BuildCraftingChain`: `GroupBy+Sum` replaces `ToDictionary` — no more duplicate-key throw | `5119f75` | ✅ Done |
| B4 | `CraftingChainOrder` expanded: dark_oak/jungle/acacia/mangrove/cherry planks, stick, oak_stairs, fence, fence_gate, wooden/stone tools | `5119f75` | ✅ Done |
| B4 | `SmeltItem` step for `iron_ingot` in `DecomposeBuild` | `5119f75` | ✅ Done |
| B2 | `BuildFactKeys.BuildProgressIndex(string)` + context keys `PlaceBlockProgressBlueprintId/BlockIndex` | `6f553d2` | ✅ Done |
| B2 | `AgentBackgroundService.DispatchActionsAsync`: writes checkpoint fact on PlaceBlock success | `5709b23` | ✅ Done |
| B2 | `HtnTaskLibrary.DecomposeBuild`: reads `BuildProgressIndex` checkpoint, skips placed blocks | `866d637` | ✅ Done |
| B1-note | Phase 0 early-return removed (broke tests); preflight gating deferred to Sprint 11 after callsite audit | `866d637` | ✅ Done |
| AGENTS.md | Matt Pocock style rewrite: 7 rules, where-things-live, patterns, anti-patterns | `fd867f9` | ✅ Done |

---

## Sprints 11–13 (COMPLETE ✅)

**Council:** `Data/Pages/council/sprint11-council-20260617.md` through `sprint13-council-20260617.md`  
**CI commit:** `c6d913c` (green — GoalFactoryBuildTests fix, Sprint 13 final)

Summary:
- Sprint 11: Chat observability (CraftRegex, LLM timeout, thinking indicator, intent log, requireOrigin)
- Sprint 12: Bug fixes (ActionQueue thread-safety, response queue ordering, LLM hard timeout, CS0420, NUnit2058)
- Sprint 13: CraftItemGoal, built-in gather fallback for 22 blocks, TryGetIntFact JsonElement, GoalNamesMatch suffix fix, recovery rate-limiter

---

## Sprint 14 — Material Pre-gather & Block Constant Unification (IN PROGRESS 🔄)

**Branch:** `sprint-5-tool-safety` (PR #1)  
**Starting commit:** `70ffcf1` (Sprint 14 handoff doc)  
**Council:** `Data/Pages/council/sprint14-council-20260617.md` (pending)

| ID | Task | File | Status |
|----|------|------|--------|
| P0 | `DecomposeCraftItem`: pre-gather iron_ore + smelt for iron tools; mine stone for stone tools | `HtnTaskLibrary.cs` | ✅ Done |
| P0-test | `HtnPlanner_CraftItemGoal_IronPickaxe_EmitsMineBlockAndSmelt` + 4 more iron/stone tests | `CraftItemGoalTests.cs` | ✅ Done |
| P1a | `Agent.Core/CommonMinecraftBlocks.cs` shared constant; both `DirectMineBlocks` refs point here | `CommonMinecraftBlocks.cs` (NEW) | ✅ Done |
| P1b | `WorldStateProjector.ApplyStatus`: normalize inventory keys (strip `"minecraft:"` prefix) | `WorldStateProjector.cs` | ✅ Done |
| P1b-test | `Apply_StatusEvent_NamespacedInventoryKey_IsNormalized` + 2 more | `WorldStateProjectorTests.cs` | ✅ Done |
| Audit | 4 external audit docs → `Data/Pages/Audit/` | `Audit/*.md` | ✅ Done |
| tasks | phase6-tasks.md Sprint 14 row + Phase 7 direction section | `phase6-tasks.md` | ✅ Done |

---

## Deferred carry-forward

| ID | Finding | Target |
|----|---------|--------|
| D4 (S2) | `TorchesPerCraft = 4` hardcoded vanilla recipe | future IRecipeRegistry |
| D5 (S7) | Inventory × char in LLM prompt (benign, cosmetic) | Future |
| D6 (S1) | NUnit2058 warning in MockMemoryGatewayTests.cs | Sprint 15 cleanup |
| Atomic-origin | Three-fact auto-origin write is not atomic | Deferred (single-threaded safe for now) |
| Per-blueprint | `maxSlope`, `minArea` per-blueprint configuration | Sprint 15+ |
| Declarative-crafting | Fold `CraftingChainOrder` + `RequiresCraftingTable` into declarative table | Sprint 15+ |
| Typed-facts | `WorldState.Facts: Dictionary<string, object?>` — migrate to typed facts | Phase 7+ |
| B3 | Orientation-aware PlaceBlock: facing direction in action args | Sprint 15 |
| B5 | Clear-area action before building on slight slope | Sprint 15 |
| Stall | Warn if no action dispatched >10s with active goal | Sprint 15 |
| D2 (S2) | MemorySmithItemRegistry parallel miss race | Sprint 15 |
| Bubble | Add `bubble_column` to LIQUID_BLOCK_NAMES | Sprint 15 |
| D3 (S13) | `_lastRecoveredGoalName` not cleared on goal completion | Sprint 15 |

---

## Phase 7 — Cognition Substrate Direction (FUTURE)

**External audits persisted:** `Data/Pages/Audit/` (Sprint 14 intake, 2026-06-17)

Four independent external audits all converge on the same direction: pivot from "Minecraft bot with memory" to "persistent embodied intelligence platform." No audit conflicts with current Sprint work — they describe the next architectural layer above what Sprints 1–14 have been building.

### What the audits say

All four documents (see `Data/Pages/Audit/`) identify the same missing substrate:

| Gap | Audit consensus |
|-----|----------------|
| Observation pipeline | Adapter events are not normalized; planner consumes raw state |
| Belief layer | No stable interpreted state between observations and planning |
| Episodic memory | No experience records; no lesson capture |
| Memory graph | Memories are second-class; pages do memory's job |
| Reflection | No post-action evaluation or lesson writing |
| Project hierarchy | Vision → Project → Goal → Task → Action hierarchy absent |
| Adapter isolation | Minecraft semantics still leak into planner logic |

### Alignment with current work

Sprints 1–14 have built the execution layer correctly (deterministic HTN, safety gates, observability). Phase 7 builds the cognition layer above it. The audits explicitly preserve existing work:
- keep deterministic-first planning
- keep adapter isolation progress
- keep current tool dispatch
- add memory/observation/belief on top, not instead

### Phased roadmap (from Concrete Refactor Plan)

| Phase | Scope |
|-------|-------|
| Phase 7-A | Memory-first substrate: IMemoryStore, MemoryNode, MemoryEdge |
| Phase 7-B | Observation pipeline: typed observations, immutable, provenance |
| Phase 7-C | Belief + episodic memory: belief reconciliation, episode compression |
| Phase 7-D | World model depth: belief-driven projection, uncertainty |
| Phase 7-E | Planner migration: accept world model + beliefs, multi-scale split |
| Phase 7-F | Reflection + consolidation: post-action eval, lesson writing |
| Phase 7-G | Page synthesis: cluster → synthesis → page |
| Phase 7-H | Adapter generalization: Minecraft clearly one embodiment |

### When to start Phase 7

Start Phase 7-A after Sprint 14 council is clean and CI is green. The first task is **not** code — it is the architecture inventory described in Phase 0 of the Concrete Refactor Plan.
