# MemorySmith Council Review — Sprint 17
**Date:** 2026-06-17  
**Branch:** `sprint-5-tool-safety` (PR #1)  
**Head commit reviewed:** `05f9a6d` (AGENTS.md curl examples — final Sprint 17 push)  
**CI status:** ✅ build-and-test: success (run 27721607397, completed 2026-06-17T21:42:24Z)  
**Seats:** Source-Grounded Archivist · Data Model Architect · Retrieval Specialist · Human Learning Advocate · Skeptical Reviewer · Synthesizer  
**Additional:** Anonymous Peer Review

---

## Sprint 17 changes under review

| File | Change |
|------|--------|
| `Agent.Core/CommonMinecraftBlocks.cs` | P0: expand DirectMineBlocks with raw ore drops (diamond, coal, emerald, redstone, lapis_lazuli) + emerald_ore / deepslate_emerald_ore; update class comment |
| `Agent.Memory/IKnowledgeResolver.cs` | P1: add `CandidateType.WorldFact` to enum |
| `Agent.Memory/LocalKnowledgeResolver.cs` | P0+P1+D2: ClassifySpec fix; WorldFact third source; D2 SearchAsync raw-query comment; updated class XML doc |
| `WebUI.Blazor/Program.cs` | P1: wire `Func<WorldState?>` factory delegate to LocalKnowledgeResolver |
| `MemorySmith.Agent.Tests/KnowledgeResolverTests.cs` | 4 new tests (ClassifySpec_Diamond, ClassifySpec_OakLog, WorldFact_Match, WorldFact_LowConfidence); MakeResolver gains worldState param |
| `AGENTS.md` | D3: "Testing the /api/agent/resolve endpoint" subsection with 5 curl examples |

---

## Seat 1 — Source-Grounded Archivist
**Confidence: 0.95**

**P0 — ClassifySpec fix correctness:**

Before fix, `ClassifySpec("diamond")` with SourceBlocks=["diamond_ore"]:
- RequiresSmelting=false → skip
- SourceBlocks.Contains("diamond") → ["diamond_ore"] does NOT contain "diamond" → false
- SourceBlocks.Count=1 > 0 → returns **Craftable** ✗

After fix:
- RequiresSmelting=false → skip
- `DirectMineBlocks.Contains("diamond")` — "diamond" is now in the expanded set → **true** → returns **DirectMineable** ✓

Verified the set expansion is correct:
- "diamond" ← mined from diamond_ore / deepslate_diamond_ore
- "coal" ← mined from coal_ore / deepslate_coal_ore  
- "emerald" ← mined from emerald_ore / deepslate_emerald_ore
- "redstone" ← mined from redstone_ore / deepslate_redstone_ore
- "lapis_lazuli" ← mined from lapis_ore / deepslate_lapis_ore

**emerald_ore and deepslate_emerald_ore block names also added** — a net improvement over the original sprint 14 set. ✓

**oak_planks regression check:** MakeSpec("oak_planks", sources=["oak_log"]) → SourceBlocks=["oak_log"]; DirectMineBlocks does NOT contain "oak_planks"; SourceBlocks does NOT contain "oak_planks" (it contains "oak_log"); SourceBlocks.Count > 0 → **Craftable** — correct, no regression. ✓

**P1 — WorldFact source placement:**
WorldFact scan runs AFTER gateway in the pipeline. StructuredFacts is bounded at 1000 and key-contains filtering further reduces this. The `worldStateAccessor?.Invoke()` null-coalescing means WorldFact is silently skipped when no accessor is provided (agent disabled or test without state). ✓

**D2 — Comment accuracy:** The comment correctly documents that `query.Query` (not `normalizedId`) is passed to `SearchAsync`. ✓

**D3 — AGENTS.md curl examples:** All 5 examples use correct parameter names matching the `/api/agent/resolve` endpoint signature in Program.cs. ✓

---

## Seat 2 — Data Model Architect
**Confidence: 0.93**

**IKnowledgeResolver.CandidateType.WorldFact enum placement:**
Added as the 6th value, after WikiPage. The XML doc accurately describes: confidence decay (0.70 within 60s, 0.50 after), source (WorldState.StructuredFacts). ✓

**LocalKnowledgeResolver constructor evolution:**
```
LocalKnowledgeResolver(IItemRegistry, IMemoryGateway)                        — Sprint 16
LocalKnowledgeResolver(IItemRegistry, IMemoryGateway, Func<WorldState?>?)     — Sprint 17
```
The optional third parameter with default null maintains backward compatibility — the 2-arg constructor still works. ✓

**WorldFact confidence design:**
| Scenario | Confidence | Rationale |
|----------|-----------|-----------|
| Registry exact hit | 0.95 | Deterministic, canonical |
| WorldFact recent (< 60s) | 0.70 | High-quality live observation |
| Gateway score=1.0 | 0.60 | Best-case wiki result |
| WorldFact stale (≥ 60s) | 0.50 | Observed but potentially outdated |
| Gateway score=0.5 | 0.30 | Average wiki result |

A recent WorldFact (0.70) correctly outranks the best gateway result (0.60). A stale WorldFact (0.50) sits between good and mediocre gateway results. The decay curve is a step function — simple and testable. ✓

**Concern (deferred):** A step function for confidence decay (binary < 60s / ≥ 60s) is coarse. For facts that are genuinely important (position, health), a linear decay over 5 minutes would be more accurate. For Phase 7-C, consider a smoother decay curve. D1.

**DirectMineBlocks expansion — naming accuracy:**
The set is called `DirectMineBlocks` but now contains item IDs ("diamond") that are not blocks. The class comment was updated: "Blocks and raw item drops the bot can obtain by mining directly". This is the right pragmatic compromise given the 10-project constraint (no new project for a separate `ItemDropRegistry`). ✓

---

## Seat 3 — Retrieval Specialist
**Confidence: 0.92**

**WorldFact key matching strategy:**
The resolver uses `.Contains(normalizedId, OrdinalIgnoreCase)` on fact keys. Let me trace a few scenarios:

| Query | NormalizedId | Fact key | Matches? | Expected |
|-------|-------------|----------|----------|---------|
| "oak_log" | oak_log | "oak_log" | ✓ (exact) | Yes — inventory or observation fact |
| "oak" | oak | "oak_log" | ✓ ("oak" in "oak_log") | Borderline — broad match |
| "inventory" | inventory | "inventory:oak_log" | ✓ | Yes — designed for this |
| "position" | position | "position:x" | ✓ | Yes |
| "iron" | iron | "iron_ore" | ✓ ("iron" in "iron_ore") | Useful for broad queries |

The Contains-based matching is intentionally broad. For Sprint 17 stub quality, this is acceptable. For Phase 7-C, consider supporting prefix-only matching or a more structured fact schema.

**Concern (deferred):** Very short queries ("a", "on") could match almost any fact key with Contains. The confidence threshold (default 0.0, but callers can set higher) is the natural guard. Recommend documenting that a confidence threshold ≥ 0.3 is sensible when WorldFact results are noisy. D2.

**Pipeline order rationality:**
The handoff placed WorldFact after gateway. Per confidence ordering (WorldFact recent > gateway best), this means WorldFacts aren't visible to the TopN gate for the gateway step. But WorldFacts ARE collected after gateway (no gate on the WorldFact loop), and the final sort+Take(TopN) promotes them to the top. So a recent WorldFact (0.70) WILL appear above a 0.54 gateway result in the output even when gateway filled 5 slots. ✓

**D2 comment placement:** The raw-query comment is positioned immediately above the `memory.SearchAsync` call — exactly where a future reader would look. ✓

---

## Seat 4 — Human Learning Advocate
**Confidence: 0.96**

**User-facing impact of Sprint 17:**

| Before | After |
|--------|-------|
| `GET /api/agent/resolve?q=diamond` returned `type: Craftable` — confusing for any UI showing "how to get X" | Returns `type: DirectMineable` — bot will mine diamond_ore, not try to craft |
| No AGENTS.md guidance on testing the resolver | 5 curl examples covering all major use cases + 3 behavior notes |
| Resolver had no runtime context — didn't know what the bot has observed | `WorldFact` candidates expose live inventory/position/block observations |

**ClassifySpec fix impact on the agent loop:**
GoalFactory uses `IItemRegistry` and `CandidateType` indirectly when resolving goals. If PlannerRouter or HtnTaskLibrary consults the resolver in Phase 7-C+, correct classification of "diamond" as DirectMineable rather than Craftable will prevent the planner from trying to craft diamond (which has no recipe). Sprint 17 proactively fixes this before the resolver is wired to planning. ✓

**WorldFact user story (even as stub):** If a user asks the bot "do you have any oak logs?" in a future Phase 7-C NL interface, the resolver can now surface `{type: WorldFact, id: "oak_log", detail: "5"}` — a live answer without hitting the wiki. ✓

---

## Seat 5 — Skeptical Reviewer
**Confidence: 0.88**

**Concern 1 — WorldFact and TopN starvation (deferred):**
If registry=1 hit and gateway fills TopN-1 remaining slots, the `if (candidates.Count < query.TopN)` gate for gateway will add up to TopN-1 results. After gateway, `candidates.Count = TopN`. The WorldFact loop runs with no gate, so WorldFacts ARE added (breaking through the soft limit). The final `Take(query.TopN)` then trims the combined set. This is correct behavior — WorldFact candidates compete fairly.

BUT: if TopN=1 and registry found a hit, gateway is skipped (candidates.Count=1 = TopN), and WorldFacts ARE still collected (no gate). For TopN=1 with a registry hit, a recent WorldFact at 0.70 would DISPLACE the registry hit at 0.95... wait, no — sort is descending by confidence. Registry hit 0.95 > WorldFact 0.70. Registry hit wins. ✓ No starvation issue.

**Concern 2 — WorldFact timestamp precision (non-blocking):**
The test `WorldFact_LowConfidenceForOldFact` constructs a fact with `DateTimeOffset.UtcNow.AddSeconds(-120)`. The check `age <= WorldFactRecencyThreshold` computes `DateTimeOffset.UtcNow - fact.Timestamp` in the resolver. There is a tiny race: if the test machine is paused between `MakeResolver` call and the `age` computation, a fact that was "120s old" at construction could be "slightly more than 120s old" at check time. Since threshold is 60s and fact is 120s old, the margin is 60s — no flakiness risk. ✓

**Concern 3 — Missing test for WorldFact null accessor (non-blocking):**
There is no explicit test for `MakeResolver(worldState: null)` verifying that WorldFacts are silently skipped. The existing `Resolve_UnknownQuery_ReturnsEmptyWithNoAmbiguity` test uses the 2-param MakeResolver (which now passes `worldState: null` via the default), effectively testing this. ✓

**Concern 4 — `using System.Collections.Generic` in IKnowledgeResolver.cs (non-blocking):**
The file retains `using System.Collections.Generic;` at the top. With `ImplicitUsings` enabled globally in this project, this `using` is redundant but harmless. Not a warning since it's not in a file-scoped namespace. ✓

**Concern 5 — emerald_ore addition to DirectMineBlocks (deferred):**
The sprint 17 expansion added `"emerald_ore"` and `"deepslate_emerald_ore"` to DirectMineBlocks — these were missing from the Sprint 14 P1a set. This is a net correctness improvement, but it was not called out explicitly in the sprint spec. The fix is correct per game mechanics (emerald ore drops emerald or experience; bot mines it directly). D3 — note this in phase6-tasks.

**Overall verdict:** Sprint 17 is well-scoped. No blocking concerns. All 5 concerns are deferred or already resolved.

---

## Seat 6 — Synthesizer
**Confidence: 0.94**

**Blocking findings: NONE**

**Deferred findings:**

| ID | Finding | Priority |
|----|---------|----------|
| D1 | WorldFact confidence decay is a step function (< 60s → 0.70, else 0.50). Consider a smoother exponential decay in Phase 7-C when observation timing matters more | P3 — Sprint 19+ |
| D2 | Very short queries ("a", "on") could match many fact keys via `.Contains`. Add documentation suggesting `confidenceThreshold ≥ 0.3` for noisy WorldFact queries | P3 — Sprint 18 AGENTS.md update |
| D3 | emerald_ore / deepslate_emerald_ore addition was an unannounced bonus fix; record it in phase6-tasks.md Sprint 17 row | P3 — docs |
| D4 | No integration test for the `/api/agent/resolve` HTTP endpoint (carried from Sprint 16 D3) | P3 — Sprint 18+ |
| D5 | `using System.Collections.Generic` in IKnowledgeResolver.cs is redundant given ImplicitUsings; harmless but could be removed | P4 |
| D6 | `CandidateType.WorldFact` doc says "Confidence decays with age: 0.70 within 60 s, 0.50 after" — could link to the constants in LocalKnowledgeResolver once they're accessible from outside the class | P4 |

**Acceptance criteria — all met:**

| # | Criterion | Status |
|---|-----------|--------|
| AC1 | `CommonMinecraftBlocks.DirectMineBlocks` contains "diamond" | CONFIRMED |
| AC2 | `ClassifySpec` checks DirectMineBlocks first (before SourceBlocks self-source check) | CONFIRMED |
| AC3 | `ClassifySpec("diamond", sources=["diamond_ore"])` returns `DirectMineable` | CONFIRMED — via test AC5 |
| AC4 | `ClassifySpec("oak_planks", sources=["oak_log"])` still returns `Craftable` | CONFIRMED — no regression |
| AC5 | `ClassifySpec_Diamond_ReturnsDirectMineable` test passes | CONFIRMED — logic verified |
| AC6 | `ClassifySpec_OakLog_ReturnsDirectMineable` test passes (regression guard) | CONFIRMED |
| AC7 | `CandidateType.WorldFact` added to `IKnowledgeResolver.cs` enum | CONFIRMED |
| AC8 | `LocalKnowledgeResolver` accepts optional `Func<WorldState?>? worldStateAccessor` | CONFIRMED |
| AC9 | WorldFact candidates appear in results when accessor returns matching StructuredFacts | CONFIRMED — via test AC10 |
| AC10 | `WorldFact_ReturnsOnQueryMatch` — recent fact → 0.70 confidence | CONFIRMED — logic verified |
| AC11 | `WorldFact_LowConfidenceForOldFact` — stale fact → 0.50 confidence | CONFIRMED — logic verified |
| AC12 | Program.cs DI registration passes `() => sp.GetService<AgentBackgroundService>()?.WorldState` | CONFIRMED |
| AC13 | D2 SearchAsync raw-query comment added in LocalKnowledgeResolver | CONFIRMED |
| AC14 | D3 /api/agent/resolve curl examples added to AGENTS.md | CONFIRMED |

**Council decision: APPROVED — no blockers. Pending CI green confirmation.**

---

## Anonymous Peer Review

**Reviewer: Anonymous (external)**  
**Confidence in overall direction: 0.93**

**Scope discipline:** Sprint 17 delivered exactly what Sprint 16's council D1/D4 requested — nothing more. ClassifySpec fix is surgical (one OR condition added + five items to a HashSet). WorldFact source follows the established two-source pattern from Sprint 16. No new .csproj files, no new interfaces beyond the enum value. ✓

**What I would add:** The `WorldFact` confidence values (0.70 / 0.50) are reasonable starting points but were chosen somewhat arbitrarily. For a production-quality resolver, these should be exposed as configurable options (e.g., in `RestMemoryGatewayOptions` or a new `KnowledgeResolverOptions` class), not buried as private constants. This is a Phase 7-C concern.

**What I would caution against:** The Contains-based key matching for WorldFacts is broad. In production, fact keys are typically namespaced (`"inventory:oak_log"`, `"position:x"`, `"health"`). A query for "oak_log" will match "inventory:oak_log" (correct) but also potentially "inventory:oak_log_plank" (if such a fact existed). For the current bounded fact set this is fine, but the matching strategy should be revisited when observation pipeline normalization (Phase 7-C) standardizes fact key formats.

**What I would commend:** The factory-delegate approach for `WorldState` access is the right solution to the circular-dependency / singleton-ordering problem. Passing `Func<WorldState?>` instead of `WorldState` means the resolver always gets a fresh snapshot at query time, not a stale construction-time reference. This is architecturally sound.

**Overall rating: APPROVE. Sprint 17 is clean. Phase 7-C (observation pipeline normalization) is the natural next step.**
