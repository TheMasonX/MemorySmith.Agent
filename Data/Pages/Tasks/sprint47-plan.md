# Sprint 47 Plan — "Architecture Consolidation"

**Date:** 2026-06-24
**Based on:** Audit findings from Sprint 46 audits (6 documents) and Sprint 46 Wave C council

## Theme

Consolidate the architecture: fix structural correctness bugs discovered in the Sprint 46 audits, begin runtime decomposition, and reduce architectural debt from the deterministic→LLM-first migration.

## Wave A — Correctness (P1)

| Task | Priority | Description | Audit Confidence |
|---|---|---|---|
| **TSK-0112**: Fix CraftItem prerequisite count scaling | P1 | Multiply iron/stone/cobble needs by craft count | 96% |
| **TSK-0114**: Preserve structured exception metadata in ToolDispatcher | P1 | Store exception type, stack trace, tool name, correlation in journal | 93% |
| **TSK-0117**: Post-craft/post-smelt inventory reconciliation | P1 | Force `sendBotStatus` after craft/smelt completion | 98% |
| **TSK-0093**: ParseItemSpec structured result | P1 deferred | Return `ParseResult<T>` to distinguish not-found vs malformed | — |

## Wave B — Consistency (P2)

| Task | Priority | Description | Audit Confidence |
|---|---|---|---|
| **TSK-0113**: Add drop-resolution table for mined blocks vs items | P2 | Shared `IBlockDropResolver` for projection + prediction | 88% |
| **TSK-0116**: Move creative-mode build into decomposer layer | P2 | Ensure creative mode works through router path | 90% |
| **TSK-0118**: Resolve chat interpretation split-brain | P2 | Route all intents through IntentDraft; retire old direct-goal path | 99% |
| **TSK-0115**: Unify ActionQueue synchronization | P2 | Single lock for all mutating operations | 84% |
| **TSK-0107**: Runtime decomposition planning | P2 ↑ | Produce scoping doc for AgentBackgroundService decomposition | — |
| **TSK-0083**: Checkpoint tests remainder | P2 ↑ | Complete ~50% remaining checkpoint tests | — |

## Wave C — Cleanup (P3)

| Task | Priority | Description | Notes |
|---|---|---|---|
| **TSK-0108**: Redundant state duplication cleanup | P3 | Merge `_currentGoal`/fact, dual replan-throttle, dual ConcurrentDict | Requires TSK-0107 scope |
| **TSK-0084**: ApplySmeltComplete in WorldStateProjector | P3 | Full impl when smelting functional area matures | No consumer yet |
| **TSK-0085**: HasFailed dead code removal | P3 | Affects smelt and craft goals equally | Low risk |
| **TSK-0096**: Mining double-counting dedup | P1 deferred | Waiting on real-world evidence of actual harm | — |

## Design Recommendations (Not Yet Tasked)

The `memorysmith_situational_awareness_design_doc_20260625T020914Z.md` proposes a `ScenePackBuilder` projection layer for situational awareness. This is a forward-looking design recommendation, not an audit claim. If pursued, it should be as a separate design spike:

- Phase 1: `ScenePackBuilder` class + tests for pack size and delta selection
- Phase 2: Policy-based MemorySmith writer for durable observations
- Phase 3: Planner integration consuming the pack without changing planner API
- Phase 4: Optional embeddings/graph enhancements (future capability)

Recommend deferring to Sprint 48+ unless TSK-0107's decomposition scope explicitly includes scene-building.

## Cross-Repo Items (MemorySmith Base Repo)

- ChatServices.cs 20+ bare catch blocks — tracking via `Data/Pages/MS-Requests/chat-services-bare-catches-2026-06-24.md`
- World KB deploy config / OpenTelemetry / other base-repo items tracked in their own sprint plan

## Validation

- Each task: `dotnet build` → 0 warnings, 0 errors
- Each task: `dotnet test` → 0 failures, no regressions
- TSK-0107 produces a reviewable plan document (no code changes)
