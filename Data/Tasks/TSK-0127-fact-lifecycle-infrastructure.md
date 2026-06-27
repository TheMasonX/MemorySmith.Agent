# TSK-0127: WorldState Fact Lifecycle Infrastructure (Council Discovery)

**Status:** Backlog
**Priority:** Medium
**Sprint:** TBD
**Discovered:** Council review 2026-06-27 (Data Model Architect seat)
**Council:** [build-checkpoint-per-block-council-2026-06-27.md](../council/build-checkpoint-per-block-council-2026-06-27.md)

## Summary

The council review uncovered two pre-existing infrastructure gaps that affect the feasibility of per-block tracking (TSK-0125):

1. **No fact deletion mechanism exists.** The only fact removal in the codebase is `facts.Remove("world:gamemode")` in `WorldState.Builder.SetGameMode`. There is no `RemoveFact`, `DeleteFact`, or `ClearFactsByPrefix` method. Build facts from completed/cancelled goals persist indefinitely, consuming the MaxFacts=1000 budget.

2. **Diagnostic fact inflation.** Every `BlockPlacedEvent` writes 4 diagnostic facts (`event:BlockPlaced:X`, `:Y`, `:Z`, `:Block`). With 215 block placements, that's 860 diagnostic facts — already approaching MaxFacts without any per-block tracking facts. Combined with per-block status facts, the total would exceed 1000.

## Subtasks

### TSK-0127.1 — Add `ClearFactsByPrefix` to WorldState.Builder
- Method: `WorldState.Builder ClearFactsByPrefix(string prefix)`
- Removes all facts whose key starts with `prefix`
- Used by: TSK-0125.4 (ClearBuildFacts), SetGoal (clear old build), CancelGoal

### TSK-0127.2 — Suppress Diagnostic Facts During Active Builds
- `event:BlockPlaced:*` facts write 4 per placement — 860 for a 215-block build
- Option A: Suppress when `_currentGoal is BuildGoal`
- Option B: Use LogDebug instead of fact writes during bulk operations
- Option C: Write compressed summary fact (`event:BlockPlaced:Count = 215`) instead of per-placement facts

### TSK-0127.3 — Fix `_placeBlockContexts` Leak (Sprint 44 P1-2)
- Add robust cleanup in `SweepTimedOutActions`
- Remove entries older than MaxActionLifetime (30s) regardless of correlation state
- Add `ClearBuildContexts(string blueprintId)` for explicit cleanup on goal transitions

## References
- `Agent.Core/Models/WorldState.cs:94` — StructuredFacts FIFO eviction
- `Agent.Core/Models/WorldState.cs:55` — only fact removal (SetGameMode)
- `WebUI.Blazor/AgentBackgroundService.cs:1888` — SweepTimedOutActions
- [Council Report](../council/build-checkpoint-per-block-council-2026-06-27.md) — Risk R1, Finding 2
