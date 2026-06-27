# Sprint 52 — Situational Awareness: Entity Awareness + Scene Pack

**Date:** 2026-06-26
**Branch:** `sprint-35-llm-first`
**Author:** SteveBot (MemorySmith.Agent)
**Status:** 🟡 Planned — Ready for Wave A
**Design Ref:** `Data/Pages/Audit/memorysmith_situational_awareness_design_doc_20260625T020914Z.md`
**Sprint 51 Status:** ✅ Wave A & B complete (742 tests, 0 failures)

---

## Sprint Goal

Close the mob/entity awareness gap and build the ScenePack projection layer — Phase 1 of the situational awareness design. The agent will know what entities are around it, and that knowledge will flow into the LLM prompt and planner via a compact, deterministic scene pack.

## Gaps This Sprint Closes

| Gap | Current Status | After Sprint 52 |
|:----|:---------------|:----------------|
| Entity/mob awareness | ❌ Zero — no events, no tracking, dead code | ✅ Entity events flow from adapter → WorldState → LLM prompt |
| `WorldVision.GetNearbyEntities()` | ❌ Dead code (reads facts nothing writes) | ✅ Populated by projector from entity events |
| LLM prompt context | ❌ No entity or scene info | ✅ Includes nearby entity summary |
| Scene context for planner | ❌ Raw WorldState only | ✅ ScenePack provides compact deltas + highlights |

## Wave A — Entity Pipeline (Tasks 0146–0149)

### TSK-0146 — Entity Observation in MineflayerAdapter
**Estimate:** 3.0 hrs | **Priority:** High

- Add periodic entity scan (every 2s, radius 32 blocks) using `bot.entities`
- Emit `entityObserved` / `entityDeparted` events with type, position, distance, health
- Exclude item drops, paintings, minecarts
- Tunable constants: `ENTITY_SCAN_RADIUS`, `ENTITY_SCAN_INTERVAL_MS`

### TSK-0147 — EntityObservedEvent + EntityDepartedEvent Records
**Estimate:** 1.0 hrs | **Priority:** High

- Add typed records to `WorldEvents.cs`
- `EntityObservedEvent`: EntityType, DisplayName, MobCategory, Position, Distance, Health
- `EntityDepartedEvent`: EntityType, LastPosition

### TSK-0148 — Project Entity Events into WorldState
**Estimate:** 2.0 hrs | **Priority:** High

- Add `NearbyEntities` collection to `WorldState` (bounded, max 50)
- Add `ObservedEntity` record
- Projector handlers for entityObserved (upsert) and entityDeparted (remove)
- Store as structured facts with `entity:` prefix
- Provide `NearbyEntitySummary` computed property

### TSK-0149 — Entity Summary in LLM Prompt
**Estimate:** 1.5 hrs | **Priority:** High

- Update `BuildSystemPrompt()` to include entity summary
- Format: `"Nearby: 2 cows, 1 zombie (5 blocks)"`
- Cap at 8 entities, sort by distance
- Add hostile-mob proximity rule hint

## Wave B — ScenePack + Pipeline Integration (Tasks 0150–0151)

### TSK-0150 — ScenePackBuilder
**Estimate:** 3.0 hrs | **Priority:** High

- Pure projection class: `WorldState` + recent events + goal → compact `ScenePack`
- Fields: headline, position, vitals, nearby entities, recent deltas, task highlights, memory references
- Bounded: max 8 entities, 10 deltas, 8 highlights
- Deterministic given same inputs

### TSK-0151 — Wire ScenePack into Chat Pipeline
**Estimate:** 2.0 hrs | **Priority:** High

- Register `ScenePackBuilder` in DI
- Build `ScenePack` in `HandleChatEventAsync` before LLM call
- Pass scene context to `BuildSystemPrompt` in structured format
- Backward compatible: no pack → prompt unchanged

---

## What Sprint 52 Does NOT Include

| Deferred | Why |
|:---------|:----|
| Durable MemorySmith writing (Phase 2) | → Sprint 53. Need ScenePack foundation first. |
| Planner integration with ScenePack (Phase 3) | → Sprint 53. Entity pipeline + ScenePack must be stable. |
| Observation-driven replan loop (Phase 3) | → Sprint 53. Depends on planner integration. |
| Embeddings / graph links (Phase 4) | → Sprint 54+. Blocked on MemorySmith backend. |

---

## Deferred from Sprint 51 (Still Pending)

| Task | Priority | Reason Deferred |
|:-----|:--------:|:----------------|
| TSK-0134 | High | DI startup failure logging + health checks |
| TSK-0133 | High | Fix parameter preservation on replan |
| TSK-0132 | High | Fix page search Score=0.0 under-ranking |
| TSK-0137 | Medium | Fix consecutive failure guard reset on partial progress |
| TSK-0144 | Critical | Enforce package vetting in CI |
| TSK-0145 | High | Run Verify-AboutDeps.ps1 in CI |

---

## Sprint 51 Remaining Tasks (CI + Deferred)

These were deferred from S51 waves. They can be picked up concurrently with S52 if capacity allows.

| Task | Priority | Title |
|:-----|:--------:|:------|
| TSK-0144 | Critical | Enforce package vetting policy in CI |
| TSK-0145 | High | Verify-AboutDeps.ps1 in CI |
| TSK-0134 | High | DI startup failure logging + health checks |
| TSK-0133 | High | Fix parameter preservation on replan |
| TSK-0132 | High | Fix page search Score=0.0 under-ranking |

---

## Sprint 53 Preview — Reachability + Motion + Durable Memory

Sprint 53 combines the remaining SA phases (2-3) with the critical Mineflayer adapter telemetry improvements identified in the audit.

### SA Phase 2-3 Continuation
| Task | Phase | Summary |
|:-----|:------|:--------|
| TSK-0152 | 2 | Policy-based MemorySmith writer service |
| TSK-0153 | 2 | Write snapshot/landmark/goal/failure pages to World KB |
| TSK-0154 | 3 | Feed ScenePack into planner context |
| TSK-0155 | 3 | Observation-driven replan comparison loop |

### Mineflayer Adapter — Pathfinder + Motion + Environment
| Task | Priority | Summary |
|:-----|:--------:|:--------|
| TSK-0158 | Critical | Wire pathfinder events (path_update, goal_reached, path_stop) |
| TSK-0159 | Critical | Promise.race() timeout on all 7 goto() calls |
| TSK-0160 | High | Throttle move events + add yaw/pitch orientation |
| TSK-0161 | High | Motion/equipment/environment telemetry (onGround, heldItem, timeOfDay, etc.) |

Full S53 plan: `Data/Pages/Handoffs/sprint-53-reachability-motion-environment.md`

## Sprint 54 Preview — Inventory + Chat + Action Lifecycle

| Task | Summary |
|:-----|:--------|
| TSK-0162 | Local world shape: block underfoot, block in front, light level, hazards |
| TSK-0163 | Inventory updateSlot real-time slot-level ground truth |
| TSK-0164 | Chat structured message classification (messageKind field) |
| TSK-0165 | Action progress telemetry (started/progress/failed with reason codes) |

## Sprint 55 Preview — Modularization + Cleanup

| Task | Summary |
|:-----|:--------|
| TSK-0166 | Modularize MineflayerAdapter (~1500 lines → 15+ focused modules) |
| TSK-0167 | Fix documentation version/sprint drift across README, roadmap, handoffs |
| TSK-0168 | Remove HtnPlanner legacy typed decomposition branches |

## Future Phase (Blocked)
| Task | Phase | Summary |
|:-----|:------|:--------|
| TSK-0156 | 4 | Embeddings for durable world pages (blocked: MemorySmith backend) |
| TSK-0157 | 4 | Graph links between landmarks/goals/pages (blocked: MemorySmith backend) |

## Audit Corrections

| Finding | Verdict |
|:--------|:--------|
| DEF-PAPER-2 (mineAborted/stopComplete no C# handler) | **INCORRECT** — handlers at AgentBackgroundService.cs:733-746 since Sprint 40 |
| DEF-PAPER-4 (collectblock needed) | Not needed — `updateSlot` is the correct hook per Mineflayer API |

---

## Acceptance Criteria

1. ✅ Agent emits `entityObserved` events for nearby cows, zombies, etc.
2. ✅ `WorldState.NearbyEntities` is populated and bounded.
3. ✅ LLM prompt includes compact entity summary.
4. ✅ `ScenePack` is built every chat cycle and passed to the prompt.
5. ✅ `WorldVision.GetNearbyEntities()` is no longer dead code.
6. ✅ No regression: all 742 existing tests pass.
7. ✅ New NUnit tests for entity projector, ScenePackBuilder, and prompt formatting.
