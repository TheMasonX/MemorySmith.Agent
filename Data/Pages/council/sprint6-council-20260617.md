# Sprint 6 Council Review — Journal, World Model, Planner Extensibility
**Date:** 2026-06-17  
**Commit reviewed:** b8887c28 (after CI-fix commits)  
**PR:** https://github.com/TheMasonX/MemorySmith.Agent/pull/1  
**Sprint deliverables:** P0 Agent Journal · P1 World Model Depth · P2 Planner Extensibility  

---

## Scope Summary

| Priority | Deliverable | Files |
|---|---|---|
| P0 | Agent Journal (JournalEntry, IAgentJournal, AgentJournal, 15 call sites) | Agent.Core/Models/AgentJournal.cs, Agent.Core/Models/JournalEntry.cs, Agent.Core/Interfaces/IAgentJournal.cs, + call sites in AgentBackgroundService + ToolDispatcher |
| P1 | World Model Depth (ObservationState, BeliefState, PredictionState, IWorldModel, WorldModel) | Agent.Core/Interfaces/IWorldModel.cs, Agent.Core/Models/WorldModel.cs, Agent.Core/Models/ObservationState.cs, Agent.Core/Models/BeliefState.cs, Agent.Core/Models/PredictionState.cs |
| P2 | Planner Extensibility (IGoalDecomposer, DecomposerRegistry, 3 decomposers, PlannerRouter) | Agent.Planning/Decomposition/*, Agent.Planning/PlannerRouter.cs, Agent.Planning/DecomposerRegistry.cs |

**CI status at review start:** FAILED (3 build errors — see §CI-FIX below)

---

## §CI-FIX — Pre-review build errors (blocking, patched before review proceeds)

Three compiler errors were introduced in the Sprint 6 push that prevented any test from running. All were fixed before the council seats convened.

| Error | Root cause | Fix |
|---|---|---|
| `IGoal.FailureReason` read-only at 3 assignment sites in `AgentBackgroundService` | Sprint 5 added the property with only `{ get; }` on the interface; Sprint 6 logic required setting it | Added `set;` to `IGoal.FailureReason` in Agent.Core/Interfaces/IGoal.cs |
| `AgentStatusUpdate` type not found (line 544, `PushStatusToDashboardAsync`) | The SignalR push DTO record was referenced but never defined | Created WebUI.Blazor/Dtos.cs with the positional `AgentStatusUpdate` record |
| `IHubContext<>` not found in Program.cs (line 150) | `using Microsoft.AspNetCore.SignalR;` was absent from the top-level program file | Added the missing using directive to Program.cs |

All goal implementations (`SimpleGoal`, `BuildGoal`, `GatherWoodGoal`, `GenericGatherGoal`, `SurviveNightGoal`) already had `{ get; set; }` — no changes required there.

---

## Seat Reviews

### Seat 1 — Source-Grounded Archivist
**Confidence: 0.88**

Sprint 6 is consistent with the roadmap's Phase 6 intent: observability, world-model depth, and planner extensibility. All three deliverables were listed in the prior council-approved Sprint 6 scope document and the external architecture spec review (sprint6-arch-spec-review). No scope creep detected.

**Observations:**
- `JournalEntry` is an append-only immutable sealed record — correctly models the event-sourced execution trace that the roadmap described.
- `IAgentJournal.Clear()` is present; this is needed for agent-restart scenarios documented in Phase 4 notes. Consistent with design intent.
- `WorldModel.Observe()` currently mirrors observation directly to belief (`_belief` = observation mapped). The design doc noted this as "inference layer is Phase 6+" which is correctly labeled in a comment. Source-consistent.
- `PlannerRouter` is a thin wrapper that tries the `DecomposerRegistry` first, then falls back to `HtnPlanner`. This matches the "HTN fallback strategy" specified in the sprint scope.

**Finding D1 (deferred):** The `IWorldModel` interface exposes `Observed` and `Belief` as public read properties but `Uncertainty` is not surfaced in the PR's API endpoint (`/api/agent/status`). The architecture spec noted that the dashboard should expose uncertainty for operator monitoring. Not blocking — can be added in Sprint 7.

---

### Seat 2 — Data Model Architect
**Confidence: 0.82**

The three new state record types (`ObservationState`, `BeliefState`, `PredictionState`) are well-shaped. `JournalEntry` and its enum are minimal and correct.

**Positive:**
- `JournalEntry` is a sealed record with `IReadOnlyDictionary<string, object?>?` Details — good extensibility without inheritance.
- `PredictionState` carries `Confidence` (double) and `Rationale` (string) — more than a raw prediction; suitable for debugging uncertain states.
- `AgentJournal` uses `ConcurrentQueue<JournalEntry>` for lock-free writes from multiple producers (AgentBackgroundService + ToolDispatcher). Correct choice given the concurrent write pattern.

**Finding B1 (blocking):** `AgentJournal._count` is a `volatile int` incremented with `Interlocked.Increment`. However, the trim loop reads `_count` with `Volatile.Read` and dequeues in a while loop that can race: two concurrent `Log()` calls that both see `current > MaxEntries` will both dequeue, potentially draining more than needed. This is a soft correctness issue (entries lost above capacity; total count undershoots). Given CI already generated a compiler warning for the volatile field reference, the trim logic should be rewritten to use a compare-exchange or to simply accept that the queue may transiently exceed capacity by a small margin (common pattern for bounded approximate queues).

**Proposed fix for B1:** Remove the trim loop from `Log()`; instead add a separate `TrimToCapacity()` method called opportunistically (e.g. in `Recent()`), or switch to a `Channel<JournalEntry>` with `BoundedChannelFullMode.DropOldest` — which handles trimming atomically.

**Finding D2 (deferred):** `WorldModel.GetIntArg` silently returns a fallback of 0 when args contain a `JsonElement` (from the JSON-deserialised argument bag in `DispatchActionsAsync`). Tool arguments arrive as `JsonElement` values, not boxed `int`/`long`. This means all predictions that read numeric args will silently use 0. Not blocking because `IWorldModel` is not yet wired into the dispatch loop — but must be fixed before Reconcile is exercised.

---

### Seat 3 — Retrieval Specialist
**Confidence: 0.79**

Sprint 6 adds structured retrieval paths for journal and world-model data that were absent before.

**Positive:**
- `IAgentJournal.Query(type?, from?, to?)` provides time-range and type-scoped queries — sufficient for dashboard and debugging.
- `IAgentJournal.Recent(int count)` returns newest-first — correct for operator display.
- `DecomposerRegistry` is a list-based linear scan (`CanHandle` predicate). For the current 3 decomposers this is O(n) with negligible cost. Acceptable.

**Finding D3 (deferred):** `IAgentJournal` has no `Count` property. Callers (e.g. a future API endpoint) cannot cheaply check how many entries are recorded without materialising `All`. Minor omission — add `int Count { get; }` to the interface in Sprint 7.

**Finding D4 (deferred):** `WorldModel.Reconcile()` updates `_cachedUncertainty` inside a `lock(_lock)` but reads `_recentDeviationScores` (a `ConcurrentQueue`) outside the lock. The double-check is inconsistent. Correct but confusing — lock around the full reconcile operation or use an Interlocked pattern.

---

### Seat 4 — Human Learning Advocate
**Confidence: 0.85**

Sprint 6 significantly improves agent observability and debuggability.

**Positive:**
- 15 journal call sites with consistent `JournalEntry` shape means operators can build a coherent execution narrative from `Recent(50)`.
- `JournalEntryType` enum covers 11 event types — enough granularity for real-world diagnosis without explosion.
- `WorldModel` `Rationale` string on every `PredictionState` is excellent: human-readable explanation of what the agent expected. This is a meaningful observability win.
- `IGoalDecomposer.CanHandle` + `Decompose` is intuitive; adding a new goal type requires only implementing one interface and one `reg.Register()` call.

**Finding D5 (deferred):** None of the new types are exposed through any REST endpoint or SignalR push yet. The journal, belief state, and world model are internal to the agent process. Sprint 7 should add at minimum `GET /api/agent/journal?limit=N` and `GET /api/agent/worldmodel` so operators can inspect agent internals without attaching a debugger.

**Finding D6 (deferred):** `IAgentJournal` is injected as `IAgentJournal?` (nullable) in `AgentBackgroundService` — meaning journal writes silently no-op if DI doesn't provide one. This is intentional for test isolation but should be documented. Consider a `NullAgentJournal` no-op singleton for test usage rather than optional injection to prevent accidental null-propagation bugs.

---

### Seat 5 — Skeptical Reviewer
**Confidence: 0.72**

**Blocking finding B2:** `AgentJournal.Clear()` sets `_count = 0` with a plain assignment, not `Volatile.Write` or `Interlocked.Exchange`. Concurrent `Log()` calls can see a stale `_count` value and produce incorrect trim behaviour immediately after `Clear()`. Should be `Interlocked.Exchange(ref _count, 0)`.

**Blocking finding B3 (HIGH risk):** `WorldModel.Predict()` is not called anywhere in the codebase yet — but `PlannerRouter` IS wired into DI. If `PlannerRouter` is used as the active planner and it calls decomposers that in turn invoke `Predict`, the `GetIntArg` JsonElement bug (D2) will produce silent zero-returns for all movement predictions. Classify as blocking because the wiring is in place even if not yet exercised by tests.

**Reclassify D2 → B3.** The fix: add a `JsonElement` branch to `GetIntArg`:
```csharp
if (args.TryGetValue(key, out var v) && v is JsonElement je)
    return je.ValueKind == JsonValueKind.Number ? je.GetInt32() : fallback;
```

**Dissent on Seat 2's B1 severity:** The `ConcurrentQueue` trim race can lose entries but cannot corrupt state (entries are lost, not duplicated or out-of-order). The journal is observability tooling, not a persistence layer. I would classify B1 as deferred rather than blocking — losing a few journal entries above capacity during a burst is acceptable. However, I defer to the council majority.

**Finding D7 (deferred):** `DecomposerRegistry` is a `List<IGoalDecomposer>` guarded by a `ReaderWriterLockSlim` (or similar — need to verify). If it's a plain `List`, concurrent registration is unsafe. Recommend `ConcurrentBag` or `ImmutableList` + swap.

---

### Seat 6 — Synthesizer
**Confidence: 0.84**

**Summary of findings:**

| ID | Seat | Severity | Description |
|---|---|---|---|
| B1 | Architect | BLOCKING | AgentJournal trim race — Interlocked + while-loop can over-drain |
| B2 | Skeptic | BLOCKING | AgentJournal.Clear() non-atomic `_count = 0` |
| B3 | Skeptic (reclassified from D2) | BLOCKING | WorldModel.GetIntArg does not handle JsonElement — predictions return 0 for all numeric args |
| D1 | Archivist | Deferred | Uncertainty not surfaced in /api/agent/status endpoint |
| D3 | Retrieval | Deferred | IAgentJournal missing Count property |
| D4 | Retrieval | Deferred | WorldModel.Reconcile lock inconsistency |
| D5 | HLA | Deferred | No REST/SignalR endpoints for journal or world model data |
| D6 | HLA | Deferred | Nullable IAgentJournal injection — NullAgentJournal pattern recommended |
| D7 | Skeptic | Deferred | DecomposerRegistry thread-safety — verify locking strategy |

**Synthesizer verdict on B1 vs Skeptic dissent:** The trim race (B1) is a correctness defect that creates a misleading journal API surface — `All` and `Recent()` may return inconsistent counts in high-concurrency scenarios. Given that the journal is the primary observability tool, the council classifies B1 as **blocking**. The fix is a single-line change (`Channel<JournalEntry>` + `BoundedChannelFullMode.DropOldest`).

**Sprint 6 passes with 3 blocking findings to fix before merge.**

---

## Blocking Findings — Required Fixes

### B1: AgentJournal trim race
**File:** Agent.Core/Models/AgentJournal.cs  
**Problem:** `_count` is incremented with `Interlocked.Increment`, but the trim while-loop re-reads `_count` with `Volatile.Read` and can race with concurrent `Log()` calls to over-drain the queue.  
**Fix:** Replace `ConcurrentQueue` + manual count with `Channel<JournalEntry>` bounded to `MaxEntries` with `BoundedChannelFullMode.DropOldest`. This pushes trimming into the channel infrastructure atomically. Alternatively, remove the trim loop entirely and accept a soft cap (log entries above MaxEntries are allowed; trim only in `Recent()` / `All` getters using LINQ `Take`).  
**Acceptance criteria:** Under `AgentJournal` stress test (1000 concurrent `Log()` calls), `journal.All.Count <= MaxEntries` always holds after all calls complete.

### B2: AgentJournal.Clear() non-atomic count reset
**File:** Agent.Core/Models/AgentJournal.cs  
**Problem:** `_count = 0` in `Clear()` is a non-volatile write; concurrent `Log()` calls may see a stale count and trigger incorrect trim.  
**Fix:** Replace with `Interlocked.Exchange(ref _count, 0)`.  
**Acceptance criteria:** `Clear()` followed immediately by `Log()` on another thread always results in `Count == 1`.

### B3: WorldModel.GetIntArg does not handle JsonElement
**File:** Agent.Core/Models/WorldModel.cs  
**Problem:** Tool arguments arrive in `ActionData.Arguments` as `object?`. When deserialized from JSON (as in `DispatchActionsAsync`), numeric values are `JsonElement`, not `int`/`long`. `GetIntArg` has branches for `int` and `long` but not `JsonElement`, so all numeric predictions silently use `fallback = 0`.  
**Fix:**  
```csharp
using System.Text.Json;

private static int GetIntArg(IReadOnlyDictionary<string, object?> args, string key, int fallback = 0)
{
    if (!args.TryGetValue(key, out var v)) return fallback;
    return v switch
    {
        int i       => i,
        long l      => (int)l,
        double d    => (int)d,
        JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetInt32(),
        _ => fallback,
    };
}
```
**Acceptance criteria:** `WorldModel.Predict("move", {{"x": JsonElement(5), "y": JsonElement(64), "z": JsonElement(-3)}})` returns a `PredictionState` with `PredictedPosition == (5, 64, -3)`.

---

## Testable Acceptance Criteria (full sprint)

| # | Criterion | Test location |
|---|---|---|
| AC-1 | `AgentJournal` accepts 2000 concurrent `Log()` calls; final `All.Count <= 1000` | MemorySmith.Agent.Tests/AgentJournalTests.cs |
| AC-2 | `AgentJournal.Clear()` then single `Log()` → `All.Count == 1` | AgentJournalTests |
| AC-3 | `IAgentJournal.Query(JournalEntryType.ActionFailed, from: t-1h)` returns only ActionFailed entries in range | AgentJournalTests |
| AC-4 | `WorldModel.Predict("move", {x:5,y:64,z:-3})` returns Position(5,64,-3) | WorldModelTests |
| AC-5 | `WorldModel.Reconcile()` updates `Uncertainty` to a value in [0,1] after 3 calls | WorldModelTests |
| AC-6 | `DecomposerRegistry.CanHandle(new BuildGoal(...))` returns true; decomposer produces non-empty plan | PlannerExtensibilityTests |
| AC-7 | `PlannerRouter` falls back to `HtnPlanner` for an `IGoal` with no registered decomposer | PlannerExtensibilityTests |
| AC-8 | CI build-and-test green (all tests pass, no compiler errors) | GitHub Actions |

---

## Decision

**Sprint 6: CONDITIONAL PASS.** Three blocking findings (B1, B2, B3) must be fixed and CI must pass before PR #1 merges. Deferred findings (D1–D7) are tracked for Sprint 7 backlog.

_Recorded to Data/Pages/council/sprint6-council-20260617.md_
