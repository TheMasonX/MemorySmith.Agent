# Sprint 40 Plan — Mineflayer Adapter Council Review Response

**Date:** 2026-06-22  
**Source:** `Data/Pages/Audit/mineflayer-adapter-research.md` (Council Review)  
**Previous handoff:** `Data/Pages/Tasks/handoff-sprint40-p0-fixes.md`

---

## Executive Summary

The council review of the Mineflayer Adapter Research Paper identified **7 defects (DEF-PAPER-1 through DEF-PAPER-7)** and **4 open questions (OQ-1 through OQ-4)** across the current adapter architecture. These findings are layered on top of the 5 remaining issues (A–E) from the Sprint 40 P0 handoff.

This plan consolidates both sources into a single, prioritized task set. The highest-value items are: (1) fixing the production `mineAborted`/`stopComplete` gap, (2) adding `goto()` timeout safety, (3) resolving the block-position off-by-one, and (4) verifying kick→reconnect end-to-end.

---

## Priority Order

### P0 — Must Fix (Blocking Runtime)

| Priority | Source | Description | Task |
|----------|--------|-------------|------|
| P0 | Handoff Issue C | **Block position off-by-one** — bot digs at Y=62-63 instead of Y=64. Determine if `findBlock` Euclidean distance, falling through mined blocks, or `GoalNear` tolerance is the cause. | TSK-0065 |
| P0 | Handoff Issue B | **Kick→reconnect validation** — `KickedEvent` handler exists but untested. Verify reconnection loop, Node.js process restart, and add exponential backoff. | TSK-0066 |
| P0 | DEF-PAPER-2 | **Wire `mineAborted`/`stopComplete` to C#** — These events are emitted by `index.js` but `WebSocketBridge.ParseEvent()` returns `null` for both. No `WorldEvent` subtype exists. Production defect. | TSK-0061 |

### P1 — High Value (Active Development)

| Priority | Source | Description | Task |
|----------|--------|-------------|------|
| P1 | DEF-PAPER-3 | **`goto()` timeout backstop** — `await bot.pathfinder.goto()` can hang silently. Add `Promise.race()` timeout + `path_update` listener for `noPath`/`timeout`/`partial` status. | TSK-0062 |
| P1 | DEF-PAPER-1 | **Fix `path_reset` to `path_update`** — Research paper references nonexistent `path_reset` event. Use `path_update` with `status: 'noPath'` instead. | TSK-0070 |
| P1 | Handoff Issue E | **Stale-inventory guard at goal-creation time** — `GoalFactory` rejects `GatherItem` when stale inventory shows sufficient items. Guard must apply before goal creation, not just before planning. | TSK-0067 |

### P2 — Important (Quality of Life)

| Priority | Source | Description | Task |
|----------|--------|-------------|------|
| P2 | DEF-PAPER-4 | **Wire `bot.inventory.on('updateSlot')`** — Replace mining-inference inventory tracking with real-time slot-level Mineflayer update events for authoritative ground truth. | TSK-0063 |
| P2 | DEF-PAPER-7 | **Throttle/debounce `move` events** — `bot.on('move')` fires every physics tick (~20/sec). Add rate limiting in the adapter. | TSK-0064 |

### P3 — Planning/Architecture (Future Sprint)

| Priority | Source | Description | Task |
|----------|--------|-------------|------|
| P3 | DEF-PAPER-5 | **Define `IObservationSummarizer` integration point** — Phase 3 observation summaries need a home in the existing C# pipeline. Extend `IWorldModel` or define a new interface. | TSK-0068 |
| P3 | DEF-PAPER-6 | **Add `mineflayer-collectblock` + `mineflayer-tool` deps** — Phase 5 plugin-based workflows. Requires `package.json` update + mine case semantic coordination. | TSK-0069 |

---

## Council Open Questions — Design Decisions

These do not have explicit tasks but should be resolved before the relevant phase begins:

| OQ | Question | Recommendation |
|----|----------|---------------|
| OQ-1 | Should new adapter events route through `IWorldObservationGateway`, `IWorldAdapter.ReceiveEventsAsync`, or both? | Resolve before Phase 1 implementation |
| OQ-2 | Should `path_update` forward every recalculation or only status transitions? | **Recommendation:** forward only non-`success` statuses plus `goal_reached` |
| OQ-3 | LLM replan path vs `IReplanGovernor` authority — which wins? | Governor should own deterministic stall detection; LLM path is advisory until Sprint 41 |
| OQ-4 | Is Phase 4 proposing a new LLM call path or describing existing behavior with better observation input? | Clarify in architecture docs. Current view: it describes existing `IPlanner` + evaluator with enriched input. |

---

## Dependency Map

```
TSK-0065 (Block position) ──depends-on── TSK-0061 (mineAborted/stopComplete wiring)
                                        ──depends-on── PR #2 (coordinate logging) [in-progress]

TSK-0066 (Kick→reconnect)  ──depends-on── PR #2 (KickedEvent handler) [in-progress]
                                        ──depends-on── TSK-0061 (event wiring patterns)

TSK-0062 (goto timeout)    ──depends-on── TSK-0070 (correct path_update)
                                        ──related-to── TSK-0037 (action progress telemetry)

TSK-0063 (updateSlot)     ──depends-on── TSK-0066 (solid reconnect first — inventory must survive kicks)
```

---

## Existing Related Tasks

These tasks from the backlog overlap with council review findings but are broader in scope:

| Task | Relation |
|------|----------|
| TSK-0036 (Deepen adapter observation payload) | Superset of DEF-PAPER-4, DEF-PAPER-7. New tasks TSK-0063, TSK-0064 can be sub-tasks or independent deliveries. |
| TSK-0037 (Action progress telemetry) | Complements TSK-0062 (goto timeout). Telemetry tracks progress; timeout is a safety backstop. |
| TSK-0039 (Refactor adapter into modules) | Independent from council findings. After P0/P1 items are fixed, the module split is cleaner with working event wiring. |

---

## Success Criteria

- All P0 items: verified via runtime test (connect, gather dirt, verify Y-level, verify kick recovery)
- All P1 items: `dotnet build` + `dotnet test` pass, verified in runtime log
- P2 items: adapter-side changes tested with Node.js test harness
- P3 items: interface defined, reviewed, and documented — implementation deferred
