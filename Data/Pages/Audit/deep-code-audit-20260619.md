# External Audit: Deep Code Audit Report
**Original document date**: 2026-06-19  
**Filed to repo**: 2026-06-19 (Sprint 26 audit intake)  
**Source**: Independent external code reviewer (anonymous)  
**Scope**: PR #1 (`sprint-5-tool-safety`) — architectural seams, type safety, module coherence

---

## Original Findings with Cross-Verification Annotations

### Finding 1: Tool schema validation too permissive for integers
**Original confidence: 92%**  
**Verification status: RESOLVED (Sprint 25 P0-C)**

`ToolDispatcher.ValidateAgainstSchema` / `CheckType` previously used `GetRawText().Contains('.')` to detect non-integers. This was correctly identified as failing for scientific notation (`1e20`). Fixed in Sprint 25 P0-C: `CheckType` now uses `!value.TryGetInt32(out _)` which correctly rejects all non-integer JSON numbers. Code verified at current branch HEAD (ToolDispatcher.cs, SHA e7ea0a93).

*Annotation: Finding was accurate pre-Sprint-25. Now closed.*

---

### Finding 2: ToolDispatcher assumes tools do not throw
**Original confidence: 88%**  
**Verification status: RESOLVED (Sprint 25 P0-C)**

`CallAsync` now wraps `tool.ExecuteAsync(...)` in try/catch. `OperationCanceledException` re-throws (correct); all other exceptions become `ToolResult(false, "Tool '{name}' threw: {ex.Message}")`. Journal entry added on exception. Code verified at branch HEAD.

*Annotation: Finding was accurate pre-Sprint-25. Now closed.*

---

### Finding 3: WorldModel state aliasing
**Original confidence: 86%**  
**Verification status: RESOLVED (Sprint 25 P1-A)**

Constructor now creates separate `new Dictionary<string, int>()` instances for `_observed.Inventory` and `_belief.Inventory`. `Observe()` deep-copies: `new Dictionary<string, int>(observation.Inventory)`. Code verified at branch HEAD (WorldModel.cs, SHA e9a3d0af). The observation and belief layers are now isolation-safe at the inventory boundary.

*Annotation: Finding was accurate pre-Sprint-25. Now closed.*

Residual note: `ObservationState.RecentObservations` is an `IReadOnlyList<Fact>` but the underlying list origin is caller-controlled. For the current code paths (WorldStateProjector → Observe) this is safe; if future callers pass mutable lists, the `ToList()` in Observe creates a copy so it remains safe. Full immutability (copy-on-write at projector boundary) is Sprint 26 P2 scope.

---

### Finding 4: Journal approximately bounded under contention
**Original confidence: 72%**  
**Verification status: OPEN — DELIBERATE DESIGN**

`AgentJournal` uses ConcurrentQueue with single-dequeue trim on `Count > MaxEntries`. The comment explicitly marks this as best-effort. The B1/B2 fix from Sprint 6 tightened the trim from a race-prone Clear to a single-dequeue-under-lock-equivalent approach using `Interlocked.Exchange`. The journal is an operational log, not a reliable event store. This is an intentional architecture decision deferred as "Journal semantics" (Sprint 26 P1-C).

*Annotation: Finding accurately described the design tradeoff. Sprint 26 P1-C will formally record the decision.*

---

### Finding 5: Planner routing split across two modules
**Original confidence: 81%**  
**Verification status: OPEN — Sprint 26 P1-C target**

`HtnPlanner` retains hardcoded type-switch decomposition (IItemSpecGoal, BuildGoal, CraftItemGoal branches). `PlannerRouter` adds the registry layer on top. Two places own decomposition logic. The sprint-26 plan is to route ALL decomposition through `DecomposerRegistry` by creating a `CraftItemGoalDecomposer` and deleting the hardcoded HtnPlanner branches.

Additionally, new Sprint 26 investigation finding: `IItemSpecGoal` interface lacks `TargetCount`, requiring callers to cast to `GenericGatherGoal` to access count. This results in `GatherGoalDecomposer.IItemSpecGoal` catch-all arm using `Array.Empty<string>()` instead of the actual target count. Fix: add `int TargetCount => 1;` as a default interface method to `IItemSpecGoal`. See Sprint 26 P0-B.

---

### Finding 6: Mineflayer chat filter brittle
**Original confidence: 65%**  
**Verification status: OPEN — deferred**

Nine SYSTEM_MESSAGE_PATTERNS regexes plus the enhancements in Sprints 20–21 (Cleared tightening, /clear alt, /give alt) are in place. The reviewer is correct that this will need ongoing maintenance. A structured message classifier is the long-term solution but is out of Sprint 26 scope. Filed as a deferred risk (DEF-2 in Sprint 26 backlog).

---

## Architecture and Codebase Health Opportunities — Status

| Opportunity | Sprint 26 Status |
|---|---|
| A. Strongly-typed tool execution at seam | P2 — `TryGetInt32` closes the numeric gap; full typed dispatch is future |
| B. WorldModel full immutable snapshots | P2 — P1-A closes the aliasing gap; copy-on-write at projector boundary deferred |
| C. Collapse planner selection into one place | P1-C — Sprint 26 target (CraftItemGoalDecomposer + remove HtnPlanner branches) |
| D. Journal semantics decision | P1-C — Sprint 26 target (record decision in architecture.md) |

---

## Open Questions — Answers

| Question | Answer |
|---|---|
| Should tool execution be allowed to throw? | No — `ToolResult` is the only failure channel. Sprint 25 P0-C enforces this. |
| Is WorldModel intended to preserve historical snapshots immutably? | Current: aliasing fixed at Observe boundary; full historical immutability is P2. |
| Should PlannerRouter fully replace HtnPlanner hardcoded branches? | Yes — Sprint 26 P1-C target. |
| Is the journal a bounded buffer or durable event store? | Bounded diagnostic buffer. Sprint 26 P1-C will document this explicitly. |

---

## References
- Sprint 25 handoff: `Data/Pages/Tasks/agent-handoff-sprint26.md`
- Related exec summary: `Data/Pages/Audits/exec-summary-audit-20260619.md`
- Sprint 26 council: `Data/Pages/council/sprint26-audit-council-20260619.md`
