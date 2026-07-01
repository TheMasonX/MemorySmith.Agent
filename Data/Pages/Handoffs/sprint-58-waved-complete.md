# Sprint 58 Wave D — Thread Safety & Observability Fixes

**Date:** 2026-07-01
**Status:** ✅ Complete
**Tests:** 815 passed, 0 failed, 0 skipped
**Build:** ✅ Succeeded

---

## Summary

Wave D addressed 5 remaining audit findings from `internal-audit-57-20260701.md`:
2 P0 (critical), 1 P1 (high), and 2 P2 (medium). Focus was on thread safety,
cancellation hygiene, and inventory diff completeness.

---

## Completed Tasks

| Task | Priority | Title | Files Changed |
|------|----------|-------|--------------|
| **TSK-0330** | P0 | PlaceBlockGoal._dispatched data race → Interlocked | `Agent.Planning/Goals/PlaceBlockGoal.cs`, `WebUI.Blazor/AgentBackgroundService.cs` |
| **TSK-0331** | P0 | Creative provisioning linked CTS | `WebUI.Blazor/AgentBackgroundService.cs` |
| **TSK-0332** | P2 | MineBlock per-tool timeout override (15s) | `WebUI.Blazor/AgentBackgroundService.cs` |
| **TSK-0333** | P2 | _blocksPlacedThisCycle reset at plan cycle start | `WebUI.Blazor/AgentBackgroundService.cs` |
| **TSK-0334** | P1 | WorldStateDiff unexpected inventory detection | `Agent.Core/Models/WorldStateDiff.cs` |

---

## Detailed Changes

### TSK-0330 — PlaceBlockGoal Data Race (P0)
**Files:** `Agent.Planning/Goals/PlaceBlockGoal.cs`, `WebUI.Blazor/AgentBackgroundService.cs`

**Problem:** `_dispatched` was a plain `int` field read by `DispatchActionsAsync` and
written by `ProcessEventsAsync` — two concurrently running async tasks — with no
synchronization. The write could be invisible to the reader, causing the goal to
hang indefinitely.

**Fix:**
- Added `using System.Threading` to `PlaceBlockGoal.cs`
- `Dispatched` getter now uses `Volatile.Read(ref _dispatched)`
- Added `IncrementDispatched()` method using `Interlocked.Increment`
- `IsComplete` uses `Volatile.Read` for thread-safe comparison
- `AgentBackgroundService` now calls `pgGoal.IncrementDispatched()` instead of `pgGoal.Dispatched++`

### TSK-0331 — Creative Provisioning Linked CTS (P0)
**File:** `WebUI.Blazor/AgentBackgroundService.cs`

**Problem:** `SetGoal` called `ProvisionGoalIfCreativeAsync(goal, CancellationToken.None)`.
The TSK-0326 guard (`_currentGoal != goal`) was a best-effort check, but without
proper cancellation, the `Task.Delay(200, ct)` in the provisioning loop wouldn't
abort when the goal changed.

**Fix:**
- Added `_goalProvisioningCts` field (`CancellationTokenSource?`)
- `SetGoal` now cancels/disposes the old CTS and creates a new one, linked to `_connectionCts`
- `CancelGoal` also cancels `_goalProvisioningCts`
- Provisioning loop's `Task.Delay(200, ct)` now respects cancellation

### TSK-0332 — MineBlock Timeout Override (P2)
**File:** `WebUI.Blazor/AgentBackgroundService.cs` — `ToolTimeoutOverrides`

**Problem:** MineBlock was not in `ToolTimeoutOverrides`, defaulting to 30s.
Most mine actions complete in 2-5s; a stuck mine would waste 30s before timing out.

**Fix:** Added `["mine"] = 15` to `ToolTimeoutOverrides`.

### TSK-0333 — _blocksPlacedThisCycle Reset (P2)
**File:** `WebUI.Blazor/AgentBackgroundService.cs`

**Problem:** `_blocksPlacedThisCycle` was only reset when the cycle-complete log
fired. During continuous placement phases (no cycle gap), the counter accumulated
across cycles, inflating the log output.

**Fix:** Reset `_blocksPlacedThisCycle = 0` when a new plan is generated in
`DispatchActionsAsync` (right after `_queue.EnqueueAll`).

### TSK-0334 — WorldStateDiff Unexpected Inventory (P1)
**File:** `Agent.Core/Models/WorldStateDiff.cs` — `HasInventoryMismatch`

**Problem:** `HasInventoryMismatch` only checked that expected gains were met and
expected losses happened. It did NOT flag unexpected inventory changes — items
gained or lost that weren't in either the expected gain or loss lists.

**Fix:** Added a final loop over `ActualInventoryDelta` that checks for keys not
in either `InventoryGained` or `InventoryLost`. Uses a `HashSet<string>` for O(1)
lookup of expected keys.

---

## Files Changed: 4 files

- `Agent.Planning/Goals/PlaceBlockGoal.cs`
- `Agent.Core/Models/WorldStateDiff.cs`
- `WebUI.Blazor/AgentBackgroundService.cs`

---

## Remaining Audit Findings (Deferred)

| Finding | Priority | Reason |
|---------|----------|--------|
| P0-6: ExecutionManager JSON round-trip | P0 | Deferred to Sprint 59 (TSK-0322, deferred since Sprint 40) |
| P1-1: AgentRuntime dead code | P1 | Requires architectural decision; not a bug fix |
| P1-2: EntityObservedEvent bypasses StructuredFacts | P1 | Needs Builder refactor; non-trivial |
| P1-4: PlaceBlockGoalDecomposer same coords | P1 | Design question — offset or single-action? |
| P1-5: BuildGoalDecomposer origin mislabeling | P1 | Needs new enum value + migration |
| P1-7: RememberFactAsync fire-and-forget | P1 | Already has Warning-level logging (fixed in Wave C) |
| P2-2: Entity facts never evicted | P2 | TTL eviction needs design |
| P2-5: HtnPlanner/PlannerRouter overlap | P2 | Architectural clean-up |
| P2-6: Safety config hidden coupling | P2 | Documented limitation |

---

## Sprint 58 Complete — Wave Summary

| Wave | Tasks | P0 | P1 | P2 | P3 |
|------|-------|----|----|----|----|
| A/B | TSK-0316–0319, 0302, 0309–0310, 0312, 0314–0315 | 0 | 4 | 2 | 3 |
| C | TSK-0320–0329 (minus 0322) | 4 | 1 | 4 | 0 |
| D | TSK-0330–0334 | 2 | 1 | 2 | 0 |
| **Total** | **19 tasks** | **6** | **6** | **8** | **3** |

**Deferred to Sprint 59:** TSK-0322, TSK-0311, TSK-0292/0293 + remaining audit findings above.
