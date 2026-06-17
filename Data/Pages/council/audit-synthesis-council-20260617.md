# MemorySmith Council Review — External Audit Synthesis (All 7 Docs)
**Date:** 2026-06-17  
**Branch:** `sprint-5-tool-safety` (PR #1)  
**Scope:** Synthesis of all 7 external audit documents received across Sprint 14–15 intake  
**CI status:** Sprint 14 commits pending (one test fix queued — Sprint 15 P0-CI)  
**Seats:** Source-Grounded Archivist · Data Model Architect · Retrieval Specialist · Human Learning Advocate · Skeptical Reviewer · Synthesizer  
**Additional:** Anonymous Peer Review

---

## Audit documents reviewed

| Batch | Document | Date |
|-------|----------|------|
| Sprint 14 | Summary Design Doc | 2026-06-17 |
| Sprint 14 | RFC — Cognition Substrate | 2026-06-17 |
| Sprint 14 | Concrete Refactor Plan | 2026-06-17 |
| Sprint 14 | Strategic Review | 2026-06-17 |
| Sprint 15 | Codebase Audit | 2026-06-17 |
| Sprint 15 | Implementation Plan | 2026-06-17 |
| Sprint 15 | Design Doc | 2026-06-17 |

---

## Cross-audit consensus findings

### C1 — Architecture direction (unanimous, 7/7 audits)
All seven documents agree: the project should evolve from a Minecraft bot toward a **persistent embodied cognition platform**. Minecraft is the first adapter, not the identity. The product is the cognition stack: memory, observation, belief, planning, reflection, execution, and long-horizon project continuity.

### C2 — Missing substrate (6/7 audits)
Six of seven documents identify the same missing cognitive layers:
- **Observation pipeline** — raw adapter events are not normalized before planning
- **Belief layer** — no stable interpreted state between observations and planning
- **Episodic memory** — no experience records; no lesson capture
- **Reflection service** — no post-action evaluation or lesson writing

### C3 — Mining count bug (3/7 audits, confidence 0.97–0.98)
Three independent sources identify `WorldStateProjector.ApplyBlockMined` hardcoding count=1 as a correctness bug. This is the highest-confidence specific fix in the entire audit set.

### C4 — Unified knowledge resolver needed (3/7 audits, confidence 0.94–0.96)
Three documents specifically call for a single resolver service that answers: canonical item, related blueprints, relevant plan context, memory/page retrieval, world facts. Currently these are fragmented across separate point solutions.

### C5 — Deterministic-first, LLM-secondary (5/7 audits)
All five audits that address planning style agree: deterministic HTN + GOAP first; LLM only for novelty, ambiguity, or recovery. No audit recommends increasing LLM use.

### C6 — Memory-first, page-second (5/7 audits)
Pages should become synthesized artifacts from memory clusters, not the default write target. Small operational notes should be memories.

### C7 — Lexical + alias retrieval first, semantic second (2/7 audits — new in batch 2)
The implementation plan and design doc both argue lexical/alias retrieval is more reliable than embeddings for agent query patterns (identifiers, block names, tool names). Semantic ranking should be measured before deployment.

### C8 — Confidence-gated retrieval (1/7 audits — new in batch 2)
The design doc introduces an explicit principle: low-confidence matches must never auto-pick. Top-N suggestions or clarification requests instead.

---

## Seat 1 — Source-Grounded Archivist
**Confidence: 0.93**

**What's already done vs. what's not:**

| Gap identified in audits | Status |
|--------------------------|--------|
| Mining count bug | ❌ Open — Sprint 15 P0 |
| Observation pipeline | ❌ Open — Phase 7-B |
| Belief layer | ❌ Open — Phase 7-C |
| Episodic memory | ❌ Open — Phase 7-C |
| Unified knowledge resolver | ❌ Open — Phase 7-B (approx.) |
| Memory-first writes | ⚠️ Partial — IMemoryGateway exists; operational notes still go to pages |
| Deterministic-first planning | ✅ Done — HTN + GOAP + decomposers |
| Adapter isolation | ✅ Done — CommonMinecraftBlocks, IWorldAdapter |
| Tool safety | ✅ Done — ToolDispatcher schema validation |
| Journal | ✅ Done — AgentJournal, 15 call sites |
| World model stub | ✅ Done — ObservationState, BeliefState, PredictionState, IWorldModel |
| Inventory key normalization | ✅ Done — Sprint 14 P1b |

**Specific finding — count bug:** The `BlockMinedEvent` record carries a `Count` property. The projector uses `1`. Every multi-block mine since the projector was written has under-counted inventory. All IsComplete checks in gather goals are affected. Fix is one character: `e.Count` instead of `1`.

---

## Seat 2 — Data Model Architect
**Confidence: 0.91**

**Synthesis of what the audits are asking for vs. what the codebase has:**

The second-batch audits are materially additive to the first batch, not contradictory:
- Batch 1 described the target architecture (what layers should exist)
- Batch 2 describes the **gap analysis** against the current codebase (what concretely is missing or wrong)

Together they form a complete picture:
- Immediate fix: mining count bug (0.97 confidence, three independent sources)
- Near-term build: unified resolver service (Phase 7-B, higher priority than previously ranked)
- Medium-term build: observation pipeline → belief layer → episodic memory (Phase 7-B/C)
- Long-term: reflection, page synthesis (Phase 7-F/G)

**New from batch 2 — resolver placement question:** The design doc asks whether the resolver should live under `Agent.Memory`, `Agent.Planning`, or a new `Agent.Knowledge` project. Given the 10-project solution constraint (Sprint 6 council rejected adding .csproj files), the most pragmatic near-term answer is `Agent.Memory` (it already has IMemoryGateway). A dedicated `Agent.Knowledge` project can be added when the resolver scope justifies it.

---

## Seat 3 — Retrieval Specialist
**Confidence: 0.94**

**The fragmented retrieval story (Finding A from codebase audit):**

Today, a query for "what is oak_log?" requires checking:
1. `GoalFactory.BuiltInDirectMineItems` — does it exist as a gather item?
2. `MemorySmithItemRegistry` — does a wiki page describe it?
3. `CommonMinecraftBlocks.DirectMineBlocks` — is it mineable?
4. `HtnTaskLibrary.CraftingChainOrder` — is it craftable?
5. World facts — was it recently mined?

No single entry point answers all of these. The unified resolver described in batch 2 would do so. Implementation priority: after the immediate bug fixes but before the observation pipeline.

**Retrieval ranking guidance (new from batch 2):** Lexical + alias first (identifiers, block names, tool names), then semantic if lexical recall is insufficient. This is correct for the current query pattern. The design doc's "5-step" ranking (normalize → alias-expand → lexical-rank → top-N → clarify) is a clean, testable default.

---

## Seat 4 — Human Learning Advocate
**Confidence: 0.96**

**User-facing impact of the mining count bug:**

Every time the bot mines multiple blocks in one `BlockMined` event and the count is > 1:
- Inventory is under-counted
- IsComplete checks for gather goals may never fire (think: goal requests 5 oak_logs, bot mines 5 in batch, but inventory only shows 1 after the ProjectorApply)
- User sees the bot "stuck" gathering indefinitely

This is the most impactful runtime bug currently in the codebase. The fix is trivial. It should be the first code change in Sprint 15.

**Coal pre-gather (Sprint 15 P0-coal from handoff):** Without coal, SmeltItem silently fails. User sees "Crafting 1x iron pickaxe" → tries to smelt → fails with no material-specific error → error recovery LLM fires → frustrating loop. Fixing coal pre-gather in Sprint 15 completes the iron tool crafting story started in Sprint 14.

---

## Seat 5 — Skeptical Reviewer
**Confidence: 0.88**

**Concerns about the synthesis:**

**Concern 1 (non-blocking):** The mining count bug is described with high confidence (0.97) but I want to verify the event schema. Looking at `BlockMinedEvent` — it carries a `Count` property that represents the number of blocks mined. But does Mineflayer actually send multi-block events, or does it send one event per block? If Mineflayer always sends `count=1`, the bug exists in the projector but is never triggered at runtime. Either way, the fix to use `e.Count` is strictly more correct.

**Concern 2 (non-blocking):** The unified resolver is described as the "highest-value change" across multiple audits. However, the current retrieval fragmentation doesn't block any active functionality — it only constrains future growth. The resolver should be planned but not rushed ahead of operational correctness fixes.

**Concern 3 (blocking if wrong):** Batch 2's design doc asks "Should the journal become searchable knowledge?" — this open question, if answered wrong, could significantly complicate the journal. Current journal is deliberately bounded and non-searchable. Recommend keeping it that way until the unified resolver exists to absorb searchable state.

**Verdict:** Mining count bug is blocking. Resolver is high priority but not blocking today. Cognition substrate is Phase 7. All three sprint-15 correctness fixes are straightforward. No blocking findings from the synthesis itself.

---

## Seat 6 — Synthesizer
**Confidence: 0.94**

**Blocking findings from synthesis: ONE — mining count bug. Must fix before Sprint 15 council.**

**Priority matrix after synthesis:**

| Priority | Item | Sprint | Phase |
|----------|------|--------|-------|
| P0 (bug fix) | `ApplyBlockMined` uses `e.Count` not `1` | Sprint 15 | Now |
| P0 (correctness) | Coal pre-gather in `DecomposeCraftItem` | Sprint 15 | Now |
| P1 | `_lastRecoveredGoalName` clear on goal completion | Sprint 15 | Now |
| P1 | Stall detection in `DispatchActionsAsync` | Sprint 15 | Now |
| P2 | Unified knowledge resolver (design + stub) | Sprint 16 | Phase 7-B |
| P2 | Observation pipeline normalization | Sprint 17 | Phase 7-B |
| P3 | Belief layer, episodic memory | Sprint 18+ | Phase 7-C |
| P3 | Planner routing documentation (remove aspirational claims) | Sprint 16 | Phase 7-A |
| P4 | Confidence-gated retrieval | Sprint 18+ | Phase 7-D |
| P4 | Reflection service | Sprint 19+ | Phase 7-F |

**What the new audits add to existing direction:**
- Immediate: mining count bug fix (new P0, not in previous handoffs)
- Near-term: unified resolver design (now higher priority than previously ranked; Phase 7-B, not 7-A)
- Principle: confidence-gated retrieval (new explicit guard; informs resolver design)
- Lexical-first retrieval (new explicit guidance for resolver implementation)

**Council decision: Proceed with Sprint 15. Mining count bug is P0. Synthesis is approved. No blockers to implementation.**

---

## Anonymous Peer Review

**Reviewer: Anonymous (external)**  
**Confidence in overall direction: 0.91**

Having read all seven audit documents, I observe the following:

**What is consistent:** The seven documents represent an unusually coherent external consensus. They do not contradict each other in any material way. The batch-2 documents add specificity (the count bug, the resolver design question, the lexical-first principle) rather than overturning batch-1 conclusions.

**What I would add:** The implementation plan's "Phase 0.2 — clarify planner fallback" is underweighted. `PlannerRouter` currently has documented strategy paths (GOAP, LLM-assisted) that are not wired. This is a source of documentation debt that will confuse future agents working on the planner. Even if the paths are not implemented, they should be clearly marked as aspirational in comments and tests.

**What I would caution against:** The unified resolver is described in aspirational terms in all three batch-2 documents. The risk is scope inflation — resolver ends up absorbing the entire knowledge graph, belief layer, and retrieval pipeline before any of them are built. Recommend scoping Sprint 16's resolver strictly: one interface, two concrete sources (MemorySmith + local registry), no graph traversal yet. Grow from there.

**Rating: APPROVE with the above notes recorded in deferred backlog.**

---

## Updated Phase 7 Roadmap (post-synthesis)

| Sub-phase | Focus | Sprint estimate |
|-----------|-------|----------------|
| 7-A (in progress) | Architecture inventory; planner routing cleanup | Sprint 16 |
| 7-B | Unified resolver stub (IKnowledgeResolver, two sources) | Sprint 17 |
| 7-C | Observation pipeline normalization | Sprint 18 |
| 7-D | Belief layer + IBeliefState | Sprint 19 |
| 7-E | Episodic memory + IEpisode | Sprint 20 |
| 7-F | Planner input migration to world model + beliefs | Sprint 21 |
| 7-G | Reflection service | Sprint 22 |
| 7-H | Page synthesis from memory clusters | Sprint 23 |
| 7-I | Adapter generalization audit | Sprint 24 |
