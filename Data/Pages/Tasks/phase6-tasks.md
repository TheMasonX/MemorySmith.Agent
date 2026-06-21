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

## Sprints 9–13 (COMPLETE ✅)

**Council:** `Data/Pages/council/sprint9-pre-council-20260617.md` through `sprint13-council-20260617.md`

Summary:
- Sprint 9: Flat-area scanner depth (Vec3 fix, scoring, slope, liquid, BuildFactKeys, auto-origin)
- Sprint 10: Build robustness (GroupBy+Sum, checkpoint resume, AGENTS.md rewrite)
- Sprint 11: Chat observability (CraftRegex, LLM timeout, thinking indicator, intent log, requireOrigin)
- Sprint 12: Bug fixes (ActionQueue thread-safety, response queue ordering, LLM hard timeout)
- Sprint 13: CraftItemGoal, built-in gather fallback, TryGetIntFact, GoalNamesMatch, recovery rate-limiter

---

## Sprint 14 — Material Pre-gather & Block Constant Unification (COMPLETE ✅)

**Branch:** `sprint-5-tool-safety` (PR #1)  
**Council:** `Data/Pages/council/sprint14-council-20260617.md`

| ID | Task | File | Status |
|----|------|------|--------|
| P0 | `DecomposeCraftItem`: pre-gather iron_ore + smelt for iron tools; mine stone for stone tools | `HtnTaskLibrary.cs` | ✅ Done |
| P0-test | `HtnPlanner_CraftItemGoal_IronPickaxe_EmitsMineBlockAndSmelt` + 4 more iron/stone tests | `CraftItemGoalTests.cs` | ✅ Done |
| P1a | `Agent.Core/CommonMinecraftBlocks.cs` shared constant; both `DirectMineBlocks` refs point here | `CommonMinecraftBlocks.cs` (NEW) | ✅ Done |
| P1b | `WorldStateProjector.ApplyStatus`: normalize inventory keys (strip `"minecraft:"` prefix) | `WorldStateProjector.cs` | ✅ Done |
| P1b-test | `Apply_StatusEvent_NamespacedInventoryKey_IsNormalized` + 2 more | `WorldStateProjectorTests.cs` | ✅ Done |
| Audit | 4 external audit docs → `Data/Pages/Audit/` | `Audit/*.md` | ✅ Done |

---

## Sprint 15 — Mining Count Fix & Coal Pre-gather (COMPLETE ✅)

**CI commit:** Sprint 15 changes (fb569c0 + CI fix)  
**Council:** `Data/Pages/council/sprint15-council-20260617.md` — no blockers  

| ID | Task | File | Status |
|----|------|------|--------|
| P0-count | `WorldStateProjector.ApplyBlockMined` uses `e.Count` not hardcoded `1` | `WorldStateProjector.cs` | ✅ Done |
| P0-test | 2 new tests: multi-count BlockMined (count=5, count=64+namespaced) | `WorldStateProjectorTests.cs` | ✅ Done |
| P0-coal | `DecomposeCraftItem` pre-gathers coal_ore before SmeltItem when coal insufficient | `HtnTaskLibrary.cs` | ✅ Done |
| P0-coal-test | `EmitsCoalMine_WhenNoCoal` + `SkipsCoalMine_WhenCoalPresent` | `CraftItemGoalTests.cs` | ✅ Done |
| P1-recovery | `TryCompleteCurrentGoalFromWorldUpdate` resets `_lastRecoveredGoalName = null` | `AgentBackgroundService.cs` | ✅ Done |
| P1-stall | Stall detection (10s warn, 30s suppress) | `AgentBackgroundService.cs` | ✅ Done |
| Audit | 3 new audit docs + 6-seat synthesis of all 7 audits | `Audit/*.md`, `audit-synthesis-council-20260617.md` | ✅ Done |

---

## Sprint 16 — Planner Routing Docs & Knowledge Resolver Stub (COMPLETE ✅)

**CI commit:** `a420cd9` (dead-code fix — green)  
**Council:** `Data/Pages/council/sprint16-council-20260617.md` — no blockers  
**Branch:** `sprint-5-tool-safety` (PR #1)  

| ID | Task | File | Status |
|----|------|------|--------|
| P0-a | Annotate `PlannerRouter.cs` — [IMPLEMENTED]/[ASPIRATIONAL] XML docs on all enum values + Select() | `Agent.Planning/Router/PlannerRouter.cs` | ✅ Done |
| P0-b | Architecture inventory: `planner-routing-status-20260617.md` | `Data/Pages/Architecture/` (NEW) | ✅ Done |
| P1-a | `IKnowledgeResolver` interface + `KnowledgeQuery`, `KnowledgeResult`, `KnowledgeCandidate`, `CandidateType` | `Agent.Memory/IKnowledgeResolver.cs` (NEW) | ✅ Done |
| P1-b | `LocalKnowledgeResolver` stub — two sources (IItemRegistry + IMemoryGateway), lexical-first | `Agent.Memory/LocalKnowledgeResolver.cs` (NEW) | ✅ Done |
| P1-c | DI registration + `GET /api/agent/resolve?q=` endpoint | `WebUI.Blazor/Program.cs` | ✅ Done |
| P1-d | 8 unit tests: registry hit, smeltable, craftable, gateway fallback, TopN, threshold, type filter, ambiguity, empty | `MemorySmith.Agent.Tests/KnowledgeResolverTests.cs` (NEW) | ✅ Done |
| P2-b | Extract crafting-table bootstrap → `AddCraftingTableIfNeeded` helper | `Agent.Planning/HtnTaskLibrary.cs` | ✅ Done |

---

## Sprint 17 — ClassifySpec Fix & WorldFact Resolver Source (COMPLETE ✅)

**CI commit:** `05f9a6d` (AGENTS.md curl examples)  
**CI run:** 27721607397 (build-and-test: success)  
**Council:** `Data/Pages/council/sprint17-council-20260617.md` — no blockers, approved  
**Branch:** `sprint-5-tool-safety` (PR #1)  

| ID | Task | File | Status |
|----|------|------|--------|
| P0-classify | `ClassifySpec` checks `DirectMineBlocks.Contains(spec.ItemId) OR SourceBlocks.Contains(spec.ItemId)` | `Agent.Memory/LocalKnowledgeResolver.cs` | ✅ Done |
| P0-drops | Expand `DirectMineBlocks` with raw ore drops + emerald_ore/deepslate_emerald_ore | `Agent.Core/CommonMinecraftBlocks.cs` | ✅ Done |
| P1-enum | Add `CandidateType.WorldFact` to IKnowledgeResolver enum | `Agent.Memory/IKnowledgeResolver.cs` | ✅ Done |
| P1-source | LocalKnowledgeResolver step 4: WorldFact scan via Func accessor; confidence 0.70/0.50 | `Agent.Memory/LocalKnowledgeResolver.cs` | ✅ Done |
| P1-di | Wire WorldState factory delegate to resolver in DI | `WebUI.Blazor/Program.cs` | ✅ Done |
| Tests | 4 new tests: ClassifySpec_Diamond, ClassifySpec_OakLog, WorldFact_Match, WorldFact_OldFact | `MemorySmith.Agent.Tests/KnowledgeResolverTests.cs` | ✅ Done |
| D2 | SearchAsync raw-query comment in LocalKnowledgeResolver | `Agent.Memory/LocalKnowledgeResolver.cs` | ✅ Done |
| D3 | /api/agent/resolve curl examples + notes added to AGENTS.md | `AGENTS.md` | ✅ Done |

---

## Sprint 18 — Runtime Bug Fixes & House-Building MVP Unblock (COMPLETE ✅)

**CI commit:** `84cab34` (CI fix — remove SendEmergencyStop from SetGoal)  
**CI run:** 27727986513 (build-and-test: success)  
**Council:** `Data/Pages/council/sprint18-council-20260617.md` — no blockers, approved  
**Branch:** `sprint-5-tool-safety` (PR #1)  

| ID | Task | File | Status |
|----|------|------|--------|
| P0-floored | `toVec3(x,y,z)` helper fixes `pos.floored is not a function` in findFlatArea | `MineflayerAdapter/index.js` | ✅ Done |
| P0-stop | `case 'stop':` bypasses cmdQueue; `handleStop()` clears queue + stops pathfinder + sets `_stopRequested` | `MineflayerAdapter/index.js` | ✅ Done |
| P0-abort | `_stopRequested` checked in mine while loop, findFlatArea outer loop, wander | `MineflayerAdapter/index.js` | ✅ Done |
| P0-replan | `MinReplanIntervalSeconds = 2` guard; replan storm drops from 3x/sec to ≤ 0.5x/sec | `AgentBackgroundService.cs` | ✅ Done |
| P0-stop-c# | `SendEmergencyStop()` in `CancelGoal()` and `TryCompleteCurrentGoalFromWorldUpdate()` | `AgentBackgroundService.cs` | ✅ Done |
| P1-count | `GenericGatherGoal.TargetCount` public property; `GatherGoalDecomposer` passes count | `Goals/GenericGatherGoal.cs`, `Decomposition/GatherGoalDecomposer.cs` | ✅ Done |
| P2-config | Startup `=== Agent config ===` log (bot, LLM timeout, rate limits, memory URL) | `WebUI.Blazor/Program.cs` | ✅ Done |
| CI-fix | Removed `SendEmergencyStop()` from `SetGoal()` — preserves test's `SentActions` invariant | `AgentBackgroundService.cs` | ✅ Done |
| Test plan | `Data/Pages/Guides/test-plan-mvp.md` — 7-phase house-building test plan | `Data/Pages/Guides/` (NEW) | ✅ Done |

### Deferred from Sprint 18

| ID | Finding | Target |
|----|---------|--------|
| D1 | MinReplanInterval=2s adds latency for fast actions; make configurable | Sprint 19 |
| D2 | Goal change via TryCreateGoalFromChatAsync doesn't stop old mining | Sprint 19 |
| D3 | No test for gather count fix (GatherGoalDecomposer count pass-through) | Sprint 19 |
| D6 | MemorySmith wiki not deployed — SearchMemory returns empty; blueprint reading blocked | Sprint 19 P0 |

---

## Phase 7 — Updated Roadmap (post Sprint 18)

| Sub-phase | Focus | Sprint estimate |
|-----------|-------|----------------|
| **7-A (done)** | Architecture inventory; planner routing cleanup | Sprint 16 ✅ |
| **7-B (done)** | Resolver growth: ClassifySpec fix + WorldFact source | Sprint 17 ✅ |
| **7-C (next)** | Observation pipeline normalization (ObservationNormalizer) | Sprint 19 |
| 7-D | Belief layer + IBeliefState | Sprint 20 |
| 7-E | Episodic memory + IEpisode | Sprint 21 |
| 7-F | Planner input migration to world model + beliefs | Sprint 22 |
| 7-G | Reflection service | Sprint 23 |
| 7-H | Page synthesis from memory clusters | Sprint 24 |
| 7-I | Adapter generalization audit | Sprint 25 |
