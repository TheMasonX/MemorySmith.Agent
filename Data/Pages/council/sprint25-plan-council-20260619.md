# MemorySmith Council Review — Sprint 25 Plan

**Date**: 2026-06-19
**Subject**: Sprint 25 plan: "Tool Boundary Hardening + Action Lifecycle"
**Input documents**:
- `Data/Pages/council/deep-code-audit-20260619.md` (external audit)
- `Data/Pages/council/reliability-report-20260619.md` (external audit)
- `Data/Pages/Tasks/agent-handoff-sprint25.md` (synthesized sprint plan)
- Independent source-level verification by 3 parallel agents

**Review type**: Sprint plan approval (pre-implementation)

---

## Council Seats

### 1. Source-Grounded Archivist
**Confidence: 92%**

The sprint plan correctly traces every item to its source: both external audits are cited with finding numbers and confidence levels, Sprint 24 carry-forwards are mapped with original IDs, and the human reviewer annotations are incorporated. The cross-audit convergence table is a strong addition — it shows where both audits independently arrived at the same conclusion.

**Verified claims:**
- FindFlatAreaTool defaults 20/9 vs JS 32/25 — confirmed stale in source
- ToolDispatcher integer check uses `Contains('.')` — confirmed in `CheckType` method
- ToolDispatcher.CallAsync lacks try/catch — confirmed in source
- WorldModel constructor shares mutable dict — confirmed in source
- StatusTool and GetStatusTool coexist — confirmed in source
- Sprint 24 fully unstarted — confirmed via commit history (15 doc-port commits only)
- LLM blocking refuted — confirmed Channel pattern exists

**Concern (non-blocking):** The plan references "GatherGoalDecomposer TargetCount fix" as P1-C but notes the file doesn't exist on the branch. The handoff correctly flags this as needing investigation, but the item should not be counted toward test targets until the file's location is confirmed.

**Vote: APPROVE**

---

### 2. Data Model Architect
**Confidence: 88%**

The P0-D (PendingAction + ActionLifecycle) design is architecturally sound. Using a `ConcurrentDictionary<Guid, PendingAction>` to replace the current `List<ActionData>` is the right move — it provides O(1) lookup by correlation ID and is thread-safe without external locking.

**Strengths:**
- The ActionLifecycle enum (Dispatched, Acknowledged, Completed, Failed, TimedOut) correctly models the 5 states the reliability report identified
- Injecting correlationId into ActionData.Context is backward-compatible — existing code that doesn't use it won't break
- JS echo is the right approach — the adapter already has structured event emission

**Concerns:**
- **(B-1, blocking):** The plan doesn't specify a timeout mechanism for PendingActions stuck in `Dispatched` state. The existing per-action timeout (30s from Sprint 5) in DispatchActionsAsync is at the CancellationToken level — it cancels the dispatch call, but what about actions that were dispatched successfully and then never get a result event? The PendingAction needs its own timeout sweep, either via a periodic check or by integrating with the existing governor cycle.
- **(D-1, deferred):** The plan correctly defers full WorldModel immutability to Sprint 26, but the P1-A fix (defensive copy in constructor + Observe) is a good incremental step. Consider also making the `Predict` method copy-safe.

**Vote: APPROVE with B-1 (add PendingAction timeout sweep specification)**

---

### 3. Retrieval Specialist
**Confidence: 85%**

The plan's search/retrieval implications are well-scoped. The action correlation ID system will make debugging significantly easier — currently, when a tool returns "dispatched" and the world event never arrives, there's no way to trace the gap. The correlationId bridges that.

**Strengths:**
- The startup constant log (P2-A) will help operators understand the agent's configuration without reading source
- The world KB routing from Sprint 23 is preserved and not disrupted by any Sprint 25 changes

**Concerns:**
- **(D-2, deferred):** The end-to-end gather test (P1-D) will need a fake IWorldAdapter that emits events with correlationIds. If P0-D lands first, P1-D should use the new correlation infrastructure rather than bypassing it.
- **(D-3, deferred):** No mention of updating SearchMemoryTool or GetPageTool error handling — those tools also throw on missing args (SearchMemoryTool throws ArgumentException per the deep audit). If P0-C wraps CallAsync in try/catch, those throws will be caught, but the tools should also be updated to return ToolResult failures directly.

**Vote: APPROVE**

---

### 4. Human Learning Advocate
**Confidence: 90%**

The sprint plan is unusually well-documented for a planning artifact. The cross-audit convergence table, the carry-forward mapping, and the explicit "refuted" finding (LLM blocking) demonstrate critical evaluation rather than blind acceptance of external audit findings.

**Strengths:**
- Clear test names for every P0 item make implementation unambiguous
- The human reviewer annotations are incorporated directly into priority decisions
- The deferred items table explains *why* each is deferred, not just that it is

**Concerns:**
- **(D-4, deferred):** The plan should note that the external audits' "priority order" suggestions differ slightly from the sprint priorities. The deep audit suggested world model immutability as #2, while the sprint plan has it at P1-A. This is a deliberate prioritization choice (tool safety first is correct), but it should be acknowledged in the handoff so the next agent understands the tradeoff.

**Vote: APPROVE**

---

### 5. Skeptical Reviewer
**Confidence: 82%**

The plan absorbs too much from Sprint 24 without questioning whether all items are still correctly scoped. Some specific challenges:

**Concerns:**
- **(B-2, blocking):** P0-D (correlation IDs) is a large item touching C# core, background service, and JS adapter. The 4-test specification may be insufficient — what about: (a) concurrent dispatch of multiple actions with different correlationIds, (b) adapter crash mid-action leaving PendingActions orphaned, (c) duplicate correlationId in events (malformed adapter)? I count at least 7 meaningful test scenarios for this feature. The sprint should either scope P0-D to the infrastructure (records + DI) with event echo deferred to P1, or commit to thorough test coverage.
- **(D-5, deferred):** The plan says GatherGoalDecomposer "file not found on branch" and suggests P1-C may need investigation. But if the file doesn't exist, this is a *create* task, not a *fix* task. The scope is underestimated — creating a new decomposer that passes TargetCount requires understanding the DecomposerRegistry contract, the IGoalDecomposer interface, and how it interacts with PlannerRouter. This should be re-scoped or deferred.
- **(D-6, deferred):** The StatusTool dedup (P0-B) is listed as P0 but is low-risk compared to P0-C and P0-D. It could safely be P1 to reduce the blocking-item count.

**Vote: APPROVE with B-2 (expand P0-D test coverage specification to at least 7 scenarios)**

---

### 6. Synthesizer
**Confidence: 88%**

**Overall assessment:** This is a well-sourced sprint plan that correctly synthesizes two independent external audits with unfinished Sprint 24 backlog. The independent verification adds significant confidence — the "refuted" finding on LLM blocking demonstrates intellectual honesty and prevents wasted implementation effort.

**Key synthesis points:**
1. Both audits converge on tool safety as the top priority — this is rare and should be trusted
2. The reliability report's "dispatched != done" insight is the most impactful new finding (P0-D)
3. Sprint 24's unimplemented state means Sprint 25 is realistically a 2-sprint scope compressed into 1
4. The plan wisely defers the larger refactors (full immutability, planner consolidation, adapter reconnection) while addressing their most critical symptoms

**Blocking findings to resolve:**
- B-1 (Data Model Architect): Specify PendingAction timeout sweep mechanism
- B-2 (Skeptical Reviewer): Expand P0-D test coverage to 7+ scenarios

**Deferred findings (6):**
- D-1: Predict method copy-safety in WorldModel
- D-2: P1-D should use correlation infrastructure from P0-D
- D-3: SearchMemoryTool/GetPageTool should return ToolResult failures (not throw)
- D-4: Acknowledge audit priority ordering differences in handoff
- D-5: GatherGoalDecomposer P1-C may need re-scoping as a create task
- D-6: StatusTool dedup could be P1 instead of P0

**Average confidence: 87.5%**

**Council verdict: APPROVED with 2 blocking findings**

---

## Blocking findings resolution requirements

### B-1: PendingAction timeout sweep
**Owner**: Implementing agent
**Requirement**: Add specification to P0-D for a periodic timeout sweep. Recommendation: In the governor cycle (or a dedicated sweep in DispatchActionsAsync), check all PendingActions in `Dispatched` state older than the existing 30s per-action timeout. Transition to `TimedOut` state and log warning. Add test: `PendingAction_StaleDispatch_TimesOutAfter30s`.

### B-2: P0-D test coverage expansion
**Owner**: Implementing agent
**Requirement**: Expand P0-D test list to at least 7 scenarios:
1. `PendingAction_LifecycleTransition_DispatchedToCompleted`
2. `PendingAction_Timeout_MarkedTimedOut`
3. `PendingAction_UnknownCorrelation_Ignored`
4. `Shutdown_PendingActions_LoggedAsAbandoned`
5. `PendingAction_ConcurrentDispatch_IndependentTracking`
6. `PendingAction_StaleDispatch_TimesOutAfter30s` (B-1 sweep)
7. `PendingAction_DuplicateCorrelationInEvent_HandledGracefully`

---

## Post-council sprint plan status

The sprint plan (`agent-handoff-sprint25.md`) is approved for implementation once B-1 and B-2 are incorporated into the handoff document. No code changes required before starting — the blocking findings are specification additions, not design rejections.
