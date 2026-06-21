# External Audit: MemorySmith.Agent — Strategic Architecture Review & Implementation Plan
**Source:** External architecture review, 2026-06-17  
**Persisted by:** Implementation agent (Sprint 14 intake)  
**Original filename:** MemorySmithAgent review_260617_103508.txt

---

## Executive Summary

After reviewing the documented architecture, roadmap, stated goals, and the clarification regarding Memories vs Pages, the recommendation is to deliberately pivot from:

> "Minecraft agent with memory"

toward:

> "Persistent embodied intelligence platform with Minecraft as its first embodiment."

This is not a rewrite. Most of the existing direction remains valid. The adjustment is primarily architectural and conceptual:
- elevate memories to a first-class graph
- introduce an explicit world model
- separate projects from goals
- introduce observation/belief systems
- treat Minecraft as an adapter
- make planning operate on knowledge rather than raw world state

---

## Revised Core Philosophy

The system should revolve around five pillars:

```
Memory
Knowledge
World Modeling
Planning
Embodiment
```

---

## The MemorySmith Knowledge Pyramid

Current implied model:
```
Pages
 └─ Everything
```

Proposed model:
```
Pages          → Deep knowledge
Memories       → Lightweight graph nodes
Observations   → Raw perceived facts
Beliefs        → Interpreted world state
Episodes       → Historical experiences
Projects       → Long-term initiatives
```

---

## Memory Should Become The Primary Knowledge Primitive

A memory is: Small, Atomic, Linkable, Taggable, Searchable, Editable, Graphable.

Examples:
- Coal vein discovered east of village
- Village blacksmith sells iron tools
- House project requires 480 stone
- User prefers spruce wood
- Dangerous cave near spawn

Each memory should carry: Id, Title, Summary, Tags[], References[], Source, Confidence, Timestamp.

---

## Pages Become Long-Form Knowledge

Pages are not the primary navigation structure. Pages become: Knowledge synthesis, Documentation, Blueprints, Research, Plans, Reports.

Examples: Settlement Survey, Village Expansion Plan, Castle Blueprint, Resource Gathering Guide.

**Pages are generated from memory clusters. Not vice versa.**

---

## Proposed Knowledge Hierarchy

```
Observation
    ↓
Memory
    ↓
Belief
    ↓
Episode
    ↓
Page
```

---

## Observation Layer

Missing entirely today. Needs to exist before planning.

**Observation** — Raw fact. Examples: Saw 12 coal ore blocks; Zombie approaching; Chest contains 32 logs.

Model: `{ Source, Time, Location, Content, Confidence }`. Observations are immutable.

---

## Belief Layer

Observations become beliefs.

Observations: `Coal found at X` + `Coal found nearby at Y` → Belief: `Coal-rich area exists east of village`.

Beliefs represent current understanding, not raw facts.

Model: `{ Statement, Confidence, SupportingEvidence[], ContradictingEvidence[] }`.

---

## Episodic Memory Layer

Currently absent. This is critical.

Episodes record experiences: Built starter house, Attempted mine expansion, Defended village raid, Explored northern forest.

Model: `{ StartTime, EndTime, Participants, Outcome, LessonsLearned, Memories[] }`.

---

## World Model

This is the largest architectural addition.

Current: `Planner → Minecraft`  
Proposed: `Planner → World Model → Minecraft`

World model owns: Known resources, Known structures, Known entities, Known threats, Known projects, Known settlements, Known locations.

Model: `{ Resources, Structures, Regions, Entities, Threats, Projects }`.

The planner should never ask "Where is coal?" directly from Minecraft. Instead: ask the WorldModel.

---

## Project System

This is currently conflated with goals. Needs separation.

```
Vision    → Years      (Create autonomous settlement)
Projects  → Weeks/months (Village Expansion, Castle Construction)
Goals     → Days       (Build storage room, Gather stone)
Tasks     → Hours      (Mine 128 cobblestone)
Actions   → Seconds    (Break block, Move, Place block)
```

Hierarchy: `Vision → Project → Goal → Task → Action`

---

## Planning Architecture Redesign

Current: `Goal → HTN → Actions`

Proposed:
```
Vision Layer
Project Layer
Goal Layer
Planning Layer (HTN / GOAP / Reactive)
Execution Layer
```

### Multi-Scale Planning

- Strategic Planner (weeks/months) → produces Projects
- Operational Planner (days) → produces Goals
- Tactical Planner (minutes) → produces Tasks
- Reactive Layer (seconds) → handles Combat, Hazards, Interruptions

---

## Reflection System

Currently missing. Should become mandatory.

After significant actions: Observe → Evaluate → Store lessons → Update beliefs.

Example:
```
Mining strategy failed
Reason: Lacked torches
Lesson: Always gather coal first
```
Store as memory.

---

## Knowledge Synthesis System

One of the most important future capabilities.

Agent accumulates 1000+ memories → periodically creates Pages:
- Regional Resource Survey
- Village Status Report
- Construction Progress Report
- Exploration Journal

Process: `Memories → Clusters → Summaries → Pages`

This creates durable intelligence.

---

## Blueprint Architecture

Blueprints should become first-class knowledge objects. Not pages. Not memories. Separate type.

Blueprint: `{ Structure, Requirements, Stages, Dependencies, Metadata }`.  
Blueprints link to: Projects, Pages, Memories.

---

## Agent Identity System

Currently underrepresented. Identity should contain: Who am I? What am I doing? What matters? What projects are active?

Identity is not personality.
- Personality: Friendly, Curious
- Identity: Builder, Explorer, Settlement Manager

---

## Multi-Agent Future

Do not implement immediately. But prepare architecture.

Shared layer: Pages, Memories, Projects, Blueprints  
Private layer: Beliefs, Working memory, Plans

This naturally supports: Builder Agent, Explorer Agent, Farmer Agent, Architect Agent.

---

## Embodiment Abstraction

Minecraft should become `MinecraftAdapter`, not a core subsystem.

Future: `MinecraftAdapter`, `DesktopAdapter`, `DiscordAdapter`, `WebAdapter`, `RobotAdapter` — all consuming the same planning system.

---

## Recommended New Architecture

```
MemorySmith.Agent
├─ Embodiments (Minecraft, Desktop, Future)
├─ WorldModel (Observations, Beliefs, Episodes, Regions)
├─ Planning (Strategic, Operational, Tactical, Reactive)
├─ Knowledge (Memories, Pages, Blueprints, Synthesis)
├─ Identity
└─ Execution
```

---

## Recommended Roadmap Revision

- Phase 1 — Foundation: Memory integration, Tool system, Minecraft embodiment, Goal execution
- Phase 2 — World Understanding: Observation system, Belief system, Episodic memory, World model
- Phase 3 — Planning: HTN, GOAP, Multi-scale planning, Reflection
- Phase 4 — Knowledge Formation: Memory graph, Knowledge synthesis, Auto-generated pages, Blueprint system
- Phase 5 — Autonomous Projects: Vision layer, Project management, Long-running initiatives, Resource forecasting
- Phase 6 — Multi-Agent: Shared knowledge, Agent specialization, Coordination
- Phase 7 — Additional Embodiments: Desktop, Web, Robotics, External systems

---

## Final Recommendation

Do not continue expanding Minecraft-specific planning directly. Before adding substantial HTN/GOAP complexity, introduce:
1. Observation model
2. Belief model
3. Episodic memory
4. Explicit world model
5. Project hierarchy
6. Memory-first knowledge graph

These systems are the missing substrate that turns MemorySmith.Agent from a capable Minecraft bot into the persistent intelligence platform that the broader vision is aiming toward.

> **The most important architectural principle going forward:**
> Planners reason over beliefs and world models. World models are constructed from observations. Observations become memories. Memories synthesize into knowledge. Embodiments merely provide observations and execute actions.
