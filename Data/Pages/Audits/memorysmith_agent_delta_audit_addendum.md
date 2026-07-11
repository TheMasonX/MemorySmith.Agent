# MemorySmith.Agent Delta Audit Addendum

**Repo:** `TheMasonX/MemorySmith.Agent`  
**Branch:** `dev/round-3`  
**Commit:** `eceb01e2a226e3a3445f2e02d019cb3b86b97e7d`

## What is new in this pass

These findings are additional to the prior report. I focused on residual fallback behavior, bridge-layer brittleness, and places where the current task ledger does not yet explicitly cover the risk. The current sprint 59 task list already covers evaluator, inventory sync, execution-manager JSON round-trip, sync-over-async, safety merge, circuit breaker, goal identity, runtime normalization, plan-raw log level, and SignalR event drift, so I avoided duplicating those items. fileciteturn15file0

## New findings

### 1) Memory registry fallback still skips search when local fallback returns whitespace
**Severity:** High  
**Confidence:** 91%

`MemorySmithItemRegistry` now tries a local checked-in page fallback when the remote page fetch returns whitespace, which is a good extension. The remaining edge is that the method still only falls through to search when `content is null`. If the remote fetch returns whitespace and the local file is missing or also whitespace, search is skipped and the method proceeds toward parsing bad content instead of searching for an alternate page. That reintroduces the old null-vs-empty gap through a different branch. fileciteturn97file0

**Why it is a delta:** this is not represented in the current sprint 59 task list, and it is a new behavior introduced by the local-file fallback layer. fileciteturn15file0

**Recommendation:** normalize both remote and local misses into a single “no usable content” state, then let search run from there. Treat `string.IsNullOrWhiteSpace(content)` as a miss all the way through, not just on the first read.

---

### 2) Build-origin handling still accepts partially populated stored facts
**Severity:** High  
**Confidence:** 84%

`BuildGoalDecomposer` now checks origin facts axis-by-axis and supports a stored-origin fallback, which is better than the older sentinel approach. The remaining issue is that there is no completeness gate for the stored-fact path: if only one or two axes exist, the decomposer still proceeds with a mixed origin composed of real values and zero defaults. That is brittle because it silently turns “partially known origin” into a valid-looking coordinate triple. fileciteturn104file0

This extends existing origin cleanup work rather than duplicating it. The current task ledger already contains the explicit build-origin work (`TSK-0095` and `TSK-0103`), so the missing piece is completeness validation for stored facts, not the broader origin refactor itself. fileciteturn105file0turn103file1

**Recommendation:** require all three stored axes before accepting a persisted origin; otherwise force the auto-detect path. A partial origin should be treated as missing, not as `(x, 0, z)`.

---

### 3) Creative provisioning can race `PlaceBlockGoal` failure detection
**Severity:** Medium-High  
**Confidence:** 87%

`SetGoal` triggers creative provisioning asynchronously and also uses a linked cancellation source when a connection CTS exists. `PlaceBlockGoal.HasFailed`, however, still fails immediately when the item is absent from inventory and the inventory is not marked stale. In creative mode, that means the goal can be marked failed before the delayed `/give` provisioning finishes, even though the agent already knows creative provisioning is in flight. fileciteturn44file0turn37file0

The provisioning cancellation logic is also using `CreateLinkedTokenSource` without disposing the linked source when `_connectionCts` exists, which leaks a disposable CTS per goal-start in the connected case. fileciteturn44file0

**Why it is a delta:** Sprint 59 currently tracks command/safety/runtime hardening, but this creative provisioning race and CTS leak are not in that task list. fileciteturn15file0

**Recommendation:** make `PlaceBlockGoal.HasFailed` aware of creative provisioning state, or suppress the failure path until the provisioning loop either completes or is cancelled. Also dispose the linked CTS in the connected case.

---

### 4) Replan context preservation is order-dependent and first-write-wins
**Severity:** Medium  
**Confidence:** 80%

`HtnPlanner.ReplanAsync` preserves context keys by prefix, but it uses `TryAdd` when merging preserved context back into the new plan. That means the first value encountered for a key wins, and later values are silently ignored. The result is order-dependent carry behavior: a context bag can preserve stale values simply because they were encountered earlier in the old action list. fileciteturn24file0turn25file0

This is not the same issue as the already-tracked context-merge bridge. The bridge is intentional for now; the new delta is that the merge semantics are arbitrary and can silently freeze an old value even when a later action wrote a better one. That is especially risky for coordinates and recovery hints. fileciteturn13file0turn25file0

**Recommendation:** make the merge policy explicit. Either move to a typed context object with defined precedence or switch to an intentional last-write-wins merge for the preserved keys.

## Corrections / task-coverage notes

The following are not new findings in this addendum and should stay with the existing task ledger rather than being duplicated here:

- general runtime hardening and evaluator/inventory fixes already mapped into Sprint 59, fileciteturn15file0
- the broader explicit-origin/build-origin refactor, which already has task coverage, fileciteturn105file0turn103file1
- the context-carry bridge itself, which the architecture doc already classifies as temporary. fileciteturn13file0

## Net new action items

1. Normalize remote/local memory-registry misses before search and parse.
2. Require complete stored build origins, not partial axis sets.
3. Make creative provisioning state-aware for `PlaceBlockGoal` and dispose linked CTS instances.
4. Replace order-dependent preserved-context merging with an explicit precedence rule.
