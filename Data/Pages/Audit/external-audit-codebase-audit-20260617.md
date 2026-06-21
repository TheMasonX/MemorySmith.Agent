# External Audit: MemorySmith.Agent — Codebase Audit
**Source:** External codebase review, 2026-06-17  
**Persisted by:** Implementation agent (Sprint 15 intake)  
**Original filename:** memorysmith_agent_codebase_audit.md

---

## Executive Summary

MemorySmith.Agent is already doing a lot right. It is not a thin wrapper around MemorySmith; it is building a deterministic-first embodied agent stack with:
- a typed memory gateway
- memory-backed blueprint and item registries
- a world model built from typed events
- schema-validated tool dispatch
- a journal for bounded execution history
- a planner/router split that supports future decomposition strategies

The main gap is that the agent still uses MemorySmith in **point solutions** rather than as a **unified knowledge substrate**. Item lookup, blueprint lookup, planner context, chat interpretation, and world-state reasoning all have pieces of memory awareness, but the retrieval and knowledge-graph story is fragmented.

---

## What is Already Strong

### 1) Memory integration is real, not aspirational
The repo already defines an `IMemoryGateway` abstraction for search, page read, page create, and page update. That is the correct seam for durable backing. It keeps MemorySmith external but still first-class in the agent architecture.

### 2) Durable KB-backed registries are a good pattern
`MemorySmithItemRegistry` and `MemorySmithBlueprintRepository` already use MemorySmith pages as source-of-truth content, with local file fallback for offline/dev use.

### 3) The chat interpreter is deterministic where it should be
The interpreter uses aliases and regexes for gather/build/craft/move/status/cancel behaviors.

### 4) Tool dispatch is safer than a raw function runner
`ToolDispatcher` validates arguments against declared input schemas before execution.

### 5) The world model stack is promising
`WorldModel` plus `WorldStateProjector` already give you a typed observation/belief/prediction flow.

### 6) The agent loop is already stateful and resilient
`AgentBackgroundService` owns reconnects, journaling, world-event projection, chat routing, and planner dispatch.

---

## Findings

### Finding A: Retrieval is fragmented
Item registry lookup, blueprint lookup, chat aliases, planner context, and MemorySmith page retrieval each solve a narrow slice. There is no single agent-side knowledge resolver that can answer: what is the canonical item? what blueprints are related? what plan context should be preserved? what world facts are relevant? what memory/page should the planner read next?

This fragmentation is the biggest architectural gap if the goal is better discoverability and durable backing.

### Finding B: Planner extensibility is ahead of implementation
`PlannerRouter` can select a decomposer or fall back to HTN, but the richer strategy space is still mostly aspirational. GOAP and LLM-assisted planning exist as ideas, not fully wired runtime paths.

### Finding C: There is a concrete world-model bug ⚠️
`WorldStateProjector.ApplyBlockMined` increments inventory by `1` even though the event carries a count. That is a correctness bug if the event represents multiple mined blocks in one report. It should reflect the actual event quantity.

**Confidence: 0.97**

### Finding D: Search fallback is useful but too coarse
Both item and blueprint repositories fall back to search using page-id substring containment. Not a robust ranking strategy for ambiguous or expanded vocabularies.

### Finding E: Tool validation is minimal by design
The tool dispatcher validates a subset of JSON schema. Acceptable if tool schemas stay simple.

### Finding F: Knowledge discoverability is still mostly manual
Alias tables are hand-authored, and there is no centralized agent knowledge graph or resolver service yet.

---

## Prioritized Recommendations

1. **Create a unified knowledge resolver.** Combine MemorySmith pages, local registries, aliases, and world facts into one retrieval path.

2. **Fix the world-model count bug immediately.** Use the mined event count rather than hardcoding `1`.

3. **Promote planner routing to a real strategy system.** Wire GOAP/LLM-assisted paths or remove placeholders.

4. **Improve retrieval ranking.** Replace coarse search fallback with scored candidates and confidence thresholds.

5. **Turn the memory patterns into a reusable agent knowledge layer.** Blueprints, items, plans, and memories should share one lookup paradigm.

---

## Confidence

- MemorySmith integration is a strong foundation: **0.93**
- Retrieval is currently fragmented: **0.90**
- `WorldStateProjector` has a count bug: **0.97**
- Planner routing is incomplete relative to the docs: **0.88**
- A unified knowledge resolver will materially help discoverability: **0.94**

---

## Open Questions

- Should the agent keep separate registries for items, blueprints, and task memories, or merge them behind one resolver?
- Should the resolver expose top-N candidates with confidence, or hide ranking behind simpler APIs?
- Should the agent treat MemorySmith pages as one source among several, or the primary durable knowledge store for all human-authored context?
