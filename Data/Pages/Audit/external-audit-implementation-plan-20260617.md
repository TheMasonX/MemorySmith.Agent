# External Audit: MemorySmith.Agent — Implementation Plan
**Source:** External implementation plan review, 2026-06-17  
**Persisted by:** Implementation agent (Sprint 15 intake)  
**Original filename:** memorysmith_agent_implementation_plan.md

---

## Objective
Improve discoverability, world-model quality, and durable knowledge use in MemorySmith.Agent without turning the repo into a MemorySmith rewrite.

---

## Phase 0 — Fix correctness and expose the seams

### 0.1 Fix the world-model count bug ⚠️
**Problem:** `WorldStateProjector.ApplyBlockMined` increments inventory by `1` regardless of the mined event count.  
**Action:** Use the actual event count in the inventory update.  
**Acceptance:** A test verifies multi-block mine events produce the correct inventory total.  
**Confidence: 0.98**

### 0.2 Clarify planner fallback behavior
**Problem:** `PlannerRouter` advertises richer strategies than it actually routes today.  
**Action:** Either wire GOAP/LLM-assisted paths or make the supported strategy set explicit and remove placeholders.  
**Acceptance:** Planner selection behavior is documented and tested.

### 0.3 Add a unified knowledge query object
**Action:** Introduce a query type that can represent: text query, candidate types (item, blueprint, memory, page, task), confidence threshold, top-N requested, optional alias domain, optional source restriction.  
**Acceptance:** Item lookup and blueprint lookup can both call the same resolver interface.

---

## Phase 1 — Build the unified resolver

### 1.1 Create a knowledge resolver service
Build a new service that combines:
- MemorySmith page lookup
- MemorySmith memory lookup
- local registries
- alias tables
- world facts
- task metadata

### 1.2 Add alias-aware ranking
Start with deterministic ranking:
- exact normalized id
- alias match
- page-id match
- title match
- fallback search hit

Return: top result if confidence is high; top-N suggestions if confidence is medium; clarification request if confidence is low.

### 1.3 Share resolver outputs with the planner and chat interpreter
Use the same resolver in: chat command interpretation, item/blueprint lookup, planner context assembly, goal expansion.

**Acceptance:** No duplicate lookup logic remains in item/blueprint/chat code paths.

---

## Phase 2 — Improve discoverability and graph support

### 2.1 Add lightweight graph edges
Model relations between: items, blueprints, memories, pages, goals, observations, facts.

### 2.2 Add traversal helpers
Implement simple traversals: references, backlinks, conflicts, related items, related blueprints, recent facts by goal.

### 2.3 Use graph context for planning
Before a plan is built, fetch a compact context bundle from the resolver: canonical item/page, related memory nodes, relevant facts, conflicting notes, recent observations.

**Acceptance:** Planner tests show context-preserving behavior improves candidate selection and replanning.

---

## Phase 3 — Introduce optional semantic retrieval

### 3.1 Keep lexical retrieval as the default
Do not replace lexical/alias ranking. Keep it as the primary signal.

### 3.2 Add semantic ranking only as a secondary signal
Add semantic ranking only after baseline metrics prove it helps. It should enhance recall, not replace deterministic matching.

### 3.3 Measure before expanding
Track: exact-hit rate, alias-hit rate, false-positive rate, clarification rate, average candidate count, planner success after retrieval.

---

## Phase 4 — Turn MemorySmith into better durable backing

### 4.1 Write back durable discoveries
When the agent confirms a stable fact, persist it as: a short memory if it is operational; a page if it is curated and long-form.

### 4.2 Keep human control over durable artifacts
Do not auto-write low-confidence knowledge. Require explicit approval for high-impact writes.

### 4.3 Make the KB searchable by intent
Support common agent intents: find item, find blueprint, find related memory, find prior decision, find task history, find source-backed evidence.

---

## Test Plan

- unit tests for the resolver ranking
- unit tests for alias normalization
- regression test for multi-count mining
- planner tests for strategy selection
- integration tests for item and blueprint lookup
- tests that low-confidence queries return suggestions rather than auto-picks

---

## Success Criteria

- The agent has one canonical retrieval path for knowledge.
- Item and blueprint lookup are no longer isolated special cases.
- Planner context uses durable knowledge rather than only live state.
- Low-confidence matches are never silently chosen.
- MemorySmith remains the backing store, not a Minecraft-specific subsystem.

---

## Confidence

- Unified resolver is the highest-value change: **0.96**
- The mining count bug should be fixed immediately: **0.98**
- Graph-aware retrieval will materially improve discoverability: **0.89**
- Semantic retrieval should remain secondary for now: **0.90**
- Durable writes should stay approval-gated: **0.94**
