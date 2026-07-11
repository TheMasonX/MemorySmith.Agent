# Sprint 44 — Next Agent Handoff (2026-06-23)

## Summary

Sprint 42 and 43 addressed the **placement hygiene** (terrain collision, checkpoint fidelity, timeout tuning) and **live gameplay regressions** (navigate, wool, origin warp, coordinate rounding). The bot can now place blocks more reliably with correct checkpoint tracking and terrain awareness.

The next phase must focus on **known correctness bugs** and **test coverage gaps** before resuming feature work. Three P0 items have been confirmed by 3+ audits and remain unfixed.

---

## What Sprint 43 Delivered ✅

| Fix | Files | Impact |
|---|---|---|
| **P0: Fast-path navigate** — LLM can no longer override "come here" → "cancel" | `LlmChatInterpreter.cs` | "Come here" now works reliably |
| **P0: Selective CancelGoal** — only StopNow for conflicting goals, not idle/wander | `AgentBackgroundService.cs` | Navigate doesn't clear queue unnecessarily |
| **P0: blockPlaceSkipped event** — terrain collisions don't advance checkpoint | `index.js`, `WorldEvents.cs`, `WebSocketBridge.cs`, `AgentBackgroundService.cs` | No permanent holes from terrain skip |
| **P1: Wool gathering** — 16 wool colors in DirectMineBlocks + ItemAliases | `CommonMinecraftBlocks.cs`, `IntentManager.cs` | "Gather 5 wool" now works |
| **P1: Proximity-gated MoveTo** — skip MoveTo(origin) if within 5 blocks | `HtnTaskLibrary.cs` | Bot stops running back to origin |
| **P1: Math.floor() for botPos** — fixes off-by-one coordinate rounding | `index.js` | Block positions now align with entity standing blocks |
| **P1: PlaceBlock timeout 2s → 5s** — matches Sprint 41 doc intent | `AgentBackgroundService.cs` | Fewer false timeouts |

---

## Council Reports Produced This Session

| Report | Location |
|---|---|
| Sprint 42 Placement Hygiene Council | `Data/Pages/council/sprint42-placement-hygiene-council-20260623.md` |
| Sprint 43 Live Gameplay Fixes Council | `Data/Pages/council/sprint43-live-gameplay-fixes-council-20260623.md` |
| Sprint 43 Post-Fix Status Synthesis | `Data/Pages/council/sprint43-post-fix-status-synthesis-20260623.md` |

---

## P0 Items — Must Fix Before Any New Feature Work

### P0-1: Smelt→CraftItem Routing Bug (7 sprints old)

**Evidence:** `SmeltCompleteEvent` exists, `case 'smelt':` handler works in `index.js`, but the C# planner routes `smelt` intent through `CraftItemGoal` → `CraftItemGoalDecomposer` → `CraftItemTool`. No furnace workflow is ever generated.

**Confidence:** 98% (3 separate audits confirm this)

**Suggested fix:** Either:
- Add `SmeltGoalDecomposer` that emits `SmeltItem` actions instead of `CraftItem`
- Or route `smelt` intent in `IntentManager` to a different goal type

**Files:** `Agent.Planning/IntentManager.cs`, `Agent.Planning/GoalFactory.cs`, `Agent.Planning/Decomposition/CraftItemGoalDecomposer.cs`

**Acceptance criteria:**
- "leo smelt 5 iron ore" → emits `SmeltItem` actions (not `CraftItem`)
- Adapter `case 'smelt':` handler is exercised
- Unit test: smelt intent → `SmeltItem` in action plan

---

### P0-2: SearchMemory Dead Weight (10+ sprints)

**Evidence:** `SearchMemory` is called ~15 times per gather cycle from `HtnTaskLibrary.cs` via hardcoded queries like `"flat area build location {blueprint.Name}"`, `"{block} nearby source location"`. Results are stored in `planContext` but NEVER consumed by any downstream action. TSK-0004 (SearchMemory → MoveTo context injection) was planned in Phase 4 but never implemented.

**Confidence:** 99%

**Suggested fix — pick ONE:**
- **Option A:** Implement TSK-0004 — wire `SearchMemory` results (coordinates, page ID) into `MoveTo` arguments so search results actually influence movement
- **Option B:** Strip `SearchMemory` calls from gather/build decompositions — the adapter's `bot.findBlock()` already does spatial search locally. Wiki search for block locations only makes sense for persistent multi-session knowledge.

**Files:** `Agent.Planning/HtnTaskLibrary.cs` (all call sites), `Agent.Tools/Tools/SearchMemoryTool.cs`, `WebUI.Blazor/AgentBackgroundService.cs` (planContext consumption)

**Acceptance criteria:**
- Either TSK-0004 is implemented (results feed into MoveTo) or SearchMemory is removed from decompositions
- No redundant SearchMemory calls per replan cycle

---

### P0-3: Zero Tests for Sprint 42/43 Checkpoint Changes

**Evidence:** `AdvanceBuildCheckpoint`, `BlockPlaceSkippedEvent` handler, `blockPlaceSkipped` adapter path, `_placeBlockContexts` lifecycle — all untested.

**Files needing tests:**
- `AdvanceBuildCheckpoint` — happy path, missing context, duplicate event
- `BlockPlacedEvent` handler — normal placement, terrain skip
- `BlockPlaceSkippedEvent` handler — checkpoint NOT advanced, correlation completed
- `_placeBlockContexts` — entry creation, cleanup on goal completion/cancel/timeout
- `IsIdleOrWanderGoal` — true for idle/wander, false for gather/build
- `IntentManager.ResolveItem` — wool→white_wool, planks→oak_planks, unknown→passthrough

**Suggested approach:** Add tests to `MemorySmith.Agent.Tests/Sprint43Tests.cs` following the existing `Sprint35Tests.cs` pattern.

**Acceptance criteria:**
- All 6+ test cases pass
- No regressions in existing 608 tests

---

## P1 Items — Next Priority

### P1-1: Remove `ChatInterpretation.GoalName` (7 sprints deferred)

**Status:** Documented as "removal deferred to Sprint 39 (requires ChatInterpreterTests + Sprint21Tests updates)" in AGENTS.md. It's now Sprint 44.

**Files:** `Agent.Planning/ChatModels.cs`, `Agent.Planning/LlmChatInterpreter.cs`, `MemorySmith.Agent.Tests/ChatInterpreterTests.cs`, `MemorySmith.Agent.Tests/Sprint21Tests.cs`

---

### P1-2: `_placeBlockContexts` Cleanup in SweepTimedOutActions

**Evidence:** Entries in `_placeBlockContexts` are removed by `AdvanceBuildCheckpoint` (via `TryRemove`). But if a PlaceBlock times out or the sweep fires before the event arrives, the entry leaks. Add cleanup in `SweepTimedOutActions` for any PlaceBlock contexts whose correlationId is no longer in `_correlatedActions`.

**Files:** `WebUI.Blazor/AgentBackgroundService.cs`

---

### P1-3: Scaffolding for Roof/Upper Walls (TSK-0077)

**Status:** Backlog. Bot can't reach Y=68+ for roof blocks. Requires building temporary dirt pillars.

**Files:** `Agent.Planning/HtnTaskLibrary.cs`, `Agent.Construction/BlueprintExecutor.cs`, `MineflayerAdapter/index.js`

---

### P1-4: Pre-Build Terrain Clearance (TSK-0078)

**Status:** Backlog. Blueprint positions overlapping terrain should emit `MineBlock` actions before placement phase.

**Files:** `Agent.Planning/HtnTaskLibrary.cs`

---

## P2 Items — Deferred

| Item | Age | Notes |
|---|---|---|
| Stale player position for "come here" | 1 sprint | Uses chat-time position, not current — minor UX issue |
| findFlatArea Y offset (surface+1) | Ongoing | May cause 1-block Y misalignment in builds |
| AgentBackgroundService decomposition | 13+ responsibilities | Event routing → IEventRouter, correlation → IActionTracker |
| WorldState.Facts vs StructuredFacts | Ongoing | Dual store can diverge — migrate to single source |
| ActionData typed context | Ongoing | Extract correlationId:Guid as first-class property |
| HasExplicitOrigin fix | Ongoing | Require all 3 coords, not any single one |
| Blueprint verification | New | Post-build scan + diff + recovery |
| GoalFactory rename | Cosmetic | Should be GoalResolver (policy engine, not factory) |

---

## Architecture Map

### Key Files

| File | Purpose |
|---|---|
| `MineflayerAdapter/index.js` | Node.js bridge — all Minecraft interaction (move, mine, place, craft, smelt, findFlatArea) |
| `WebUI.Blazor/AgentBackgroundService.cs` | Agent orchestration — goal lifecycle, action dispatch, event processing, correlation |
| `Agent.Planning/HtnTaskLibrary.cs` | Task decomposition — blueprint → actions, gather → actions, craft → actions |
| `Agent.Planning/LlmChatInterpreter.cs` | LLM-based chat interpretation — IntentDraft pipeline |
| `Agent.Planning/IntentManager.cs` | IntentDraft → GoalRequest mapping + alias resolution |
| `Agent.Planning/GoalFactory.cs` | GoalRequest → IGoal creation |
| `Agent.Core/CommonMinecraftBlocks.cs` | Direct-mine block catalog + yield source mappings |
| `Agent.Core/Events/WorldEvents.cs` | Typed event hierarchy (17+ sealed records) |
| `Agent.Core/Models/IntentDraft.cs` | LLM output shape (no GoalName) |
| `Agent.Core/BuildFactKeys.cs` | Fact key constants for build progress |
| `Agent.Core/WorldStateProjector.cs` | Event-driven world state updates |
| `Agent.Construction/BlueprintExecutor.cs` | Blueprint → ordered PlaceBlock actions |
| `Agent.World.Minecraft/WebSocketBridge.cs` | JSON event deserialization |

### Event Flow (Intent → Action → Event)
```
Chat → LlmChatInterpreter → IntentDraft → IntentManager → GoalRequest
  → GoalFactory → IGoal → HtnTaskLibrary.Decompose* → ActionData[]
  → ToolDispatcher → WebSocket → index.js dispatch → Minecraft
  → index.js event → WebSocket → AgentBackgroundService.ProcessEventsAsync
  → WorldStateProjector → WorldState update
```

---

## Test Strategy

- **638 unit tests** — all pass. Strong core abstraction coverage.
- **New Sprint 44 tests:** 31 tests in `Sprint44Tests.cs` covering SmeltGoal, IntentManager.ResolveItem, IsIdleOrWanderGoal, SearchMemory removal, SmeltGoalDecomposer, ChatInterpretation removal, IntentDraft.GoalName check.
- **Critical gaps:** No tests for `AdvanceBuildCheckpoint`, `BlockPlaceSkippedEvent`, `_placeBlockContexts`, `BlockPlacedEvent` handler, terrain occupancy skip path. Tracked as TSK-0083.
- **No E2E tests** — all tests are unit-level in simulated environment. Nothing exercises against a real Minecraft server.
- **Chronic risk:** BlockPlacedEvent correlation was missing for 10+ sprints before Sprint 41 caught it. The current test suite would not catch a similar missing-handler regression.

---

## Sprint 44 Completed ✅

| Task | Status | Details |
|---|---|---|
| **TSK-0079**: Smelt→CraftItem routing | ✅ Done | SmeltGoal, SmeltGoalDecomposer, SmeltGoalRequest, DecomposeSmeltItem, GoalFactory, LLM prompt, ABS handler |
| **TSK-0080**: SearchMemory dead weight | ✅ Done | Stripped all 15 dead SearchMemory calls from decompositions. Tool remains registered for LLM use. |
| **TSK-0081**: Add tests | 🟡 50% | 31 tests added for SmeltGoal, IsIdleOrWanderGoal, ResolveItem, SearchMemory removal. Checkpoint tests tracked as TSK-0083. |
| **P1-1**: Remove ChatInterpretation.GoalName | ✅ Done | Record removed from ChatModels.cs; reflection test confirms |
| **P1-2**: _placeBlockContexts cleanup | ✅ Done | SweepTimedOutActions cleans up stale + orphaned entries |

### Council-Identified Critical Fixes (Applied Inline)

| Issue | Fix |
|---|---|
| SmeltGoal.OutputItem wild-card `_ore → _ingot` produces `redstone_ingot` etc. | Removed wildcard; only explicit mappings |
| IntentManager missing `"iron"→"iron_ore"` aliases | Added 4 ore aliases |
| DecomposeSmeltItem can't mine raw_iron (1.17+) | Added raw_iron → iron_ore mapping + IsMineableBlock |
| HtnPlanner creative path emits dead SearchMemory | Removed; only MoveTo remains |
| SweepTimedOutActions orphaned check races with new dispatches | Added 1-second age threshold |
| SmeltItem timeout (30s) < JS adapter timeout (40s) | Added SmeltItem=45s override |

### Council-Identified New Tasks

| Key | Title | Priority | Status |
|---|---|---|---|
| **TSK-0082** | Extract shared SmeltableMapping class | P1 | Backlog |
| **TSK-0083** | Add checkpoint tests (AdvanceBuildCheckpoint, BlockPlacedEvent, etc.) | P1 | Backlog |
| **TSK-0084** | Add WorldStateProjector.ApplySmeltComplete | P1 | Backlog |
| **TSK-0085** | Fix SmeltGoal.HasFailed dead code | P2 | Backlog |
| **TSK-0086** | Fix stale doc comments across 5 files | P2 | Backlog |

## Updated Council Recommendations

1. ✅ **Sprint 44: Correctness sprint** — COMPLETE (638 tests, 0 failures)
2. **Sprint 45: Architecture + remaining tests sprint** — TSK-0082, TSK-0083, TSK-0084, AgentRuntime decomposition, fact store unification
3. **Sprint 46+: Feature sprint** — scaffolding, terrain clearance, new goals

---

## Quick Reference: Key Constants

| Constant | Value | File |
|---|---|---|
| PlaceBlock timeout | 5s | `AgentBackgroundService.cs` |
| MoveTo timeout | 10s | `AgentBackgroundService.cs` |
| MoveTo tolerance (place) | 2 blocks | `index.js` |
| Build proximity threshold | 5 blocks | `HtnTaskLibrary.cs` |
| Replan interval | 2s | `AgentBackgroundService.cs` |
| Stall threshold | 5 | `Program.cs` |
| Default action timeout | 30s | `AgentBackgroundService.cs` |
| LLM confidence threshold | 0.6 | `LlmChatInterpreter.cs` |
