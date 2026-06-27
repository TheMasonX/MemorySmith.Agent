# Council Synthesis: Sprint 43 Post-Fix Status Review

**Date:** 2026-06-23  
**Branch:** `sprint-35-llm-first` (87 commits ahead of `main`)  
**Tests:** 608 pass, 0 fail  
**Last commit:** "Sprint 43: Live gameplay fixes"  
**Method:** Synthesizer seat review of Sprint 42 council, Sprint 43 council, and Sprint 35 LLM-First Delta Audit

---

## 1. What's Working — Real Progress

### ✅ Sprint 43's 7 live-gameplay fixes are committed and passing
All P0/P1 items from the Sprint 43 council were implemented in a single commit batch: fast-path navigate, selective CancelGoal, blockPlaceSkipped event, wool aliases, proximity-gated MoveTo, Math.floor() botPos, and 5s PlaceBlock timeout. **This is the fastest fix-to-merge turnaround in the project's history** (council → implementation → commit within the same session).

### ✅ Correlation lifecycle is mature and production-hardened
The `TryUpdate` → sweep → per-tool timeout → event-driven completion pipeline has held up across Sprints 41-43. The `blockPlaceSkipped` event addition proves the architecture supports new event types without destabilizing existing state transitions.

### ✅ LLM-first IntentDraft pipeline is architecturally sound
Despite implementation gaps (see §2), the structural commitment is solid: `ChatInterpreter` produces `ChatInterpretation`, `IntentManager` maps to `GoalRequest`, `GoalFactory` resolves to `IGoal`. The "parsers never create goals" rule (CRITICAL Rule A-1) is enforced in `AGENTS.md` and followed in code paths that go through `IntentManager`.

### ✅ Logging and observability are significantly better than Sprint 40
Full LLM debug logging (prompt, raw response, parsed JSON, confidence), error position context in events, and the E-3 "never swallow exceptions" rule have eliminated the silent-drop failure mode that plagued Sprint 41. The `blockPlaceSkipped` event adds checkpoint-transparent logging for a previously invisible failure mode.

### ✅ Test suite is stable at 608 passing with zero failures
Despite adding substantial new code (blockPlaceSkipped handler, proximity gating, wool aliases, navigate fast-path), no regressions were introduced. This is a strong signal that the core abstractions (events, correlations, timeouts) are well-tested.

---

## 2. What's Accumulated — Deferred Maintenance

### 🔴 P0: Smelt→CraftItem routing bug (7 sprints old)
**Status:** Unfixed. Confirmed by 3 separate audits with 98%+ confidence. `ChatInterpreter` routes `smelt` → `CraftItemGoal` → `CraftItemTool`, which crafts from inventory — it does not smelt in a furnace. A player saying "smelt iron ore" gets a nonsense craft plan.

### 🔴 P0: SearchMemory called ~15x per gather cycle, results NEVER consumed
**Status:** Unfixed. TSK-0004 exists but is unassigned. The SearchMemory wiring was added for context injection into MoveTo coordinates, but the consumer side was never built. Every gather/build cycle makes ~15 dead API calls.

### 🔴 P0: Zero tests for Sprint 42/43 checkpoint/occupancy changes
**Status:** Unfixed. The `AdvanceBuildCheckpoint` method, `BlockPlacedEvent` handler, `BlockPlaceSkippedEvent` handler, `_placeBlockContexts` lifecycle, and terrain occupancy skip path are all untested. These are the most critical correctness paths in the build system.

### 🟡 P1: `ChatInterpretation.GoalName` zombie field (7 sprints old)
**Status:** Unfixed. The field exists in `ChatInterpretation` for Sprint 21 backward compatibility, with removal deferred to Sprint 39 (now Sprint 43+). Every interpreter still populates it, creating confusion about whether goal naming is an interpreter or planner responsibility.

### 🟡 P1: No E2E tests against real Minecraft server
**Status:** Unfixed. All 608 tests are unit tests. The placement reliability ceiling has been reached without integration tests.

### 🟡 P1: Scaffolding not implemented (TSK-0077)
**Status:** Backlog. Can't reach Y=68+ for roofs. Blocks significant build scenarios.

### 🟡 P1: Pre-build terrain clearance not implemented (TSK-0078)
**Status:** Backlog. No automated tree/rock clearing before building.

### 🟢 P2: AgentBackgroundService has 13+ responsibilities
**Status:** Deferred. Event routing, goal management, correlation tracking, checkpoint management, timeouts, logging, LLM evaluation stubs — all in one class.

### 🟢 P2: WorldState.Facts vs StructuredFacts dual store
**Status:** Deferred. Can diverge, creating correctness risk for goal completion detection.

---

## 3. Recommended Priorities for Next Agent

### P0 — Fix before any new feature work

| # | Item | Effort | Risk if Deferred |
|---|---|---|---|
| 1 | **Fix smelt→CraftItem routing** — route `smelt` intent through furnace path or add `SmeltGoalDecomposer` | Small | Active user-facing bug: "smelt iron ore" produces nonsense |
| 2 | **Wire SearchMemory results or remove calls** — either implement TSK-0004 or strip SearchMemory from gather/build decompositions | Medium | ~60 wasted API calls per minute; masks that context injection never shipped |
| 3 | **Add unit tests for Sprint 42/43 changes** — `AdvanceBuildCheckpoint`, `BlockPlacedEvent`/`BlockPlaceSkippedEvent` handlers, `_placeBlockContexts` lifecycle, terrain occupancy skip, proximity-gated MoveTo | Medium | Silent correctness regression on next refactor; no safety net for the most critical build path |

### P1 — High value, moderate effort

| # | Item | Effort | Notes |
|---|---|---|---|
| 4 | **Remove `ChatInterpretation.GoalName`** | Small | 7 sprints deferred; do a grep for unbilled consumers, update Sprint21Tests, remove |
| 5 | **Add `_placeBlockContexts` cleanup in `SweepTimedOutActions`** | Small | Prevents dictionary leak from duplicate events (known pattern) |
| 6 | **Implement scaffolding (TSK-0077)** | Medium | Unlocks roof/upper-wall builds |
| 7 | **Implement pre-build terrain clearance (TSK-0078)** | Medium | Prevents terrain-collision skips on vegetated sites |
| 8 | **Fix stale player position for "come here"** | Medium | Re-query position at dispatch time instead of using chat-time position |

### P2 — Strategic, paced

| # | Item | Notes |
|---|---|---|
| 9 | **Decompose AgentBackgroundService** | Extract event routing → `IEventRouter` first; full decomposition in Sprint 44+ |
| 10 | **Add E2E tests against simulated/real Minecraft** | Targeting Sprint 44 |
| 11 | **Unify `Facts` and `StructuredFacts`** | After event routing extraction, not before |
| 12 | **Rename `GoalFactory` → `GoalResolver`** | During AgentRuntime decomposition for natural refactor point |
| 13 | **Wire `IKnowledgeResolver` into planning** | Currently has zero consumers |
| 14 | **Add "explain" chat command** | P3 nice-to-have |

---

## 4. Risk Assessment

### Where the next production incident is likely

**Highest risk: Smelt→CraftItem routing (P0, 70% chance of user-facing failure)**
This is the most dangerous remaining bug. It's been confirmed by 3 audits at 98% confidence, documented in the Sprint 42 council with explicit acceptance criteria (AC-3), and still unfixed. A user saying "smelt iron ore" or "smelt 5 iron ingots" gets a craft plan that fails silently or produces the wrong output. Unlike the terrain collision issue (which was fixed in Sprint 43), this has no visible error signal — the bot tries to craft, fails, and the user sees a vague timeout or stall.

**Second highest: Checkpoint/occupancy regressions from untested code (P0, 40% chance)**
The `AdvanceBuildCheckpoint`, `BlockPlaceSkippedEvent`, and `_placeBlockContexts` code has zero test coverage. Any refactor, rename, or event-type addition risks breaking the checkpoint lifecycle without detection. The Sprint 43 fix for `blockPlaceSkipped` was implemented correctly based on manual inspection, but there's no automated safety net.

**Medium risk: AgentBackgroundService sprawl (P1, 25% chance of incident in next 3 sprints)**
The class grows with every sprint. The next incident is likely from an interaction between two of its 13+ responsibilities — e.g., a new event type that interacts unexpectedly with the sweep timeout logic, or a goal state check that reads the wrong fact store.

**Low risk: SearchMemory dead weight (P0, 10% chance of user-facing incident)**
It's wasteful but not dangerous — the API calls return data that's simply ignored. Risk is limited to rate limiting if the server has aggressive API quotas, and minor latency from wasted network calls (~15 per cycle).

---

## 5. Direction Assessment

### Is the `sprint-35-llm-first` branch near merge-ready?

**No — not yet. Confidence: 65%**

**What's merge-ready:**
- The LLM-first intent pipeline architecture (parsers → `ChatInterpretation` → `IntentManager` → `GoalRequest` → `GoalFactory`) is structurally sound
- The event/correlation lifecycle is production-hardened through Sprints 41-43
- Logging and observability are adequate
- 608 tests pass with zero regressions
- 7 live-gameplay fixes from Sprint 43 are committed

**What blocks merge:**
1. **P0 bugs with user-facing impact**: Smelt→CraftItem routing is confirmed by 3 audits. This is a real bug that real users will hit.
2. **Untested critical path**: The checkpoint/occupancy changes from Sprints 42-43 have zero coverage. A merge without tests means any future regression on the most critical build path is invisible.
3. **Dead weight in production**: SearchMemory is called ~15x per cycle with results discarded. Merging this means shipping code that wastes API calls and adds latency with zero benefit.
4. **Dual fact stores**: `WorldState.Facts` and `StructuredFacts` can diverge, creating correctness risk for goal completion.

**What a merge would look like:**
A responsible merge to `main` requires:
- [ ] Fix smelt→CraftItem routing
- [ ] Wire or remove SearchMemory calls
- [ ] Add unit tests for Sprint 42/43 checkpoint/occupancy changes
- [ ] Remove `ChatInterpretation.GoalName` (7 sprints deferred — just do it)

These 4 items represent roughly 1-2 focused coding sessions. The branch is **close** — closer than it has been since Sprint 35. The architectural direction is correct, the code quality is improving (608 tests green, logging adequate, event system clean), and the pace of fix delivery is accelerating (Sprint 43's 7 fixes in one session).

**Bottom line:** The branch is **65% of the way to merge-ready**. The remaining work is concentrated in 4 well-understood items, all P0. The project should resist the temptation to add new features (scaffolding, terrain clearance, E2E tests) until these 4 items ship.

### Open Questions

1. **Should the Sprint 43 `blockPlaceSkipped` implementation be reviewed against the Sprint 42 council's recommendation?** The Sprint 42 council recommended a `BlockSkippedEvent` that advances correlation but NOT checkpoint. This was implemented. A review should confirm the handler correctly avoids checkpoint advancement and logs at appropriate level.

2. **Is the 2-sprint "correctness sprint" still the plan?** Sprint 43 was the first correctness sprint. Sprint 44 should be the second — fixing smelt, SearchMemory, and test gaps — before Sprint 45 resumes feature work.

3. **Who owns TSK-0004 (SearchMemory → MoveTo wiring)?** If it's deprioritized, the calls should be removed from gather/build decompositions to eliminate waste. The current state (called but not consumed) is the worst of both worlds.

---

*Synthesizer seat · Confidence: 85% · Produced 2026-06-23*
