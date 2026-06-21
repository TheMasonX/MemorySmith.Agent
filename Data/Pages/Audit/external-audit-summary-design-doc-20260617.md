# External Audit: MemorySmith.Agent — Summary Design Doc
**Source:** External architecture review, 2026-06-17  
**Persisted by:** Implementation agent (Sprint 14 intake)  
**Original filename:** MemorySmithAgent_Summary_Design_Doc.md

---

## 1. Purpose

MemorySmith.Agent should evolve from a Minecraft-specific agent into a persistent embodied intelligence platform. Minecraft is the first world adapter, not the product. The core product is the cognition stack: memory, world modeling, planning, reflection, and long-horizon project execution.

The most important design correction is to treat **Memories** as the lightweight, graphable, filterable, editable primitives, while **Pages** remain the heavier synthesis layer for durable knowledge, blueprints, reports, and detailed documentation.

## 2. Current state at a glance

The current codebase is already past the skeleton phase and has real forward momentum:

- a modular solution with agent core, planning, world adapter, construction, vision, and Blazor UI boundaries
- deterministic-first planning philosophy
- HTN/GOAP direction for task decomposition
- background service orchestration
- signal-driven world event processing
- a growing observability surface
- a visible shift toward world-modeling and journaling

The remaining gap is not "more Minecraft features." The gap is the missing cognition substrate:
- explicit memories vs pages separation
- observation and belief layers
- episodic memory
- structured world model
- project hierarchy
- reflection and consolidation
- adapter-agnostic planning

## 3. Design principles

### 3.1 Deterministic first
The agent should rely on deterministic logic wherever possible. LLM usage should be reserved for:
- novel goals
- ambiguous planning
- recovery when deterministic methods fail
- synthesis and interpretation, not routine execution

### 3.2 Memory-first, page-second
Memories are the operational substrate:
- atomic
- searchable
- taggable
- linkable
- low-friction to write
- easy to graph and filter

Pages are synthesized artifacts:
- long-form
- curated
- slower-changing
- useful for reports, blueprints, policies, and broad strategy

### 3.3 World adapters are peripherals
The world adapter is only the sensor/actuator boundary. The planner must not depend directly on Minecraft semantics.

### 3.4 Belief-based reasoning
Planning should operate on interpreted beliefs and the world model, not raw adapter events.

### 3.5 Reflection closes the loop
Every substantial action should feed back into memory, beliefs, and future planning.

## 4. Core conceptual model

```
Observation -> Memory -> Belief -> Episode -> Page
```

### Observation
A raw, typed fact from the world or a tool.

### Memory
A small graph node with tags, links, confidence, provenance, and time.

### Belief
A stabilized interpretation of one or more observations and memories.

### Episode
A compressed record of an experience or event sequence.

### Page
A synthesized document produced from clusters of memories, beliefs, and episodes.

## 5. Recommended architecture

```
Embodiment Adapter(s)
  -> Observation pipeline
  -> Memory graph
  -> Belief / episodic consolidation
  -> World model
  -> Planning
  -> Execution
  -> Reflection
  -> Page synthesis
```

### 5.1 Embodiment adapters
Examples: Minecraft, desktop automation, web, future robotics.  
Adapters should only expose: observed events, action execution, capability metadata.

### 5.2 Observation pipeline
Normalize raw adapter events into structured observations. Observations should be immutable.

### 5.3 Memory graph
Central knowledge substrate for operational reasoning.

Suggested node types: atomic memory, episodic memory, belief memory, project memory, reference memory.  
Suggested edge types: temporal, causal, spatial, semantic, dependency, contradiction, refinement.

### 5.4 World model
The world model is a cached, queryable interpretation of current state: position, inventory, resources, known threats, known structures, active projects, uncertainty.

### 5.5 Planning
A multi-scale planner:
- strategic: projects
- operational: goals
- tactical: tasks
- reactive: interrupts and hazards

HTN and GOAP are both valid within the tactical layer.

### 5.6 Reflection
After actions: evaluate result, create/update memories, revise beliefs, record episode, update project status, synthesize pages when warranted.

## 6. Memory vs Page policy

### Write memories for
quick notes, breadcrumbs, discoveries, preferences, intermediate results, candidate hypotheses, short status updates, lightweight graph edges.

### Write pages for
deep research, detailed plans, blueprints, reports, canonical project docs, long-form synthesis.

### Rule of thumb
If the information will be useful in graph traversal, filtering, or quick retrieval, it should be a memory first. If it is meant to be read as a curated artifact, it should be a page.

## 7. What should be broad rather than Minecraft-specific

The design should explicitly support:
- generic project execution
- generic observation and belief revision
- reusable planning primitives
- non-Minecraft world adapters
- memory-backed knowledge graphs
- multimodal critique
- long-horizon projects

Minecraft remains a proving ground, not the identity of the system.

## 8. Non-goals for the near term
- distributed consensus
- multi-host orchestration
- autonomous self-modification
- over-engineered message buses
- premature vector database dependence
- replacing deterministic planning with pure LLM planning

## 9. Phased roadmap

### Phase A — Cognition substrate
memory graph abstractions, observation normalization, belief model, episode model, world model

### Phase B — Planning rewrite
planner input should be world model + beliefs, strategic/operational/tactical separation, explicit reflection after execution

### Phase C — Knowledge synthesis
memories synthesize into pages, page generation based on clusters and project milestones, blueprint and report generation

### Phase D — Adapter expansion
Minecraft remains primary, future adapters share the same cognition stack

## 10. Success criteria

The architecture is successful when the system can:
- write and retrieve memories efficiently
- build a stable world model from observations
- plan against beliefs rather than raw events
- recover from failure using history
- produce useful pages from memory clusters
- swap adapters without changing core cognition

## 11. Bottom line

The project should be framed as a persistent cognition platform with Minecraft as the first embodiment. The biggest missed opportunity is not tool coverage; it is the absence of a formal memory graph, belief layer, episodic memory, and world model.
