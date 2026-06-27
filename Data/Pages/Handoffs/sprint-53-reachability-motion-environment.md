# Sprint 53 — Reachability + Motion + Environment Exposure

**Date:** 2026-06-26 (planned)
**Branch:** `sprint-35-llm-first`
**Author:** SteveBot (MemorySmith.Agent)
**Status:** 🟡 Planned
**Design Refs:**
- `Data/Pages/Audit/memorysmith_situational_awareness_design_doc_20260625T020914Z.md` (Phases 2-3)
- `Data/Pages/Audit/mineflayer-adapter-research.md` (Council Review)
- `Data/Pages/Audit/msa_planning_mineflayer_audit_20260621_1437.md` (Deep Audit)

---

## Sprint Goal

Fix the biggest telemetry gaps in the Mineflayer adapter and complete phases 2-3 of situational awareness. The agent will understand WHY navigation fails, have timeout protection against hangs, know its physical state (orientation, motion, equipment, environment), and feed all of this into the planner and durable memory.

## Wave A — Pathfinder Telemetry + Timeout Protection (Critical)

### TSK-0158 — Wire Pathfinder Events
**Estimate:** 3.0 hrs | **Priority:** Critical

- Wire `path_update` (noPath/timeout/partial only), `goal_reached`, `path_stop` listeners
- Create `PathUpdateEvent`, `GoalReachedEvent`, `PathStoppedEvent` in WorldEvents.cs
- Handle in WebSocketBridge and AgentBackgroundService
- **Why critical:** DEF-PAPER-1 — the single biggest telemetry gap. goto() currently fire-and-wait.

### TSK-0159 — goto() Timeout Protection
**Estimate:** 2.0 hrs | **Priority:** Critical

- Add `gotoWithTimeout()` helper with Promise.race()
- 15s default timeout, configurable per action type
- Cancel active pathfinder on timeout, emit clean failure
- **Why critical:** DEF-PAPER-3 — known mineflayer-pathfinder hang (#222, #273). All 7 goto() calls unprotected.

### TSK-0160 — Move Throttle + Orientation
**Estimate:** 2.0 hrs | **Priority:** High

- Throttle move events to 250ms / 4 per second
- Emit immediately on > 1 block movement
- Add yaw/pitch to MoveEvent
- **Why:** DEF-PAPER-7 — 20 events/sec during pathfinding creates WebSocket flood.

### TSK-0161 — Motion, Equipment, Environment Telemetry
**Estimate:** 3.0 hrs | **Priority:** High

- Motion: onGround, isInWater, isInLava, velocity
- Equipment: heldItem, selectedSlot, armor, offhand
- Environment: timeOfDay, isRaining, isThundering, dimension
- Periodic `sendRichStatus()` every 2s

## Wave B — Durable Memory + Planner Integration (SA Phase 2-3)

### TSK-0152 — Policy-Based MemorySmith Writer
**Estimate:** 3.0 hrs | **Priority:** Medium

- Write on meaningful boundaries only (not every tick)
- Triggers: new landmark, goal start/complete/fail, recurring failure, user checkpoint
- Page types: snapshot/{timestamp}, landmark/{name}, goal/{goalId}, failure/{action}

### TSK-0153 — Write Durable Pages to World KB
**Estimate:** 3.0 hrs | **Priority:** Medium

- Wire writer into AgentBackgroundService lifecycle
- Landmark deduplication (within 10 blocks)
- `!remember` and `!landmark` chat commands

### TSK-0154 — Feed ScenePack into Planner
**Estimate:** 2.5 hrs | **Priority:** High

- Attach ScenePack to planning context
- BuildGoalDecomposer: entity presence near build site
- GatherGoalDecomposer: hostile mob proximity check

### TSK-0155 — Observation-Driven Replan Loop
**Estimate:** 3.0 hrs | **Priority:** High

- Compare expected vs observed outcomes after each action
- New ReplanReason: OutcomeMismatch, ThreatDetected
- Extend IReplanGovernor with outcome comparison

---

## What Sprint 53 Does NOT Include

| Deferred | Why |
|:---------|:----|
| Local world shape (block underfoot, light level) | → Sprint 54 with inventory and chat improvements |
| Inventory slot-level updateSlot | → Sprint 54 |
| Chat structured classification | → Sprint 54 |
| Action progress telemetry | → Sprint 54 |
| Adapter modularization | → Sprint 55 |
| Documentation drift fix | → Sprint 55 |
| HtnPlanner legacy branch removal | → Sprint 55 |

---

## Sprint 54 Preview — Inventory + Chat + Action Lifecycle

| Task | Summary |
|:-----|:--------|
| TSK-0162 | Local world shape: block underfoot, block in front, light level, hazards |
| TSK-0163 | Inventory updateSlot real-time ground truth |
| TSK-0164 | Chat structured message classification (messageKind) |
| TSK-0165 | Action progress telemetry (started/progress/failed with reason codes) |

## Sprint 55 Preview — Modularization + Cleanup

| Task | Summary |
|:-----|:--------|
| TSK-0166 | Modularize Mineflayer adapter (~1500 lines → 15+ focused modules) |
| TSK-0167 | Fix documentation version/sprint drift |
| TSK-0168 | Remove HtnPlanner legacy typed decomposition branches |

---

## Audit Corrections Noted

| Finding | Verdict |
|:--------|:--------|
| DEF-PAPER-2 (mineAborted/stopComplete no C# handler) | **INCORRECT** — handlers exist at AgentBackgroundService.cs:733-746 since Sprint 40 P0-C |
| DEF-PAPER-4 (collectblock) | Not needed — updateSlot is the correct hook for inventory telemetry, not collectblock |
| OQ-1 (IWorldObservationGateway) | Deferred — current approach is to extend existing event pipeline, not add new seam |

---

## Acceptance Criteria

1. ✅ Agent receives `goalReached` when pathfinding succeeds; `pathUpdate(noPath)` when it fails.
2. ✅ No goto() call runs longer than its timeout (15s default).
3. ✅ Move events throttled to 4/sec during pathfinding.
4. ✅ WorldState includes yaw/pitch, onGround, heldItem, timeOfDay.
5. ✅ ScenePack flows into planner; hostile mobs trigger replan.
6. ✅ Durable pages written to MemorySmith World KB on goal boundaries.
7. ✅ No regression: all existing tests pass.
