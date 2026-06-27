# MemorySmith.Agent — Situational Awareness Design Doc
**Branch / snapshot reviewed:** sprint-35-llm-first work as surfaced in current repo files and sprint handoff docs.  
**Timestamp:** 20260625T020914Z

## Purpose

This document revises the external situational-awareness proposal so it matches the current codebase instead of assuming future components already exist. It keeps the repo’s current architecture intact: deterministic-first planning, a dual MemorySmith gateway, and a canonical `WorldState` updated from Mineflayer events. The goal is to improve perception and retrieval without inventing a second parallel world model or a new hot-path graph subsystem. fileciteturn17file0turn24file0turn32file0turn33file0

## Validation summary of the external report

### Claims that are supported by the current code

The repository already has a two-gateway memory model: Agent KB and World KB, with `SearchMemory` / `CreatePage` routed to World KB and `GetPage` routed to Agent KB. The memory docs also show `WorldState.StructuredFacts` with provenance and a 1000-fact cap, plus a lexical-first resolver pipeline that can also search MemorySmith and in-memory world facts. fileciteturn32file0turn39file0turn40file0

The planner is already split into a `PlannerRouter`, typed goal decomposers, and an HTN fallback. Build goals already support explicit coordinates, fact-based origins, and auto-detection through `FindFlatArea`. The chat layer already has a deterministic regex interpreter plus an LLM-backed interpreter with fast-paths and truncated-JSON recovery. fileciteturn23file0turn26file0turn29file0turn45file0turn46file0

### Claims that are not yet supported by the current code

The external report’s `LocalSceneProvider`, `SpatialGraph`, embeddings-based retrieval, and CLIP/caption pipeline are not present in the current repo snapshot. The memory docs explicitly describe embeddings as a future phase and graph relations as future, not baseline behavior. The vision docs likewise show only deterministic world vision and an algorithmic spatial-analysis phase, with multimodal critique still marked Phase 4. fileciteturn32file0turn33file0turn37file0turn38file0

### Claims that need reframing

The report’s “keep the prompt minimal and push rich state into a graph + embeddings” direction is broadly right, but the implementation path should not assume a graph exists today. In this repo, the safe near-term version is: keep the prompt minimal, write durable observations as structured facts and MemorySmith pages, and only add vector/graph retrieval when the backing MemorySmith deployment actually exposes it. fileciteturn32file0turn39file0turn40file0

## Current codebase truth

### What currently owns state

`WorldState` is the canonical in-process snapshot. It already stores position, health, food, inventory, a legacy flat fact map, structured facts with source metadata, and an inventory-staleness flag. `WorldStateProjector` is the canonical reducer that applies typed world events, normalizes inventory keys, clears staleness on status, and stores raw facts for debugging. fileciteturn47file0turn20file0

That is the correct place to anchor situational awareness. A new design should extend `WorldState` and `WorldStateProjector`, not bypass them with an independent observation store. fileciteturn47file0turn20file0

### What currently owns planning

`PlannerRouter` is the production entry point. It prefers registered decomposers, then falls back to `HtnPlanner`. The current code already routes `BuildGoal`, `GatherGoal`, and `CraftItemGoal` through decomposers, while `HtnPlanner` remains a fallback that still handles typed build goals directly for direct callers and older tests. `ReplanAsync` still preserves selected action-context prefixes across replans. fileciteturn23file0turn24file0turn30file0turn27file0

### What currently owns memory

`RestMemoryGateway` is a thin REST adapter over MemorySmith’s search and page APIs. It is not a graph engine, and it does not create embeddings. The current memory architecture is BM25-first with future vector and graph phases called out explicitly in docs. fileciteturn39file0turn32file0

### What currently owns vision / scene perception

`WorldVision` is a deterministic adapter over `WorldState` facts. The current vision docs split the roadmap into deterministic world vision, algorithmic spatial analysis, and future multimodal critique. The current code only shows the deterministic layer. fileciteturn38file0turn37file0

## Revised design

## Design goal

Add situational awareness as a **derived view** over existing state and memory, not as a second world model.

The system should answer three questions cheaply:

1. What is true right now?
2. What changed since the last decision?
3. What memory is relevant to the current goal?

The answer should come from a compact scene pack built from `WorldState`, adapter events, and a small amount of routed memory, while the durable record lives in MemorySmith pages and structured facts. fileciteturn47file0turn20file0turn39file0turn32file0

## Proposed architecture

### 1) Canonical live state remains `WorldState`

Keep `WorldState` as the live, in-memory state the planner reads. Extend it only with fields that are cheap to maintain and broadly useful:

- current agent pose
- inventory
- health / food / mode
- staleness markers
- a bounded recent-observation list
- last action outcome summary
- references to the most recent durable memory pages

Do **not** put a spatial graph or embeddings store directly into `WorldState`. That would make the hot path heavier and harder to test. fileciteturn47file0turn33file0

### 2) Add a derived `ScenePack` view

Introduce a pure projection layer that can be built from `WorldState` plus the latest adapter events. The pack should be compact and deterministic:

- local scene summary
- top nearby entities or blocks
- recent deltas
- task-relevant facts
- memory references for the current goal

This is the right replacement for the external report’s `LocalSceneProvider` idea, but it should be modeled as a projection, not as a brand-new subsystem that bypasses the current state model. The implementation should live alongside `WorldStateProjector` or in the world adapter layer, because it is fundamentally an interpretation of live events. fileciteturn20file0turn47file0turn38file0

### 3) Persist only the right things to MemorySmith

Use the existing dual MemorySmith gateway:

- Agent KB for stable codebase and blueprint knowledge.
- World KB for transient world observations, exploration notes, goal-related facts, and durable location records. fileciteturn32file0turn33file0

Write to MemorySmith only on meaningful boundaries:
- a new landmark
- a goal-relevant success or failure
- a scan result worth reusing
- a build origin or other durable coordinate
- an explicit user-requested checkpoint

Do not write every tick. That would amplify noise, cost, and retrieval ambiguity. The current docs already frame MemorySmith as the durable layer, while `AgentJournal` is explicitly not the durable store. fileciteturn33file0turn32file0

### 4) Reuse the existing knowledge resolver before inventing new retrieval machinery

The current `LocalKnowledgeResolver` already has a sensible precedence order: item registry, MemorySmith search, then in-memory world facts, with confidence scoring and ambiguity detection. That is enough to power the first version of situational awareness retrieval. Add retrieval policies on top of this resolver before introducing semantic graphs. fileciteturn40file0turn32file0

The retrieval contract should be:

- exact state first
- recent world facts second
- memory search third
- longer-term memory references only when relevant to the current goal

This keeps the planner’s context narrow and prevents token flooding without assuming embeddings exist today. fileciteturn40file0turn32file0

### 5) Make observation-driven replanning a first-class loop

The current code already has a replan governor, action lifecycle tracking, and world-state reconciliation. Use those hooks to compare expected outcomes to observed outcomes instead of relying on adapter success alone. That is the missing piece behind the external report’s “better situational awareness” goal. fileciteturn23file0turn24file0turn42file0

The loop should be:

`Plan → Dispatch → Observe → Compare → Replan if needed`

with comparison based on:
- inventory delta
- position delta
- health delta
- event-specific outcome facts
- any relevant MemorySmith page updates

This can be introduced without replacing the current planner stack. fileciteturn20file0turn47file0turn42file0

## Concrete implementation plan

### Phase 1 — Projection layer and scene pack

Add a pure `ScenePackBuilder` or similarly named projection class that consumes:
- `WorldState`
- the latest few `WorldEvent`s
- current goal metadata
- optional nearby entity summaries from `WorldVision`

Output a small immutable structure with:
- headline summary
- deltas since last tick
- task-relevant highlights
- memory references

This gives the prompt a stable, compact structure without changing the planner boundary. fileciteturn38file0turn47file0turn20file0

### Phase 2 — Durable world memory writing

Add a small writer service that turns scene packs into MemorySmith pages for only the durable or goal-relevant events. Reuse the existing `IMemoryGateway` API and World KB routing. That is a better fit than the external report’s proposed `IWorldMemoryWriter` as a standalone new persistence layer. The writer should be policy-driven, not always-on. fileciteturn39file0turn32file0

Suggested page types:
- `snapshot/{timestamp}` for significant scene snapshots
- `landmark/{name}` for stable locations
- `goal/{goalId}` for persistent goal context
- `failure/{action}` for recurring failure patterns

### Phase 3 — Planner integration

Keep `PlannerRouter` and the decomposer registry. Feed the planner a compact scene pack and a small set of memory hits, not raw world logs. For build goals, continue using the current explicit-origin / fact-origin / auto-detect flow already implemented in `BuildGoal` and `BuildGoalDecomposer`. fileciteturn23file0turn26file0turn29file0

Do not make the planner depend directly on MemorySmith graph traversal. The current code’s router and HTN fallback are already mature enough to accept a better context pack. fileciteturn23file0turn30file0

### Phase 4 — Optional semantic enhancement

Only after the backend exposes it cleanly, add:
- embeddings for durable world pages
- graph links between landmarks, goals, and pages
- semantic retrieval of related areas or past builds

This should remain a capability layer on top of the current World KB, not a prerequisite for the initial situational-awareness system. The repo docs already treat embeddings and graph relations as future phases. fileciteturn32file0turn33file0turn37file0

## Design decisions resolved

### Do we need a new global world model?

No. The current `WorldState` plus `WorldStateProjector` already fill that role. The new system should be a derived view over them. fileciteturn47file0turn20file0

### Should embeddings be part of the first implementation?

No. They are not in the current codebase and are explicitly treated as a future phase in the docs. A first version should rely on structured facts, page text, and existing search. fileciteturn32file0turn33file0turn37file0

### Should a spatial graph be added now?

No. There is no current spatial graph implementation. Start with page links and structured facts first; add graph semantics only when the memory backend supports them in a way the repo can consume cleanly. fileciteturn32file0turn33file0

### Should the chat layer move immediately to a brand-new intent schema?

Not as the first step. The current code still uses `ChatInterpretation` and goal creation in the chat interpreters. Preserve that boundary for now, and improve situational awareness through the planner and observation layers first. An `IntentDraft`-style split can be a later refactor if the team chooses to fully adopt the LLM-first handoff. fileciteturn45file0turn46file0turn43file0

### Should the separate World KB be mandatory?

No. The current code already supports graceful fallback when `WorldKbUrl` is null. The design should respect that. fileciteturn32file0turn33file0turn27file0

## Risks and mitigations

### Risk: overfitting the prompt to raw world data
Mitigation: keep the prompt to one scene pack, a few deltas, and the relevant memory hits.

### Risk: writing too much to MemorySmith
Mitigation: only persist durable or goal-relevant observations.

### Risk: adding a second planner-like abstraction
Mitigation: keep the planner stack unchanged; add a projection layer and retrieval policy only.

### Risk: confusing implementation status with roadmap status
Mitigation: gate embeddings, graphs, and multimodal vision behind explicit future-phase milestones, because the current code does not provide them yet. fileciteturn32file0turn37file0

## Open questions, now answered

1. **“Where does situational awareness live?”**  
   In a derived scene pack over `WorldState`, with durable writes to MemorySmith for important observations. fileciteturn47file0turn20file0turn39file0

2. **“Do we need embeddings right now?”**  
   No. They are future capability, not current baseline. fileciteturn32file0turn33file0

3. **“Do we need a spatial graph right now?”**  
   No. Represent relationships with page links and structured facts first. fileciteturn32file0turn33file0

4. **“Should the prompt become a giant scene dump?”**  
   No. Keep it compact and task-relevant; use retrieval for anything else. fileciteturn45file0turn46file0turn40file0

## Recommended next implementation slice

The smallest high-value PR is:

1. `ScenePackBuilder`
2. tests for pack size and delta selection
3. a policy-based MemorySmith writer for durable observations
4. planner integration that consumes the pack without changing the planner API

That gets the repo closer to real situational awareness while preserving the codebase’s existing strengths: deterministic planning, a canonical world state, and a two-gateway memory architecture. fileciteturn33file0turn47file0turn39file0
---

## Tasks Created (2026-06-26)

Concrete task records were created from this design doc. See `Data/Pages/Handoffs/sprint-52-situational-awareness-planning.md` for the sprint plan.

### Sprint 52 — Entity Awareness + Scene Pack (Phase 1)

| Task | Title | Priority |
|:-----|:------|:--------:|
| TSK-0146 | Add entity observation to MineflayerAdapter | High |
| TSK-0147 | Add EntityObservedEvent + EntityDepartedEvent to WorldEvents.cs | High |
| TSK-0148 | Project entity events in WorldStateProjector into WorldState | High |
| TSK-0149 | Include entity summary in LLM system prompt | High |
| TSK-0150 | Implement ScenePackBuilder projection class | High |
| TSK-0151 | Wire ScenePackBuilder into chat pipeline | High |

### Sprint 53 — Durable Memory + Planner Integration (Phases 2-3)

| Task | Title | Priority |
|:-----|:------|:--------:|
| TSK-0152 | Implement policy-based MemorySmith writer service | Medium |
| TSK-0153 | Write snapshot/landmark/goal/failure pages to World KB | Medium |
| TSK-0154 | Feed ScenePack into planner context | High |
| TSK-0155 | Add observation-driven replan comparison loop | High |

### Sprint 53 — Mineflayer Adapter: Reachability + Motion + Environment

| Task | Title | Priority | Audit Source |
|:-----|:------|:--------:|:-------------|
| TSK-0158 | Wire pathfinder events (path_update, goal_reached, path_stop) | Critical | DEF-PAPER-1, DEF-PAPER-3 |
| TSK-0159 | Promise.race() timeout on all goto() calls | Critical | DEF-PAPER-3 |
| TSK-0160 | Throttle move events + add yaw/pitch orientation | High | DEF-PAPER-7 |
| TSK-0161 | Motion/equipment/environment telemetry | High | Audit Finding 2 |

### Sprint 54 — Inventory + Chat + Action Lifecycle

| Task | Title | Priority | Audit Source |
|:-----|:------|:--------:|:-------------|
| TSK-0162 | Local world shape: block underfoot, light level, hazards | High | Audit Finding 2 |
| TSK-0163 | Inventory updateSlot real-time slot-level ground truth | High | DEF-PAPER-4 |
| TSK-0164 | Chat structured message classification (messageKind) | Medium | Audit Finding 3 |
| TSK-0165 | Action progress telemetry (started/progress/failed) | Medium | Audit Finding 6 |

### Sprint 55 — Modularization + Cleanup

| Task | Title | Priority | Audit Source |
|:-----|:------|:--------:|:-------------|
| TSK-0166 | Modularize MineflayerAdapter (15+ modules) | Medium | Audit Finding 5 |
| TSK-0167 | Fix documentation version/sprint drift | Low | Audit Finding 1 |
| TSK-0168 | Remove HtnPlanner legacy typed branches | Low | Audit Finding 4 |

### Sprint 54+ — Semantic Enhancement (Phase 4, Future)

| Task | Title | Priority | Blocker |
|:-----|:------|:--------:|:--------|
| TSK-0156 | Add embeddings for durable world pages | Low | MemorySmith backend |
| TSK-0157 | Add graph links between landmarks/goals/pages | Low | MemorySmith backend |