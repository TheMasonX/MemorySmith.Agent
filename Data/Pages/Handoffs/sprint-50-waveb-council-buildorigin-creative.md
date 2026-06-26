# Sprint 50 Wave B — Council Review + BuildOrigin Migration + Creative Cleanup

**Date:** 2026-06-25
**Branch:** `sprint-35-llm-first`
**Author:** SteveBot
**Tests:** 731 passing, 0 failing

---

## Council Review

Conducted a 7-seat adversarial council review of all remaining backlog tasks for Wave B:

1. **TSK-0107 (Build Origin Sentinel)** — Verified the problem existed (half-done) and completed the migration
2. **TSK-0116 (Creative Build → Decomposer)** — Confirmed HtnPlanner creative branch was dead code, removed it
3. **TSK-0117 (Inventory Reconciliation)** — Verified C# side already done (ApplyCraftComplete, ApplySmeltComplete exist). Adapter already fires sendBotStatus() after craft/smelt
4. **TSK-0093 (ParseItemSpec)** — Deferred (no consumer need since Sprint 43)
5. **TSK-0096 (Mining double-counting)** — Won't Fix (documented tradeoff, no evidence of real harm)
6. **TSK-0118 (Chat split-brain)** — Deferred to Sprint 51 (Sprint 35 already fixed the dangerous paths)

**Council seats:** Source-Grounded Archivist · Data Model Architect · Retrieval Specialist · Human Learning Advocate · Skeptical Reviewer · Synthesizer · Adversarial Provocateur

**Key findings:**
- TSK-0117 C# side was already complete; only adapter-side verification needed
- TSK-0118 was already resolved by Sprint 35 P1-D changes
- TSK-0096's fix was judged more dangerous than the current behavior
- TSK-0107 required careful handling of BuildOriginSource.Explicit vs AutoScanned semantics

---

## Changes Implemented

### TSK-0107: Build Origin Sentinel — Completed BuildOrigin? Migration

**Problem:** `HtnTaskLibrary.DecomposeBuild` accepted `int originX/originY/originZ` and treated `(0,0,0)` as the auto-detect sentinel, making builds at world origin impossible. The XML doc claimed TSK-0107 was done (mentioned `BuildOrigin?`) but the signature still used raw ints.

**Fix:**
1. Changed `DecomposeBuild` signature from `(Blueprint, IReadOnlyList<PlacementBlock>, int originX, int originY, int originZ, WorldState, bool)` to `(Blueprint, IReadOnlyList<PlacementBlock>, BuildOrigin? origin, WorldState, bool)`
2. Updated `BuildGoalDecomposer.Decompose` to construct `BuildOrigin` with proper `BuildOriginSource` enum (Explicit vs AutoScanned) instead of passing raw ints
3. Updated `HtnPlanner.PlanAsync` to construct `BuildOrigin` instead of raw ints
4. Updated all 20+ test callers across 3 test files to use `new BuildOrigin(...)`
5. Origin resolution logic: `BuildOriginSource.Explicit` → use coordinates as-is; non-Explicit with non-zero coords → use verbatim; null or all-zero coords → fall back to `ResolveAutoOrigin` (backward compatible)

**Files changed:**
- `Agent.Planning/HtnTaskLibrary.cs` — method signature + body
- `Agent.Planning/Decomposition/BuildGoalDecomposer.cs` — caller updated
- `Agent.Planning/HtnPlanner.cs` — caller updated
- `MemorySmith.Agent.Tests/HtnTaskLibraryExtraTests.cs` — 11 test callers updated
- `MemorySmith.Agent.Tests/HtnTaskLibraryCraftingTests.cs` — 14 test callers updated
- `MemorySmith.Agent.Tests/Sprint36Tests.cs` — 3 test callers updated

### TSK-0116: Creative-Mode Build — Dead Code Removal

**Problem:** `HtnPlanner.CreateCreativeBuildActions` generated creative-mode build actions, but `PlannerRouter` prefers `BuildGoalDecomposer` which routes through `HtnTaskLibrary.DecomposeBuild` — which already handles creative mode internally. The HtnPlanner creative branch was dead code.

**Fix:** Removed `CreateCreativeBuildActions` and `TryGetIntFactFromState` methods from HtnPlanner. Removed `if (state.IsCreativeMode)` branch. Removed unused `using System.Text.Json` and `using Agent.Construction`.

**Files changed:**
- `Agent.Planning/HtnPlanner.cs` — removed ~60 lines of dead code

### TSK-0117: Inventory Reconciliation — Verified Complete

**Verification:** `WorldStateProjector.cs` already has `ApplyCraftComplete` and `ApplySmeltComplete` with TSK-0117 markers. The adapter `MineflayerAdapter/index.js` already calls `sendBotStatus()` after craft and smelt completion events (Sprint 35 P0-A). No code changes needed — task confirmed complete.

---

## Remaining Deferred Tasks

| Task | Title | Priority | Reason |
|:---|---|:---:|---|
| TSK-0118 | Chat interpretation split-brain cleanup | P2 | Sprint 35 already fixed dangerous paths. Defer to Sprint 51 for janitorial cleanup |
| TSK-0093 | Structured ParseItemSpec result | Med | No consumer need since Sprint 43 audit. Logging-only fix sufficient |
| TSK-0096 | Mining inventory double-counting | Med | **Won't Fix.** Documented tradeoff in WorldStateProjector.cs (lines 106-117). Over-count is safer than under-count. Periodic GetStatus reconciles drift |

---

## Build & Tests

```
Build succeeded. 0 Error(s)
Passed!  - Failed: 0, Passed: 731, Skipped: 0, Total: 731
```
