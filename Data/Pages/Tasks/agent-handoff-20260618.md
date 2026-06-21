# MemorySmith.Agent Handoff — Sprint 20
**Date:** 2026-06-18
**Branch:** `sprint-5-tool-safety` → PR #1 (merge still deferred)
**Head commit:** `0878e7bc` (Sprint 19 council review)
**CI status:** GREEN (build-and-test: success on ce22603)
**Previous handoff:** Data/Pages/Tasks/agent-handoff-sprint19.md

---

## Current state in one paragraph

Leo is a Minecraft bot (C# + Node.js) with a deterministic HTN planner, LLM fallback chat, and a MemorySmith wiki as long-term memory. Sprints 1–19 are complete. Sprint 19 delivered 6 phases: (1) structured rolling-file logging with Serilog Debug sink, ms precision, structured properties, action timing, inventory context, and JS-side JSON file logger; (2) system message filtering that blocks teleport/join/leave/server messages from the LLM pipeline; (3) gather plan rework that removes default Wander and adds it conditionally after BlockNotFound; (4) a 2-state replan governor (ACTIVE/STALLED) that detects 3+ identical plans and suppresses replanning until progress or 60s timeout; (5) findFlatArea expansion from default radius 20 to 32, with auto-expand to 48 on retry; (6) stone alias fix with yield-aware SourceBlocks so mining stone counts cobblestone toward completion. 15 new tests added. Council reviewed (87% confidence, all 6 seats approve, no blocking findings).

---

## What changed in Sprint 19

**Phase 1 — Logging Infrastructure**
- `WebUI.Blazor/Program.cs`: Serilog file sink lowered to Debug, ms precision (.fff), `{Properties:j}` for structured data
- `WebUI.Blazor/AgentBackgroundService.cs`: `SummarizeInventory()` helper; `[goal]` logs with inventory; `[plan]` logs action sequence; `[action]` logs with timing (Stopwatch); `[dispatch]` debug-level args
- `MineflayerAdapter/index.js`: `logStructured()` JSON line writer to `logs/adapter-YYYY-MM-DD.log`; timing in dispatch, mine, wander, findFlatArea

**Phase 2 — System Message Filter**
- `MineflayerAdapter/index.js`: 9 `SYSTEM_MESSAGE_PATTERNS` regexes; `isSystemMessage()` function; bot teleport emits position update via `sendEvent('move', botPos())` on next tick

**Phase 3 — Gather Plan Rework**
- `Agent.Planning/HtnTaskLibrary.cs`: `GatherItemDecompose` default plan is SearchMemory → MineBlock → GetStatus (3 actions). Wander inserted only when `event:BlockNotFound:Block` matches a source block.

**Phase 4 — Replan Governor**
- `Agent.Core/IReplanGovernor.cs` (NEW): `Evaluate`, `RecordProgress`, `Reset`, `IsStalled`
- `Agent.Core/ReplanGovernor.cs` (NEW): 2-state machine, threshold=3, recovery=60s, configurable for tests
- `WebUI.Blazor/AgentBackgroundService.cs`: Governor injected; evaluates plan fingerprint before enqueueing; `RecordProgress` on progress-signal tools; `Reset` on goal set/cancel
- `WebUI.Blazor/Program.cs`: `IReplanGovernor` registered as singleton

**Phase 5 — findFlatArea Expansion**
- `MineflayerAdapter/index.js`: `FLAT_AREA_SCAN_RADIUS` 20→32; `searchedRadius` in event payload
- `Agent.Planning/HtnTaskLibrary.cs`: `DecomposeBuild` sends radius=48 when `BuildFactKeys.LastFlatArea` was 0

**Phase 6 — Stone Alias Fix**
- `Agent.Planning/ChatInterpreter.cs`: `["stone"] = "stone"` (was "cobblestone")
- `Agent.Planning/GoalFactory.cs`: `YieldSourceBlocks` dictionary maps "stone" → ["stone", "cobblestone"]

**Phase 7 — Tests**
- `MemorySmith.Agent.Tests/ReplanGovernorTests.cs` (NEW): 7 tests
- `MemorySmith.Agent.Tests/Sprint19Tests.cs` (NEW): 8 tests

**Council review:** `Data/Pages/council/sprint19-council-20260618.md` — APPROVED (87%, no blockers)

---

## Sprint 20 starting point

### P0 — Council deferred findings (D-2, D-3, D-7)

**D-2 (HIGH): Verify BlockNotFound fact producer**
- Confirm `WorldStateProjector.StoreFacts` sets `event:BlockNotFound:Block` on `BlockNotFoundEvent`
- Add an integration test: `BlockNotFoundEvent → WorldStateProjector → state.Facts["event:BlockNotFound:Block"]` matches block name
- If the fact is NOT set, the conditional Wander from Sprint 19 Phase 3 silently degrades to "never wander"

**D-3 (MEDIUM): Verify LastFlatArea fact producer**
- Confirm `WorldStateProjector.StoreFacts` sets `BuildFactKeys.LastFlatArea` on `FlatAreaFoundEvent`
- Add integration test verifying the fact is set on area=0 events

**D-7 (LOW): Governor recovery log line**
- In `AgentBackgroundService.DispatchActionsAsync`, after `replanGovernor?.RecordProgress()`, log `[governor] recovered via progress — replanning resumed`

### P1 — Sprint 19 handoff deferred items (original Sprint 19 plan)

These were the P1 items from the original sprint-19 handoff that were not addressed:

**IItemAcquisitionRegistry (deferred from Sprint 19)**
- Interface in Agent.Core with `ResolveUserIntent` and `GetPlan` methods
- `StaticItemAcquisitionRegistry` backed by migrated ItemAliases data
- Mark `ChatInterpreter.ItemAliases` as `[Obsolete]`
- Migrate ChatInterpreter.ResolveItemId to delegate to registry

**Full 4-state governor (P2-7 from Sprint 19 handoff)**
- Design: PLANNING → EXECUTING → BACKING_OFF → STALLED
- Budget-based 1.5x exponential backoff, cap 30s, budget 8
- Extends the Sprint 19 2-state governor

### P2 — Design only (Sprint 21+)

**GoalProgressTracker**
- 30-line ConcurrentDictionary tracking per-goal mining progress independent of StatusEvent resets
- INVARIANT: read only in IsComplete(), never for "what does the bot have"

**NavigateTo race condition**
- Seat 4 identified: NavigateTo is fire-and-forget from C#, pathfinding is async in JS
- If stop fires before pathfinding setup completes, bot twitches and stops

**JS-side greedy mining loop**
- After mining a block, scan adjacent same-type blocks and mine nearest before returning

**MemorySmith wiki deployment scripts**
- `tools/Start-MemorySmithWiki.ps1` and `tools/Seed-Wiki.ps1`
- Required for blueprints and SearchMemory to work

**ObservationNormalizer**
- `Agent.Core/ObservationNormalizer.cs` with `NormalizeId`, `NormalizeInventory`, `NormalizeBlockId`

---

## Key rules (non-negotiable)

1. Warnings = errors (`Directory.Build.props`). Fix before pushing.
2. paramsFile, never inline content when pushing to GitHub MCP.
3. CI must be green before council review.
4. Enqueue chat response AFTER the switch in `HandleChatEventAsync`.
5. ActionQueue is ConcurrentQueue — don't revert to `Queue<T>`.
6. NEVER put `SendActionAsync` calls in `SetGoal()` — breaks `ActionQueue_IsDrained` test.
7. Council workflow per phase: implement → push → CI green → council review → fix blockers → next sprint.
8. Do NOT change StatusEvent full-inventory-replacement in WorldStateProjector — it is a reconciliation feature.

---

## Files to read on arrival

- `AGENTS.md` — all rules, patterns, anti-patterns
- `Data/Pages/council/sprint19-council-20260618.md` — Sprint 19 council review (7 deferred findings)
- `Agent.Core/ReplanGovernor.cs` — Sprint 19 governor (understand before extending)
- `Agent.Core/IReplanGovernor.cs` — interface for the governor
- `WebUI.Blazor/AgentBackgroundService.cs` — governor integration points
- `Agent.Planning/HtnTaskLibrary.cs` — conditional Wander logic
- `Agent.Planning/GoalFactory.cs` — YieldSourceBlocks pattern
- `Agent.Core/WorldStateProjector.cs` — fact key producers (verify D-2, D-3)

---

## Guidance for future agent sessions

Per user instructions, every sprint must follow this workflow:
1. Review the handoff and understand the next sprint's tasks
2. Implement the sprint (P0 first, then P1, then P2)
3. Run 6-seat council review + anonymous peer review to `Data/Pages/council/sprint<N>-council-<date>.md`
4. Fix any blocking council findings; confirm CI green
5. Write the next sprint handoff (`Data/Pages/Tasks/agent-handoff-20260618<letter>.md`)
6. Push all docs to the branch

**This guidance must be included in every future handoff.**
