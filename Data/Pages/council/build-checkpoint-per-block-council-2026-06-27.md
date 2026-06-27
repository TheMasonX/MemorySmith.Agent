# Council Review: Replace Linear Build Checkpoint with Per-Block Status Tracking

**Date:** 2026-06-27
**Method:** 5-seat council (Source-Grounded Archivist, Data Model Architect, Skeptical Reviewer, Human Learning Advocate, Synthesizer)
**Decision Confidence:** 0.80

## Decision

Replace the linear build checkpoint (`build:{blueprint}:progress:index`) with per-block status tracking (`build:{blueprint}:block:{N}:status`) in this sprint, keeping TSK-0124 band-aids as safety nets, and defer the verification pass and band-aid reversion to the next sprint after characterization tests prove correctness.

## Evidence Reviewed

- `Agent.Planning/HtnTaskLibrary.cs` — DecomposeBuild, EmitBuildPlacementLoop, EmitSurvivalMaterialProvisioning
- `Agent.Core/BuildFactKeys.cs` — checkpoint key patterns, auto-origin keys
- `WebUI.Blazor/AgentBackgroundService.cs` — AdvanceBuildCheckpoint, BlockPlacedEvent handler, BlockPlaceSkippedEvent handler, batch dispatch
- `Agent.Construction/BlueprintExecutor.cs` — block ordering, coordinate mapping
- `Agent.Core/Models/WorldState.cs` — MaxFacts=1000, StructuredFacts eviction
- `MineflayerAdapter/index.js` — place handler, NATURAL_TERRAIN whitelist, step-aside, scaffold
- `Data/Pages/blueprints/small-house.md` — 215-block test blueprint
- `Data/Pages/architecture.md` — runtime flow, bounded contexts
- `MineflayerAdapter/logs/adapter-2026-06-27.log` — destructive mining evidence (oak_door→oak_planks, etc.)
- `WebUI.Blazor/logs/memorysmith-agent-20260627.log` — stall loop evidence

## Findings

| Seat | Recommendation | Confidence | Blocking Concern |
|---|---|---|---|
| Source-Grounded Archivist | Proceed with per-block tracking. Linear checkpoint is structurally insufficient per code evidence. Write characterization tests FIRST. | 0.82 | Fact explosion risk for blueprints >400 blocks |
| Data Model Architect | Add 4th state ("in-progress"). Strongly recommend compact encoding (bitmask + skipped-set). Fact budget will be exhausted at ~400 blocks. | 0.72 | MaxFacts=1000 is a hard cap; GATE-4 must prove capacity |
| Skeptical Reviewer | Consider simpler alternatives (sorted-set of placed indices). Keep NATURAL_TERRAIN whitelist. Drop verification pass — adapter's "already placed" check handles this. | 0.62 | Zero test coverage for affected code. AC-11 kill-switch at 300 lines. |
| Human Learning Advocate | Per-block tracking is clearer than current 3-workaround system. Add BuildProgressReport class + structured logging. Keep NATURAL_TERRAIN visible. | 0.78 | Reverting NATURAL_TERRAIN removes a visible safety net |

## Implementation Gates

| Gate | Description |
|---|---|
| GATE-1 | Characterization tests for AdvanceBuildCheckpoint: happy path, missing context, duplicate event, terrain skip, bot-position skip |
| GATE-2 | Characterization tests for DecomposeBuild checkpoint resume: from 0, mid-build, all-placed |
| GATE-3 | Characterization tests for NATURAL_TERRAIN whitelist behavior |
| GATE-4 | Fact capacity analysis: prove worst-case < 800 facts |
| GATE-5 | State transition diagram: pending→in-progress→placed/skipped |
| GATE-6 | ClearBuildFacts semantics document |

## What Changes NOW

- Per-block status facts (`build:{blueprint}:block:{N}:status`) with 4 states: pending, in-progress, placed, skipped
- `SetBlockStatus` replaces `AdvanceBuildCheckpoint`
- `ClearBuildFacts` helper for fact lifecycle management
- `BuildProgressReport` model class
- Structured log format: `[build] small-house: 142/215 placed, 18 skipped`
- `DecomposeBuild` reads per-block facts, skips "placed", re-emits "pending"/"skipped"
- TSK-0124 NATURAL_TERRAIN whitelist and checkpoint-advance-on-skip REMAIN (do NOT revert)

## What Is DEFERRED

- Revert TSK-0124 NATURAL_TERRAIN whitelist → next sprint
- Verification pass ("mine if different") → next sprint
- Compact encoding (bitmask + skipped-set) → only if GATE-4 fails
- Dashboard block-status grid → after BuildProgressReport ships
- Integration test with simulated batch dispatch → after per-block tracking is stable
- Spatial ordering TSK-0179 evaluation → next sprint

## Consolidated Risks

| # | Risk | Severity | Blocking? |
|---|---|---|---|
| R1 | Fact explosion exceeds MaxFacts=1000 | HIGH | YES |
| R2 | Zero test coverage for checkpoint code | CRITICAL | YES |
| R3 | "Mine if different" unsafe without planner guarantee | HIGH | YES |
| R4 | Per-block tracking complexity > value | MEDIUM | NO |
| R5 | Lost WebSocket events orphan "in-progress" blocks | MEDIUM | NO |
| R6 | Batch dispatch races | MEDIUM | NO |
| R7 | App restart loses all progress | LOW | NO |

## Dissent

- **Data Model Architect ↔ Skeptical Reviewer**: Per-block facts vs sorted-set-of-placed-indices. AC-11 kill-switch resolves: if implementation exceeds 300 lines or 2 new classes, halt and prototype alternative.
- **Human Learning Advocate ↔ Source-Grounded Archivist**: Revert NATURAL_TERRAIN now vs keep. Resolved: keep, defer reversion to next sprint.
- **Skeptical Reviewer ↔ All**: Is per-block tracking the simplest fix? AC-11 provides bounded experiment.

## Acceptance Criteria

1. `DecomposeBuild` reads per-block facts, skips "placed", re-emits "pending"/"skipped"
2. `SetBlockStatus` replaces `AdvanceBuildCheckpoint`
3. All 742 existing tests pass
4. All 6 characterization tests (GATE-1 through GATE-3) pass against both old and new code
5. `ClearBuildFacts` removes all `build:{blueprint}:*` facts, leaves others intact
6. Structured log: `[build] small-house: 142/215 placed, 18 skipped, 55 pending`
7. `BuildProgressReport` model class with 7 properties
8. 215-block blueprint ≤ 215 StructuredFacts entries
9. TSK-0124 NATURAL_TERRAIN and checkpoint-advance-on-skip UNCHANGED
10. Missing fact → treated as "pending" (no crash)
11. Implementation ≤300 net-new lines AND ≤2 new classes beyond BuildProgressReport

## Open Questions

1. What is the typical blueprint size? (affects MaxFacts risk)
2. Should timed-out PlaceBlock actions auto-transition to "skipped"?
3. Should verification pass use same PlaceBlock or distinct VerifyBlock action?
4. Player-bot collaborative building: what if player modifies placed blocks mid-build?
5. Does EmitBuildPlacementLoop still need bot-position self-skip with per-block tracking?
6. Can spatial ordering (TSK-0179) eliminate the need for verification pass entirely?
