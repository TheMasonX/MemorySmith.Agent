# External Audit: MemorySmith.Agent — Design Doc
**Source:** External design review, 2026-06-17  
**Persisted by:** Implementation agent (Sprint 15 intake)  
**Original filename:** memorysmith_agent_design_doc.md

---

## Goal
Evolve MemorySmith.Agent into a persistent embodied intelligence platform with Minecraft as the testbed world adapter.  
**Secondary goal:** Use MemorySmith as durable backing for human-authored, source-grounded knowledge.

---

## Design Intent

Minecraft is not the product. It is the proving ground.

The product is the cognition stack: memory, observation, belief, planning, reflection, execution, long-horizon project continuity.

MemorySmith provides the durable backing store. MemorySmith.Agent should provide the reasoning and world-action layer above it.

---

## Principles

### 1) Deterministic first
Known command patterns, known decompositions, and known world actions should stay deterministic. LLMs are for ambiguity, novelty, and recovery.

### 2) Memory-backed, page-second
Short operational knowledge should live as small graphable memories. Long-form synthesis should live as pages.

### 3) Adapter-agnostic core
Minecraft is the first adapter, not the identity of the system. The core planner and memory model should not hardcode Minecraft semantics any more than necessary.

### 4) Confidence-gated retrieval
The system should never silently auto-pick low-confidence knowledge candidates. It should return top-N suggestions or ask for clarification.

### 5) Reflection closes the loop
Actions should feed back into memory, world state, journal, and future planning.

---

## Proposed Architecture

```
World Adapter(s)
  -> Observation normalization
  -> Unified knowledge resolver
  -> World model
  -> Planner router
  -> Execution
  -> Journal / reflection
  -> MemorySmith page synthesis
```

### Observation normalization
Raw adapter events become typed observations. Those observations should be stored and transformed consistently before planning consumes them.

### Unified knowledge resolver
This is the most important design addition for the agent repo.

It should answer queries across: MemorySmith pages, MemorySmith memories, local item registries, blueprint repositories, task/goal metadata, current world facts, alias tables.

The resolver should support: lexical retrieval, alias expansion, optional semantic ranking later, top-N results, confidence scores, source provenance.

### World model
The world model should represent: current position, inventory, threats, resources, active goals, uncertainty, recent observations, stable beliefs.

It should not be a bag of raw events. It should be a queryable interpretation layer.

### Planner router
Planner selection should be explicit and observable:
- direct decomposer when available
- HTN when the goal matches known phases
- GOAP when no HTN path fits
- LLM-assisted only when deterministic paths fail or are ambiguous

### Journal and reflection
The journal is not just logs. It should be a compact operational history that can feed later retrieval, debugging, and synthesis.

---

## How MemorySmith Should Be Used by the Agent

MemorySmith should be the durable human-facing backing store for: blueprints, long-form project decisions, world/agent research notes, task histories, source-grounded design rationale, codebase KBs.

The agent should keep its own runtime-specific knowledge layer, but all durable human-authored knowledge should be able to flow into MemorySmith.

---

## Search and Discoverability Strategy

For the agent repo, the best near-term approach is:
1. normalize
2. alias-expand
3. lexical rank
4. return top-N if confidence is medium
5. ask for clarification if confidence is low

### Why
Most agent queries are not natural-language essays. They are often identifiers, partial phrases, tool names, block names, blueprint names, or task references. Lexical retrieval and aliases are more reliable than embeddings for those cases.

---

## Memory/Graph Direction

The agent repo would benefit from a lightweight knowledge graph built on: memories, pages, items, blueprints, observations, beliefs, tasks.

Useful edge types: references, supports, contradicts, derived-from, expands, related-to, produced-by.

That graph should live as a local, deterministic structure first. It does not need a separate database to become useful.

---

## Assumptions

- MemorySmith remains the durable backing store for human-authored knowledge.
- The agent repo remains responsible for planning, execution, world modeling, and runtime decision-making.
- Low-confidence matches must not auto-pick.
- Minecraft remains the testbed world adapter rather than the system identity.

---

## Open Questions

- Should the unified resolver live under `Agent.Memory`, `Agent.Planning`, or a new `Agent.Knowledge` boundary?
- Should the agent index both MemorySmith pages and repo-local structured data into one local retrieval layer?
- Should semantic embeddings be added only after lexical + alias + graph ranking are measured?
- Should the journal become searchable knowledge, or remain purely operational history?
- Should MemorySmith pages be the canonical source for all durable design artifacts, or only for a subset?

---

## Confidence

- The agent needs a unified knowledge resolver: **0.96**
- Lexical + alias-first retrieval is the right near-term path: **0.91**
- The long-term direction should be graph-aware: **0.87**
- The planner should stay deterministic-first: **0.93**
- MemorySmith should remain backing, not the brain: **0.95**
