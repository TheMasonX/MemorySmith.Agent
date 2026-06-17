# External Audit: MemorySmith.Agent — Concrete Refactor Plan
**Source:** External architecture review, 2026-06-17  
**Persisted by:** Implementation agent (Sprint 14 intake)  
**Original filename:** MemorySmithAgent_Concrete_Refactor_Plan.md

---

## Overview

This plan assumes the current repository state already includes:
- the modular solution
- the Blazor host
- agent background service orchestration
- planner and tool dispatch infrastructure
- growing observability and journaling
- early world-model and routing work

The next step is not to add more gameplay features. It is to rewire the cognition pipeline.

## Primary implementation order

1. Memory abstractions and storage policy
2. Observation pipeline
3. Belief and episode models
4. World model
5. Planner input migration
6. Reflection and consolidation
7. Page synthesis from memory clusters
8. Minecraft adapter cleanup and generalization

---

## Phase 0 — Inventory and stabilization

### Goal
Document what already exists, what is working, and what should not be broken.

### Tasks
- Inventory all current memory, page, planner, goal, and world-adapter files.
- Mark which files are core cognition, which are adapter-only, and which are synthesis/ops.
- Add a migration note to the repo wiki explaining the new memory-first direction.

### Deliverables
- architecture inventory page
- subsystem map
- migration checklist

### Key decision
Do not rewrite the whole solution. Preserve current working code and layer new abstractions around it.

---

## Phase 1 — Memory-first knowledge substrate

### Goal
Make memory the operational primitive.

### Tasks
- Define `IMemoryStore` or equivalent abstraction.
- Define `MemoryNode`.
- Define `MemoryEdge`.
- Add tags, confidence, source, timestamps, and node kinds.
- Add lightweight edit/update operations.
- Add graph traversal queries for neighbors, tagged sets, and recent memories.

### Suggested node kinds
- breadcrumb
- observation-derived
- belief-derived
- episode-derived
- project note
- reference
- synthesis pointer

### Suggested edge kinds
- semantic
- temporal
- causal
- spatial
- contradiction
- refinement
- part-of
- supports
- derived-from

### Tests
- write/read node
- tag filter
- neighbor traversal
- edge creation
- duplicate merge behavior
- confidence update behavior

### Exit criteria
A small note can be written as a memory in one line of code and searched without creating a page.

---

## Phase 2 — Observation pipeline

### Goal
Normalize raw adapter events into structured observations.

### Tasks
- Add `Observation` model.
- Add an observation ingestion service.
- Convert adapter events to observations in one place.
- Preserve raw payloads and adapter metadata.
- Ensure observations are immutable.

### Design note
The adapter should emit raw facts. The observation layer should standardize them. The planner should not need to know raw event schema details.

### Tests
- event-to-observation conversion
- timestamp and provenance preservation
- adapter metadata survival
- malformed event handling

### Exit criteria
All major world events flow through a uniform observation pipeline.

---

## Phase 3 — Belief and episodic memory

### Goal
Separate what happened from what is believed to be true.

### Tasks
- Add `Belief`.
- Add `Episode`.
- Create a belief reconciliation service.
- Create episode compression after action sequences and failures.
- Track support and contradiction links from memories.

### Belief update rules
- Multiple concordant observations raise confidence.
- Contradictory evidence lowers confidence.
- Fresh observations should override stale low-confidence beliefs.
- Beliefs should be queryable by scope and confidence.

### Episode rules
Create episodes for: goal completion, failure sequences, recovery sequences, notable achievements, major world changes.

### Tests
- belief consolidation
- contradiction handling
- episode generation
- episode-to-memory links

### Exit criteria
The system can remember experiences and revise its understanding of the world.

---

## Phase 4 — World model

### Goal
Create a single queryable state object that planners can trust.

### Tasks
- Define `IWorldModel`.
- Add belief-driven state projection.
- Track current position, inventory, known resources, hazards, and structures.
- Track uncertainty explicitly.
- Add compact summary and detailed snapshot forms.
- Add reconciliation from new observations and episodes.

### Important rule
The world model is not a second source of truth. It is the structured interpretation of the source facts and beliefs.

### Tests
- state projection from observations
- uncertainty reporting
- compact summary vs detailed snapshot
- stale-state refresh behavior

### Exit criteria
Planners can query a world model instead of directly inspecting adapter state.

---

## Phase 5 — Planner refactor

### Goal
Move planning from raw adapter state to world model and beliefs.

### Tasks
- Update planner interfaces to accept goal + world model + beliefs.
- Split planning into: strategic, operational, tactical, reactive.
- Keep HTN and GOAP in the tactical layer.
- Use LLM only for novel or ambiguous situations.
- Add explicit replan hooks from failure and contradiction handling.

### Integration points
- current goals
- decomposer registry
- task library
- tool execution
- world model uncertainty

### Tests
- known goal decomposition
- novel goal fallback
- replan after failure
- belief-sensitive planning
- interruption and resume behavior

### Exit criteria
Planner behavior is explainable, deterministic by default, and grounded in the world model.

---

## Phase 6 — Reflection and consolidation

### Goal
Close the cognitive loop.

### Tasks
- Add a reflection service.
- Evaluate action outcomes.
- Write lessons back to memory.
- Update episode records.
- Update beliefs and uncertainty.
- Trigger page synthesis when a memory cluster becomes meaningful.

### Reflection triggers
- goal completion
- repeated failure
- world model contradiction
- major state change
- user interrupt
- project milestone reached

### Tests
- success reflection
- failure reflection
- learning note creation
- synthesis trigger conditions

### Exit criteria
The agent improves from experience instead of only executing.

---

## Phase 7 — Page synthesis and demotion of pages

### Goal
Make pages the curated output of memory, not the default note format.

### Tasks
- Define synthesis jobs from clusters of memories/episodes/beliefs.
- Generate or update pages from those clusters.
- Add page provenance links back to the source memories.
- Update documentation to state that pages are synthesized artifacts.

### Suggested page types
- project report
- blueprint
- settlement survey
- strategy summary
- knowledge digest
- long-form lesson document

### Exit criteria
Operational notes live as memories first, and pages are created when synthesis is valuable.

---

## Phase 8 — Adapter cleanup and generalization

### Goal
Make Minecraft clearly one embodiment among many.

### Tasks
- Audit Minecraft-specific assumptions in planner, goals, and UI.
- Move adapter-specific behavior behind adapter interfaces.
- Rename or document any types that accidentally imply Minecraft is the center of the domain.
- Add a future-adapter placeholder architecture note.

### Exit criteria
The codebase is clearly an embodied agent framework, not a Minecraft bot with some reusable pieces.

---

## Current repo hotspots to inspect first

### Host / composition root
`WebUI.Blazor/Program.cs` — service registration, world model wiring, journal wiring, planner routing, command validation, SignalR push path.

### Background loop
`WebUI.Blazor/AgentBackgroundService.cs` — action queue, reconnect loop, chat handling, error recovery, event ingestion, status publishing.

### Planning
`Agent.Planning/ChatModels.cs`, `Agent.Planning/Goals/*`, planner/router/decomposer pieces, HTN task library.

### Core models
`Agent.Core/Models/ActionPlan.cs`, world state projection types, any goal interfaces that still conflate status with reasoning.

### Memory / wiki side
`Data/Memories/Core/*`, `Data/Pages/*`, any current page documents that are doing memory's job.

---

## Immediate refactor sequence

1. Add memory and observation abstractions without changing planner behavior.
2. Route new observations into the memory substrate and world model.
3. Use the world model in `/api/agent/status` and `/api/agent/worldmodel`.
4. Move any note-like writes from page creation into memory creation.
5. Teach the planner to consume beliefs and world model snapshots.
6. Add reflection after action completion and error recovery.
7. Add page synthesis from memory clusters.

---

## Quality gates

Before merging major refactor work:
- every new model has tests
- memory writes are lightweight and searchable
- planner still works for the current goals
- adapter-specific behavior remains isolated
- observability endpoints continue to function
- no page becomes the default substitute for a memory

---

## Recommended naming convention

Use names that reinforce the architecture: `Observation`, `MemoryNode`, `MemoryEdge`, `Belief`, `Episode`, `WorldModel`, `Project`, `ReflectionService`, `PageSynthesisService`.

Avoid names that imply pages are the only knowledge store, Minecraft is the domain root, or the planner directly owns world truth.

---

## Definition of done for the refactor

The refactor is complete when:
- small operational knowledge is written as memory by default
- pages are generated from memory clusters
- planning is belief-driven
- episodes are captured automatically
- the world model is explicit and queryable
- Minecraft is clearly one adapter among several possible embodiments
