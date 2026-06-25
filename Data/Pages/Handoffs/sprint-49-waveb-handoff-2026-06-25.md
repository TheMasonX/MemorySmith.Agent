# Sprint 49 Wave B — Audit-Driven Fixes Handoff

**Date:** 2026-06-25  
**Branch:** `sprint-35-llm-first` (`b9d75ff`)  
**Author:** SteveBot  
**Tests:** 722 passing, 0 failing

---

## Summary

Reviewed three external audit reports and synthesized their findings into a prioritized task list. Implemented 5 fixes and created 4 deferred tasks.

### Audits Reviewed

| Audit | Focus | Key Findings |
|---|---|---|
| Supplemental Audit | New runtime/architecture issues not in prior report | 4 findings (2 High, 2 Med) |
| Deep Code Audit | Architecture-first, fault-path focused | 6 findings, 5 ranked High |
| Sprint 35 LLM-first Audit | LLM-first architecture compliance | 8 findings, 4 High risk |

### Consolidated Findings → Tasks Created

| Task | Title | Priority | Status |
|:---|---|:---:|:---:|
| TSK-0106 | Fix BlockNotFound retry count string/int mismatch | High | **Done** |
| TSK-0108 | Fix mining inventory prediction — shared BlockToItemDrop mapping | High | **Done** |
| TSK-0111 | Fix memory gateway write path — narrow exception handling | High | **Done** |
| TSK-0109 | Fix blueprint repository cancellation swallowing | Med | **Done** |
| TSK-0114 | Update README to current sprint state | Med | **Done** |
| TSK-0107 | Fix build origin sentinel — eliminate (0,0,0) overload | High | **Deferred** |
| TSK-0110 | Fix structured tool outcomes — preserve ActionOutcome semantics | Med | **Deferred** |
| TSK-0112 | Fix WebSocket clean shutdown — complete inbound channel | Med | **Deferred** |
| TSK-0113 | Fix ActionQueue race-prone surface area | Low | **Deferred** |
| TSK-0082 | Extract shared SmeltableMapping class (pre-existing backlog) | High | **Backlog** |
| TSK-0093 | Structured parse result from ParseItemSpec (pre-existing backlog) | Med | **Backlog** |
| TSK-0096 | Mining inventory double-counting dedup (pre-existing backlog) | Med | **Backlog** |

---

## Changes Implemented

### TSK-0106: BlockNotFound Retry Count Fix
**File:** `WebUI.Blazor/AgentBackgroundService.cs`

The `TryRouteAsError` method was reading the BlockNotFound retry counter with `pc is int pci` but storing the value via `.ToString()` (string). The type mismatch meant the counter never incremented past 1, defeating the progressive wander radius design (40→80→120 blocks).

**Fix:** Changed read to `pc is string pcs && int.TryParse(pcs, out var pci)`.

### TSK-0108: Mining Inventory Prediction Alignment
**Files:** `Agent.Core/CommonMinecraftBlocks.cs`, `Agent.Core/WorldStateProjector.cs`, `Agent.Core/Models/WorldModel.cs`

`WorldModel.PredictMine` was adding the raw block name to predicted inventory (e.g. `diamond_ore` → +1 diamond_ore) while `WorldStateProjector.ApplyBlockMined` correctly mapped to item drops (e.g. `diamond_ore` → +1 diamond). Prediction and projection disagreed on drops, degrading replanning quality.

**Fix:** 
- Extracted `SelfDroppingBlocks` and `BlockToItemDrop` from `WorldStateProjector` into `CommonMinecraftBlocks`
- Added `CommonMinecraftBlocks.ResolveBlockDrop(blockName)` as the canonical resolver
- Both projector and predictor now use the shared resolver

### TSK-0111: Memory Gateway Exception Narrowing
**File:** `Agent.Memory/RestMemoryGateway.cs`

`UpdatePageAsync` caught ALL exceptions when fetching existing page metadata, logging "404 or parse error", then unconditionally issuing a PUT upsert. Auth failures, timeouts, and 500s were all flattened into unintended writes.

**Fix:** Only catch `HttpRequestException` with `StatusCode == NotFound`. All other exceptions propagate.

### TSK-0109: Blueprint Repository Cancellation
**File:** `Agent.Memory/MemorySmithBlueprintRepository.cs`

Five catch blocks across `GetAsync` and `SearchAsync` were swallowing `TaskCanceledException` and continuing into fallback I/O. Caller cancellation was not honored.

**Fix:** Added `catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }` before every existing catch block.

### TSK-0114: README Update
**File:** `README.md`

Updated version string from `v0.35.0` / Sprint 35 / 501+ tests → `v0.49.0` / Sprint 49 / 722+ tests.

---

## Deferred Tasks (Next Wave)

### TSK-0107: Build Origin Sentinel (High)
The most impactful deferred fix. `HtnTaskLibrary.DecomposeBuild` still checks `(0,0,0)` as the auto-detect sentinel, making legitimate builds at world origin impossible. Requires threading `BuildOrigin?` through the build planning pipeline.

### TSK-0110: Structured Tool Outcomes (Medium)
`ActionOutcome` has rich `OutcomeType` enum but `CallWithOutcomeAsync` collapses it to binary success/failure. Recovery/replanning cannot distinguish blocked from unreachable from timed out.

### TSK-0112: WebSocket Clean Shutdown (Medium)
Inbound channel only completed after retry exhaustion, not on normal disconnect. Leaves readers suspended on clean shutdowns.

### TSK-0113: ActionQueue Race Protection (Low)
`ConcurrentQueue`-backed queue has inconsistent lock coverage. Some operations can observe stale state around clears and bulk enqueues.

---

## Pre-existing Backlog (Not Started)

| Task | Title | Priority | Notes |
|:---|---|:---:|---|
| TSK-0082 | Extract shared SmeltableMapping class | High | Ore→ingot mapping in 2 places |
| TSK-0093 | Structured parse result from ParseItemSpec | Med | NotFound vs Malformed |
| TSK-0096 | Mining inventory double-counting dedup | Med | BlockMined + ItemCollected both increment |

---

## Commit

```
b9d75ff on sprint-35-llm-first
19 files changed, 956 insertions(+), 82 deletions(-)
Pushed to origin/sprint-35-llm-first
```
