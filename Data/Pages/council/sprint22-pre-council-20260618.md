# Sprint 22 Pre-Sprint Council Review
**Date:** 2026-06-18
**Format:** 5-Chair Anonymous Peer Review
**Scope:** MemorySmith.Agent — Sprint 21 head (`8b151c08`, branch `sprint-5-tool-safety`), proposed Sprint 22 scope
**Files reviewed:** WorldState.cs, AgentBackgroundService.cs, CraftItemGoal.cs, HtnTaskLibrary.cs (sprint-branch), agent-handoff-sprint21.md, RestMemoryGatewayOptions.cs, Program.cs

---

## Chair 1 — Architecture Coherence
**Confidence:** 88%

### Summary
The architecture is internally consistent at the Sprint 21 head. The `IsInventoryStale` flag correctly threads through the Builder pattern and is cleared by `WorldStateProjector`. The governor pre-plan check in `DispatchActionsAsync` is placed correctly and avoids redundant `PlanAsync` calls during STALL. DI wiring in Program.cs is readable and complete for the current feature set.

### Findings

**B-1 (Blocking) — CraftItemGoal.IsComplete does not guard on IsInventoryStale**
`CraftItemGoal.IsComplete` reads `state.Inventory.GetValueOrDefault(itemId) >= count` with no staleness check. `GenericGatherGoal` received this guard in Sprint 21 (P0-A) but `CraftItemGoal` was not updated. The symptom is identical: after an admin `/clear`, a CraftItem goal in progress will false-complete on the next cycle before `GetStatus` refreshes inventory. Sprint 22 P0 must address this before any other work.

**D-1 (Deferred) — IReplanGovernor not registered in Program.cs**
`AgentBackgroundService` accepts `IReplanGovernor? replanGovernor = null` as an optional constructor param. Program.cs does not wire a concrete `ReplanGovernor`. The governor runs only if constructed manually (e.g. in tests). Sprint 22 should audit Program.cs to confirm whether `ReplanGovernor` is registered and add it if not.

**D-2 (Deferred) — WorldKbUrl absent from RestMemoryGatewayOptions**
The agent codebase KB and the Minecraft world KB share a single `BaseUrl`. There is no `WorldKbUrl` property. Any world-specific pages (block locations, exploration notes) pushed via `CreatePageTool` land in the same MemorySmith instance as agent docs and council reviews. Sprint 22 P0 (world KB separation) addresses this directly.

**D-3 (Deferred) — Program.cs version string is "0.7.0 Phase 5b"**
The `/api/about` endpoint still reports `Version = "0.7.0"` and `Phase = "Phase 5b — LLM chat..."`. Sprints 6-21 have shipped significant features. A version bump to `0.22.0` or similar in Sprint 22 would make operational status visible without consulting the git log.

---

## Chair 2 — Gap Analysis
**Confidence:** 85%

### Summary
Sprint 21 delivered its stated P0-A (freshness gate for GenericGatherGoal), P0-B (pre-plan IsStalled), P0-C (AGENTS.md correction), and all four P1 items. The handoff doc accurately reflects what was shipped. However, three out of five Sprint 21 council deferred items (D-1, E-1, B-1) were not closed before handoff and now roll over to Sprint 22.

### Findings

**B-1 (Blocking) — Quantity propagation in GatherItemDecompose is untested**
`GatherItemDecompose` reads `parameters[0]` for count. `HtnPlanner.PlanAsync` calls `library.DecomposeGatherItem(spec, parameters, state)` — but it is unclear what `parameters` contains for a `GenericGatherGoal(item, targetCount: 100)`. If `HtnPlanner` passes `[targetCount.ToString()]`, propagation is correct. If it passes `[]`, the default `count = 10` is used every time, losing the user's requested quantity. This requires a unit test and a trace through `HtnPlanner` to confirm. **Mark as blocking because it affects real user interaction ("get 100 sand").**

**D-1 (Deferred) — Sprint 21 D-1 deferred item not addressed**
"Add staleness debug log at GenericGatherGoal.IsComplete call site" was deferred from Sprint 21. Still not present in the fetched `GenericGatherGoal.cs`. Low risk but useful for diagnosing slow-start goal behaviour. Sprint 22 P1.

**D-2 (Deferred) — GetStatus health gate not implemented**
Sprint 21 handoff lists "GetStatus health check for drowning recovery" as Sprint 22 P0. The current `ProcessEventsAsync` handles `HealthEvent` only via `WorldStateProjector`; no swim/recovery action is triggered. The handoff notes "Leo drowned unresponsive in Session 1". Sprint 22 P0.

**D-3 (Deferred) — AGENTS.md E-1 note not present**
Sprint 21 deferred E-1: "AGENTS.md note about verbatim-regex fetch-and-patch pattern". Not found in current AGENTS.md (only Rule E-1 exists, which forbids the naive approach, but the safe alternative pattern using Python bytes patching is not documented). Sprint 22 P1.

---

## Chair 3 — Safety & Correctness
**Confidence:** 90%

### Summary
No data corruption or concurrency hazards introduced in Sprint 21. The `IsInventoryStale` flag is a boolean on an immutable record, correctly protected by the `With(...Builder)` pattern. The governor's `IsStalled` check runs on the main dispatch loop — no race condition.

### Findings

**B-1 (Blocking) — CraftItemGoal false-completion (confirm of Chair 1 B-1)**
Confirmed. `CraftItemGoal.IsComplete` will return true on stale inventory. The fix is two lines: `if (state.IsInventoryStale) return false;` before the inventory check. Test: set goal, call `/clear`, verify `IsComplete` returns false until `StatusEvent` arrives.

**B-2 (Blocking) — GetStatus freshness gate: SetGoal P0-B from Sprint 20 still deferred**
Sprint 20 deferred the GetStatus-injection-on-SetGoal because it broke 3 tests. Sprint 21 solved the symptom (IsInventoryStale gate) but not the root cause. The bot now avoids false-completion, but it still doesn't immediately fetch fresh inventory after goal set — it waits for the next plan cycle which could be up to `MinReplanIntervalSeconds` (2s). In practice this is acceptable, but a health-gate test for SetGoal-triggers-GetStatus remains an open correctness gap. Sprint 22 P0.

**D-1 (Deferred) — Governor RecordProgress is never called from outside the 300ms settle**
`ReplanGovernor.RecordProgress()` is called only from the cycle-settle block (after `_actionDispatchedThisCycle`). If the plan has zero actions (immediate completion), the settle never runs and `RecordProgress` is never called. This means a goal that completes in zero actions could leave the governor in a stale counting state. Low probability in practice (all goals have at least one action) but worth a unit test.

**D-2 (Deferred) — WorldState.With(Builder) re-stamps UpdatedAt even for no-op changes**
`Builder.Build()` always calls `_state with { UpdatedAt = DateTimeOffset.UtcNow }`. A no-op change (e.g. `SetInventoryStale(false)` when already false) produces a new record with a new timestamp. This inflates the SignalR update rate. Low impact but introduces latency spikes on heavily-evented goals. Deferred.

---

## Chair 4 — Operational / User Experience
**Confidence:** 82%

### Summary
The agent is materially more usable after Sprint 21: false-completions after `/clear` are fixed, truncated LLM responses are recovered, and stall loops no longer flood the plan log. The key remaining operational gaps are drowning recovery, CraftItemGoal stale-inventory, and quantity propagation for large gather requests.

### Findings

**B-1 (Blocking) — Drowning recovery: no swim/health event triggers action**
`HealthEvent` is applied by `WorldStateProjector` (Health field updated) but `ProcessEventsAsync` does not handle `HealthEvent` in its switch. When health drops below threshold (e.g. drowning at y<62), no recovery action is triggered. The bot continues its current plan (mining underwater) until dead. Sprint 22 P0 should add a `HealthEvent` case in `ProcessEventsAsync` that interrupts the current goal and injects a `MoveTo(y+5)` or equivalent swim-up action when `health < 6` (3 hearts).

**B-2 (Blocking) — "get 100 sand" produces identical plan to "get 1 sand" (suspected)**
If `HtnPlanner` does not pass `targetCount` in `parameters`, then `GatherItemDecompose` defaults to `count = 10`. The bot mines until it gets 10 sand, then the governor detects repeated plans and STALLs, eventually timing out without ever accumulating 100. This is a usability-critical bug for any large gather request. Confirm and fix in Sprint 22 P1.

**D-1 (Deferred) — No world-KB separation means SearchMemory returns codebase noise**
When the bot searches for "oak_log location nearby source", MemorySmith may return council review docs, sprint handoffs, and architecture notes alongside any world observation pages, lowering result quality. Sprint 22 World KB separation (Track A) addresses this directly.

**D-2 (Deferred) — Version string "0.7.0 Phase 5b" in /api/about is misleading**
Any operator checking agent status via the REST API sees Phase 5b. After 17 additional sprints this is materially wrong and could cause confusion when debugging.

---

## Chair 5 — Sprint 22 Scope Evaluation
**Confidence:** 87%

### Summary
The proposed Sprint 22 scope is well-prioritised given the reordering (planner fixes first, world KB second). The three confirmed P0 items (CraftItemGoal staleness, drowning recovery, quantity propagation) are small, targeted fixes with clear test-first paths. The world KB separation is the most architecturally significant piece and should not be rushed.

### Findings

**No blockers on scope.**

**D-1 (Deferred) — GetStatus freshness gate test-first requirement carries design risk**
The Sprint 22 plan specifies "write failing test first, implement SetGoal-injects-GetStatus-when-stale, verify 3 existing tests still pass." The Sprint 20 post-mortem documented that this injection approach broke 3 tests. The Sprint 21 P0-A approach (flag + gate in IsComplete) is cleaner and was accepted. Recommend: Sprint 22 GetStatus gate should use the same pattern — mark WorldState.IsHealthStale (or reuse IsInventoryStale semantics) rather than injecting a GetStatus action into SetGoal. The inject-on-SetGoal path may still break the same 3 tests.

**D-2 (Deferred) — World KB separation interface naming**
The plan proposes `IWorldMemoryGateway` as a distinct interface. Consider whether `IWorldMemoryGateway` and `IMemoryGateway` should be the same interface registered under two names, or truly different types. If the world KB has the same `/api/search` and `/api/pages` surface as the agent KB, a single interface with two HTTP clients (named "memorysmith-agent" and "memorysmith-world") is simpler than two interfaces. Recommend named HttpClient registrations keyed to `WorldKbUrl` rather than a new interface.

**D-3 (Deferred) — Deployment guide should document MemorySmith version pinning**
The world-kb-deployment.md guide (Sprint 22 Phase 3) should specify which MemorySmith release the agent is tested against, and document how to configure the `DataDirectory` setting in MemorySmith's appsettings.json to point to `D:\Minecraft\MemorySmith\TestWorld`.

---

## Triage Summary

| ID | Chair | Severity | Finding |
|----|-------|----------|---------|
| B-1 | 1, 3 | **BLOCKING** | CraftItemGoal.IsComplete: add IsInventoryStale guard |
| B-1 | 2 | **BLOCKING** | Quantity propagation: verify HtnPlanner passes targetCount in parameters; fix if not |
| B-1 | 4 | **BLOCKING** | Drowning recovery: handle HealthEvent in ProcessEventsAsync |
| D-1 | 1 | Deferred | IReplanGovernor registration in Program.cs — audit and wire if missing |
| D-2 | 1, 4 | Deferred | WorldKbUrl absent — Sprint 22 Phase 3 addresses |
| D-3 | 1, 4 | Deferred | Version string stale — bump to v0.22.0 in Sprint 22 |
| D-1 | 2 | Deferred | Sprint 21 D-1 staleness debug log — Sprint 22 P1 |
| D-1 | 3 | Deferred | Governor RecordProgress not called on zero-action completion |
| D-2 | 3 | Deferred | With(Builder) UpdatedAt on no-op changes |
| D-1 | 5 | Deferred | GetStatus gate: use IsInventoryStale pattern, not inject-on-SetGoal |
| D-2 | 5 | Deferred | World KB: consider named HttpClient over new interface |
| D-3 | 5 | Deferred | Deployment guide: pin MemorySmith version, document DataDirectory |

## Blocking Findings: 3
## Deferred Findings: 9

## Council Verdict: **APPROVED — proceed with Sprint 22 as planned**

All three blocking findings are confirmed Sprint 22 P0/P1 items. No finding requires a scope change or pre-sprint correction. The deferred note on GetStatus gate (D-1, Chair 5) should inform the implementation approach: prefer the `IsInventoryStale` flag pattern over SetGoal-injection.

### Per-Chair Confidence
| Chair | Score |
|-------|-------|
| 1 | 88% |
| 2 | 85% |
| 3 | 90% |
| 4 | 82% |
| 5 | 87% |
| **Avg** | **86%** |
