# RFC: MemorySmith.Agent Cognition Substrate and Memory-First Architecture
**Source:** External RFC, 2026-06-17  
**Status:** Draft — persisted as audit record  
**Persisted by:** Implementation agent (Sprint 14 intake)  
**Original filename:** MemorySmithAgent_RFC_Engineering_Spec.md

---

## 1. Abstract

This RFC proposes a structural rearchitecture of MemorySmith.Agent so that the system reasons over a memory graph, belief state, and world model instead of depending primarily on pages, raw adapter state, or Minecraft-specific concepts. The goal is to preserve the current deterministic-first and modular foundation while adding the missing long-term cognition substrate required for general embodied agents.

## 2. Motivation

The current codebase is already a good skeleton for a Minecraft agent, but the long-term product vision is broader:
- persistent memory
- lightweight searchable notes
- graphable breadcrumbs
- belief formation
- episodic learning
- project execution
- generic world adapters

The current design still over-relies on:
- pages as the primary knowledge store
- Minecraft as the implicit center of the architecture
- planner input that is too close to raw adapter state
- missing reflection and consolidation layers

Without correction, the system will scale into a polished Minecraft bot rather than a general agent platform.

## 3. Goals

1. Make memories first-class and pages secondary.
2. Add observation, belief, episodic memory, and world model layers.
3. Keep deterministic planning as the default.
4. Keep adapters swappable.
5. Preserve the current modular solution shape while improving the cognitive architecture.
6. Support long-horizon projects and synthesis from memory clusters.
7. Enable incremental migration without a rewrite.

## 4. Non-goals

- Full multi-agent coordination in the initial migration.
- A distributed event bus or broker.
- Replacing all existing page usage immediately.
- Pure LLM planning.
- A new database requirement unless the storage abstraction demands it later.

## 5. Terminology

### Memory
A small, editable, searchable, taggable graph node. Examples include discoveries, breadcrumbs, preferences, and intermediate facts.

### Page
A long-form synthesized artifact built from many memories, beliefs, or episodes.

### Observation
An immutable raw event from the world, adapter, or tool system.

### Belief
A stable interpretation of one or more observations and memories.

### Episode
A compressed experience record with outcome and lesson content.

### World Model
A structured, queryable view of the current world state and uncertainty.

### Project
A long-running initiative composed of goals and tasks.

### Goal
A bounded outcome the agent is trying to reach.

### Task
A tactical step toward a goal.

### Action
A primitive execution step.

## 6. Proposed architecture

```
Adapter Event
  -> Observation
  -> Memory Node
  -> Belief Update
  -> World Model Reconciliation
  -> Planner Input
  -> Task Decomposition
  -> Action Execution
  -> Reflection
  -> Page Synthesis
```

## 7. Data model

### 7.1 Memory node
Fields: id, title, summary, tags, links, confidence, source, timestamp, kind, status, optional payload

### 7.2 Memory edge
Fields: source id, target id, relation kind, weight, provenance, timestamp

### 7.3 Observation
Fields: source, timestamp, location, payload, confidence, adapter metadata

### 7.4 Belief
Fields: statement, confidence, support links, contradiction links, last updated, derived from memories

### 7.5 Episode
Fields: start/end time, participants, outcome, lessons, associated memories, associated project or goal

### 7.6 World model
Fields: current position, inventory, known resources, known hazards, known structures, active project state, uncertainty, recent observations

## 8. Required invariants

1. Pages are not the primary write target for operational notes.
2. Planner inputs must include beliefs or a world model, not only adapter state.
3. Observations are immutable.
4. Memories are cheap to write.
5. Pages are synthesized, not assumed.
6. Adapters should not contain planning logic.
7. Reflection must happen after action sequences or failures.
8. Current world state should be reconstructable from recent observations plus model state.

## 9. API / service surface

The implementation should expose abstractions, not concrete storage assumptions.

### Memory gateway
Responsibilities: write memories, search memories, update tags, retrieve linked memory neighborhoods, optionally back pages with memory clusters.

### World model
Responsibilities: ingest observations, maintain current belief state, expose uncertainty, expose compact summaries.

### Planner
Responsibilities: accept goal + world model + belief context, select strategy, decompose into tasks, request LLM only when needed.

### Reflection service
Responsibilities: evaluate outcomes, store lessons, update memory graph, trigger page synthesis when warranted.

## 10. Migration strategy

### Stage 1: Introduce the abstractions
Add memory, observation, belief, episode, and world model interfaces and simple in-memory implementations.

### Stage 2: Wire the pipeline
Normalize adapter events into observations, then update memory and world model before planning.

### Stage 3: Shift planning inputs
Move planner entry points from raw adapter state to the world model and beliefs.

### Stage 4: Demote pages
Make pages a synthesis artifact, not the default persistent note type.

### Stage 5: Expand adapter support
Keep the core cognition stack stable while adding future adapters.

## 11. Risks

### Risk: memory explosion
Mitigation: tagging, ranking, consolidation, and periodic synthesis.

### Risk: stale beliefs
Mitigation: confidence scores, contradiction links, and reflection.

### Risk: planner complexity
Mitigation: keep deterministic planners small and add LLM only for ambiguous cases.

### Risk: page/memory confusion persists
Mitigation: enforce API naming and write-path rules.

### Risk: Minecraft lock-in
Mitigation: make the world adapter an implementation detail, not a domain anchor.

## 12. Acceptance criteria

The RFC is satisfied when:
- memories can be created, searched, linked, and edited as lightweight nodes
- pages are clearly synthesized from memories
- observations update a world model
- beliefs drive planner input
- episodes record experience and lessons
- the system still works with the Minecraft adapter and can later support others

## 13. Implementation guidance for the agent

1. Start with abstractions and adapter boundaries.
2. Add the memory graph before expanding planner sophistication.
3. Add observation-to-belief-to-world-model flow before changing most goal logic.
4. Keep the existing deterministic-first planning design.
5. Preserve current operational improvements such as journaling, status endpoints, and tool validation.
6. Make every new layer observable and testable.

## 14. Conclusion

The project's long-term success depends on making MemorySmith.Agent a memory-driven cognition system rather than a Minecraft automation system. The RFC's central change is simple: move knowledge into memories, move reasoning onto beliefs and world models, and let pages become curated synthesis.
