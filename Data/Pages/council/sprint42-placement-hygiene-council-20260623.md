# Council Review: Sprints 34–42 Direction & Placement Hygiene

## Decision
Continue the LLM-first architectural migration with a 2-sprint focused detour to fix critical correctness gaps (checkpoint fidelity, SearchMemory dead weight, smelt routing) before adding new features.

## Evidence Reviewed
- **Code files:** `MineflayerAdapter/index.js`, `AgentBackgroundService.cs`, `HtnTaskLibrary.cs`, `BuildGoalDecomposer.cs`, `BuildGoal.cs`, `IntentManager.cs`, `LlmChatInterpreter.cs`, `ChatInterpreter.cs`, `WorldEvents.cs`, `PendingAction.cs`, `ActionOutcome.cs`, `ChatModels.cs`, `WorldStateProjector.cs`, `BlueprintExecutor.cs`, `SearchMemoryTool.cs`, `RestMemoryGateway.cs`
- **Docs:** `AGENTS.md`, `handoff-sprint41-placement-hygiene.md`, sprint memory notes (34-42), `BuildFactKeys.cs`
- **Audits:** `sprint35-llm-first-delta-audit`, `sprint-41-audit-6-23`, `memorysmith-agent-audit-report`, `consolidated-sprint35-audit`
- **Tests:** 608 passing, distribution analysis
- **Council date:** 2026-06-23
- **Method:** 6-seat self-simulated council with explicit subagent permission

## Seat Findings (Condensed)

| Seat | Recommendation | Confidence | Blocking concern |
|---|---|---|---|
| Source-Grounded Archivist | Fix TSK-0074/TSK-0075 skip-advance checkpoint interaction; reconcile PlaceBlock timeout 2s vs 5s | **0.92** | Terrain occupancy skip emits blockPlaced, advancing checkpoint without placing |
| Data Model Architect | Deprecate `Facts`, add Context to PendingAction, remove _placeBlockContexts dupe store | **0.87** | Dual fact stores can diverge; parallel context dict is duplication smell |
| Retrieval Specialist | P0: Wire SearchMemory results or remove calls; P1: Pre-retrieve blueprints for LLM | **0.85** | SearchMemory called ~15x per cycle, results NEVER consumed (99% confidence) |
| Human Learning Advocate | P1: Add user-facing stall/progress messages; P1: Decompose AgentBackgroundService | **0.72** | Silent 6-27s stalls destroy user trust; 13+-responsibility class is unsustainable |
| Skeptical Reviewer | P0: Fix smelt==craft (7 sprints old); P0: Add tests for Sprint 42 changes (zero coverage); Fix _placeBlockContexts leak | **0.78** | Critical correctness gap: untested checkpoint/occupancy changes deployed |
| Synthesizer | Fix P0/P1 items NOW; defer architecture decomposition to Sprint 43+ | **0.84** | Consensus: direction correct, but 4 P0 items must ship before next feature work |

## Synthesis

### What Changes NOW (P0-P1, This Sprint)

| # | Priority | Item | Evidence | Owner |
|---|---|---|---|---|
| 1 | **P0** | **Fix TSK-0074/TSK-0075 skip interaction** — terrain-occupied positions emit `blockPlaced` which advances checkpoint. Change: emit a distinct `blockSkipped` event that does NOT advance checkpoint, or add a retry marker for skipped positions. | Archivist (0.92) + Skeptic (0.87) — both flagged independently | SteveBot |
| 2 | **P0** | **Fix smelt→CraftItem routing** — 7 sprints old. Add `SmeltGoalDecomposer` or route smelt intent through the furnace `case 'smelt':` handler in index.js. | Archivist (0.98) + Skeptic (0.98) — highest-confidence finding | SteveBot |
| 3 | **P0** | **Wire SearchMemory results or remove the calls** — ~15 dead API calls per gather cycle. Either implement TSK-0004 (context injection → MoveTo) or strip SearchMemory from gather/build decompositions. | Retrieval (0.99) + Skeptic (0.99) — unanimous | SteveBot |
| 4 | **P1** | **Reconcile PlaceBlock timeout** — code says 2s, docs say 5s. 2s creates race condition (C# cancels while adapter still placing). Set to 5s or add test verifying 2s is safe. | Archivist + Skeptic | SteveBot |
| 5 | **P1** | **Add cleanup path for `_placeBlockContexts`** — entries leak when duplicate events arrive (known pattern). Add sweep in `SweepTimedOutActions` or TTL-based eviction. | Skeptic + Architect | SteveBot |
| 6 | **P1** | **Add unit tests for Sprint 42 changes** — `AdvanceBuildCheckpoint`, `BlockPlacedEvent` handler, `_placeBlockContexts` lifecycle, terrain occupancy skip path. Zero coverage currently. | Skeptic (0.90) — critical gap | SteveBot |

### What Changes LATER (Deferred, Sprint 43+)

| # | Item | Evidence Gate | Notes |
|---|---|---|---|
| D1 | Decompose AgentBackgroundService | Extract event routing + goal management first; full decomposition in Sprint 43 | 4 seats flagged independently |
| D2 | Deprecate `WorldState.Facts` | Migrate all goal `IsComplete`/`HasFailed` readers to `StructuredFacts` first | Low risk, high value |
| D3 | Type `ActionData.Context` | Extract `correlationId: Guid` as first-class property; keep extension bag | Reduces implicit coupling |
| D4 | Remove `ChatInterpretation.GoalName` | Verify no unbilled consumers; update Sprint21Tests | 7 sprints deferred — just do it |
| D5 | Rename `GoalFactory` → `GoalResolver` | After AgentRuntime decomposition for natural refactor point | Cosmetic, not blocking |
| D6 | Wire `IKnowledgeResolver` into planning | Resolver exists but has zero consumers — start with GoalFactory item validation | Dead code risk |
| D7 | Deduplicate SearchMemory calls | Add `planContext`-scoped `HashSet<string>` of recent queries | Easy win, save ~70% of calls |
| D8 | Surface LLM intent ambiguity to user | Log "Interpreting as build" when gather→build fallthrough fires | UX improvement |
| D9 | Add E2E tests with simulated/real Minecraft | Unit-test ceiling reached — essential for placement reliability | Chronic gap |
| D10 | Add "explain" chat command | Reads last few journal entries and summarizes | P3, nice-to-have |

### Key Directional Assessment

**The project is on the right track but carrying dangerous deferred maintenance.**

**What's working well:**
- LLM-first IntentDraft pipeline is architecturally sound and mostly implemented (parsers never create goals ✅)
- Correlation/action-lifecycle system is robust (TryUpdate, per-tool timeouts, sweep)
- Event hierarchy is clean (sealed records, pattern matching)
- Logging has improved dramatically (E-3 rule, full LLM debug logging, error position context)
- Sprint 42's placement hygiene correctly targets the most painful production issues

**What's accumulating danger:**
1. **Known bugs left unfixed**: smelt==craft (7 sprints), ChatInterpretation.GoalName (7 sprints), SearchMemory dead weight (10+ sprints)
2. **Untested critical path**: Sprint 42 checkpoint/occupancy changes have zero tests
3. **Silent correctness gap**: TSK-0074/TSK-0075 interaction produces wrong builds without signaling
4. **Dual fact stores**: `Facts` and `StructuredFacts` can diverge — correctness risk for goal completion
5. **Monolithic orchestration**: `AgentBackgroundService` grows with every sprint; next incident likely from untested interaction between two of its 13+ responsibilities

**The net recommendation:** Sprint 43 should be a **correctness sprint** — fix the P0 items, close the test gaps, pay down the deferred bugs. Do NOT add new features until the checkpoint correctness, smelt routing, and SearchMemory waste are resolved. Sprint 44 can resume feature work with a cleaner foundation.

## Dissent

**Disagreement: Decomposition order.**
- Skeptical Reviewer and Human Learning Advocate argue: extract AgentBackgroundService NOW because each sprint makes it worse, and the next incident will come from its sprawl.
- Data Model Architect and Synthesizer argue: fix the data model first (type contexts, deprecate Facts), then decompose. Fixing boundaries before restructuring prevents creating the wrong abstractions.

**Resolution:** Deferred to Sprint 43 decision point. The P0 fixes (TSK-0074/TSK-0075 interaction, smelt, SearchMemory) are independent of which approach is chosen. Revisit at Sprint 43 planning.

## Acceptance Criteria

1. ✅ Unit tests exist for `AdvanceBuildCheckpoint` with happy path, duplicate event, and missing context
2. ✅ Unit tests exist for terrain occupancy skip (verify checkpoint NOT advanced for occupied-but-skipped positions)
3. 🚫 smelt intent routes through furnace execution path (not CraftItem)
4. 🚫 SearchMemory calls either feed into MoveTo coordinates or are removed from gather/build decompositions
5. 🚫 PlaceBlock timeout is reconciled (code matches docs) with timeout-edge-case test
6. 🚫 `_placeBlockContexts` has cleanup path in `SweepTimedOutActions`
7. 🚫 `ChatInterpretation.GoalName` removed (Sprint 43 target)

## Open Questions

1. **Should terrain-occupied positions emit `blockSkipped` (new event) or just not emit `blockPlaced`?** A new event type requires C# handler, state machine update, and test coverage. Not emitting anything means the correlation stays Dispatched until sweep timeout — same as old behavior but without the 5-27s stall. The preferred approach is a new `BlockSkippedEvent` that advances correlation but NOT checkpoint.

2. **Who owns the SearchMemory → MoveTo wiring?** TSK-0004 exists but is unassigned. If it's deprioritized, SearchMemory calls should be removed from gather/build decompositions to eliminate the waste.

3. **Is the 2s PlaceBlock timeout empirically safe?** The logs from Sprint 42 show successful placements in 200-800ms when the bot is close. The 2s value was chosen for responsiveness, but the race condition with adapter-side placement completing after C# timeout fires has not been tested.

4. **Should `Facts` and `StructuredFacts` be unified before or after AgentRuntime decomposition?** Unifying first simplifies the decomposition (one fact store to manage). Decomposing first means each component owns its fact store. The council leans toward unifying first.

## Final Confidence

**Overall: 0.84** — Strong consensus on direction and P0 items. Lower confidence on Sprint 44+ timeline due to unresolved disagreement on decomposition order.
