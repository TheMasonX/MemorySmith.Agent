# TSK-0125: Replace Linear Build Checkpoint with Per-Block Status Tracking

**Status:** Backlog
**Priority:** High
**Sprint:** 53
**Council:** [build-checkpoint-per-block-council-2026-06-27.md](../council/build-checkpoint-per-block-council-2026-06-27.md)
**Confidence:** 0.80

## Summary

Replace the single-int linear checkpoint (`build:{blueprint}:progress:index`) with per-block status facts (`build:{blueprint}:block:{N}:status`) to correctly track individual block placement status across batch-dispatched PlaceBlock actions.

## Background

The current linear checkpoint cannot represent gaps: when blocks 50, 52, 53 succeed but 51 fails in a batch of 8, the single int either skips 51 permanently (TSK-0124 band-aid) or stalls the entire build. Three workarounds exist to paper over this:
1. TSK-0124 C# skip-handler: advances checkpoint on ALL skips (loses blocks)
2. TSK-0124 JS NATURAL_TERRAIN whitelist: prevents mining blueprint blocks re-emitted by the broken checkpoint
3. Vegetation clearing loop: pre-mines grass/flowers before placement

## Subtasks

### TSK-0125.1 — Characterization Tests (GATE-1, GATE-2, GATE-3)
**Must complete BEFORE any implementation.**

Write tests capturing current behavior:
- `AdvanceBuildCheckpoint` happy path, missing context, duplicate BlockPlacedEvent, terrain-collision skip, bot-position skip
- `DecomposeBuild` checkpoint resume: from 0, from mid-build (index=50), from all-placed
- NATURAL_TERRAIN whitelist: terrain block mined, non-terrain block skipped, cobblestone edge case

### TSK-0125.2 — Add `BuildProgressReport` Model Class
New class in `Agent.Core/Models/`:
- Properties: `BlueprintId`, `TotalBlocks`, `PlacedCount`, `SkippedCount`, `InProgressCount`, `PendingCount`, `PercentComplete`
- Factory method: `FromFacts(WorldState state, string blueprintId)`

### TSK-0125.3 — Implement Per-Block Status Facts
- Add `BuildFactKeys.BlockStatus(blueprintId, blockIndex)` key pattern
- Add status constants: `BlockStatusPending`, `BlockStatusInProgress`, `BlockStatusPlaced`, `BlockStatusSkipped`
- Replace `AdvanceBuildCheckpoint` with `SetBlockStatus(blueprintId, blockIndex, status)`
- Update `EmitBuildPlacementLoop` to read per-block status, skip "placed", re-emit "pending"/"skipped"

### TSK-0125.4 — Add `ClearBuildFacts` Helper
- Method on `WorldState.Builder`: `ClearFactsByPrefix(string prefix)`
- Called on `SetGoal` (new build), `CancelGoal`, build completion
- Removes all `build:{blueprint}:*` facts

### TSK-0125.5 — Add Structured Logging
- Format: `[build] {blueprint}: {placed}/{total} placed, {skipped} skipped, {pending} pending`
- Log on stall, on completion, on each replan summary

### TSK-0125.6 — Fact Capacity Validation (GATE-4)
- Prove: worst-case blueprint (215 blocks) + system facts + 1 concurrent build < 800 facts
- If GATE-4 fails: implement lazy initialization (only create facts for non-default status)

### TSK-0125.7 — State Transition Documentation (GATE-5)
- State transition diagram: pending → in-progress → placed | skipped
- Recovery paths: timeout (in-progress → pending), lost event (in-progress → pending via sweep)
- Commit diagram to `Data/Pages/council/`

### TSK-0125.8 — ClearBuildFacts Semantics (GATE-6)
- Document: when called, what key pattern matched, idempotency guarantee
- One-paragraph spec committed before implementation

## Acceptance Criteria
1. `DecomposeBuild` reads per-block facts, skips "placed", re-emits "pending"/"skipped"
2. `SetBlockStatus` replaces `AdvanceBuildCheckpoint`
3. All 742 existing tests pass
4. All 6 characterization tests pass against both old and new code
5. `ClearBuildFacts("small-house")` removes all `build:small-house:*` facts
6. Structured log: `[build] small-house: 142/215 placed, 18 skipped`
7. `BuildProgressReport` model class computable from WorldState
8. 215-block blueprint ≤ 215 StructuredFacts entries
9. TSK-0124 NATURAL_TERRAIN and checkpoint-advance-on-skip UNCHANGED
10. Missing fact → treated as "pending" (no crash)
11. Implementation ≤300 net-new lines AND ≤2 new classes beyond BuildProgressReport

## Dependencies
- Blocked by: GATE-1, GATE-2, GATE-3 (characterization tests)
- Blocks: TSK-0126 (verification pass + band-aid reversion, next sprint)

## References
- [Council Report](../council/build-checkpoint-per-block-council-2026-06-27.md)
- `Agent.Planning/HtnTaskLibrary.cs:618-675` — EmitBuildPlacementLoop
- `WebUI.Blazor/AgentBackgroundService.cs:1772-1798` — AdvanceBuildCheckpoint
- `Agent.Core/BuildFactKeys.cs:41-43` — BuildProgressIndex key
- `MineflayerAdapter/index.js:769-804` — NATURAL_TERRAIN whitelist
