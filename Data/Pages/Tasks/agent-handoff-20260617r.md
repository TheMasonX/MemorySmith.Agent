# MemorySmith.Agent Handoff — Sprint 17
**Date:** 2026-06-17  
**Branch:** `sprint-5-tool-safety` → PR #1 (merge still deferred)  
**Head commit:** `e5648817` (Sprint 16 council review)  
**CI status:** ✅ Green (build-and-test: success on 02b9ba71; dead-code fix + council push are doc-only)

---

## Current state in one paragraph

Leo is a Minecraft bot (C# + Node.js) with a deterministic HTN planner, LLM fallback chat, and a MemorySmith wiki as long-term memory. Sprints 1–16 are complete. Sprint 16 delivered the Phase 7-A architecture inventory (`planner-routing-status-20260617.md`), documented the PlannerRouter's two implemented paths vs. two aspirational stubs, introduced `IKnowledgeResolver` as the Phase 7-B knowledge entry point, implemented `LocalKnowledgeResolver` (two sources: IItemRegistry + IMemoryGateway), wired it to `GET /api/agent/resolve`, and added 8 unit tests. The crafting-table bootstrap was also extracted into a named helper for readability. Sprint 17 begins Phase 7-B growth: fix the `ClassifySpec` heuristic and add a third knowledge source (WorldFact).

---

## What changed in Sprint 16 (this session)

**P0 — PlannerRouter annotation**
- `PlannerRouter.cs`: all `PlannerStrategy` enum values annotated with `[IMPLEMENTED]` or `[ASPIRATIONAL]`; `Select()` method XML-documented with the two live paths
- `Data/Pages/Architecture/planner-routing-status-20260617.md`: full inventory of what is/isn't implemented

**P1 — IKnowledgeResolver stub**
- `Agent.Memory/IKnowledgeResolver.cs`: interface + `KnowledgeQuery`, `KnowledgeResult`, `KnowledgeCandidate`, `CandidateType` (5 types, 1 interface in one file)
- `Agent.Memory/LocalKnowledgeResolver.cs`: two sources — IItemRegistry (0.95 confidence exact-match) then IMemoryGateway.SearchAsync (0.60 × score); confidence threshold + TopN cap; WasAmbiguous flag when top-2 scores within 0.05
- `WebUI.Blazor/Program.cs`: `IKnowledgeResolver` → `LocalKnowledgeResolver` registered as singleton; `GET /api/agent/resolve?q=&types=&topN=&confidenceThreshold=` endpoint
- `MemorySmith.Agent.Tests/KnowledgeResolverTests.cs`: 8 tests (registry hit, smeltable, craftable, gateway fallback, TopN cap, threshold filter, type filter, ambiguity, empty)

**P2 — Crafting-table bootstrap refactor**
- `Agent.Planning/HtnTaskLibrary.cs`: `AddCraftingTableIfNeeded` private helper extracted from `DecomposeCraftItem`; behavior identical, readability improved

**Fix — Dead-code removal**
- `LocalKnowledgeResolver.cs`: removed display-name fallback whose condition (`queryAsId != normalizedId`) was always false (both sides computed with the same normalization formula)

**Council review:** `Data/Pages/council/sprint16-council-20260617.md` — no blockers, approved

---

## Suggested skills

- **GitHub MCP** — all code at `TheMasonX/MemorySmith.Agent`, branch `sprint-5-tool-safety`. Always fetch blob SHA before updating existing files. Use `paramsFile`, never inline.
- **CI check** — `curl -s "https://api.github.com/repos/TheMasonX/MemorySmith.Agent/commits/<sha>/check-runs"` + annotations for failures.
- **Council review pattern** — 6-seat review to `Data/Pages/council/` after each sprint.

---

## Sprint 17 starting point (P0 first)

### P0 — Fix ClassifySpec heuristic (council D1)

**Why:** `LocalKnowledgeResolver.ClassifySpec` uses `SourceBlocks.Contains(ItemId)` to detect DirectMineable items. For items where the drop differs from the block name (diamond dropped by diamond_ore, coal dropped by coal_ore), the heuristic returns `Craftable` instead of `DirectMineable`. The fix adds a `CommonMinecraftBlocks.DirectMineBlocks` check as the primary signal.

**Tasks:**
1. Open `Agent.Memory/LocalKnowledgeResolver.cs` → `ClassifySpec` method
2. Change the `DirectMineable` branch to:
   ```csharp
   if (CommonMinecraftBlocks.DirectMineBlocks.Contains(spec.ItemId, StringComparer.OrdinalIgnoreCase)
       || spec.SourceBlocks.Contains(spec.ItemId, StringComparer.OrdinalIgnoreCase))
       return CandidateType.DirectMineable;
   ```
3. Add 2 tests to `KnowledgeResolverTests.cs`:
   - `ClassifySpec_Diamond_ReturnsDirectMineable` (diamond is in DirectMineBlocks)
   - `ClassifySpec_OakLog_ReturnsDirectMineable` (oak_log is in SourceBlocks AND DirectMineBlocks)

**Files:** `Agent.Memory/LocalKnowledgeResolver.cs` · `MemorySmith.Agent.Tests/KnowledgeResolverTests.cs`

**Note:** `Agent.Memory` already imports `Agent.Core` (where `CommonMinecraftBlocks` lives). No project reference changes needed.

### P1 — WorldFact as a third source

**Why:** Phase 7-B goal is "Unified resolver growth." WorldState.Facts holds runtime observations (bot position, inventory, recently-seen blocks) that should be searchable via the resolver.

**Tasks:**
1. Add `IWorldState` or `WorldState` as a constructor parameter to `LocalKnowledgeResolver` (or a factory delegate if immutable access is needed)
2. Add a third retrieval step after gateway search: scan `WorldState.Facts` for keys matching the query prefix; emit `CandidateType.WorldFact` candidates
3. Confidence for WorldFact: `0.70f` for recent facts (within last 60s), `0.50f` for older
4. Add 2 tests: `WorldFact_ReturnsOnQueryMatch` and `WorldFact_LowConfidenceForOldFact`

**Constraint:** Keep the resolver non-blocking and synchronous for WorldFact (facts are in-memory, no I/O). WorldFact candidates have no wiki backing — their Detail field should be the raw fact value.

**Files:** `Agent.Memory/LocalKnowledgeResolver.cs` · `MemorySmith.Agent.Tests/KnowledgeResolverTests.cs`

### P2 — Documentation + comment cleanup (council D2/D3)

| ID | Task | File |
|----|------|------|
| D2 | Add inline comment in `LocalKnowledgeResolver.ResolveAsync` explaining that `SearchAsync` receives the raw un-normalized query (intentional: wiki search is semantic, not lexical) | `LocalKnowledgeResolver.cs` |
| D3 | Add note in AGENTS.md "Testing the /api/agent/resolve endpoint" with a curl example | `AGENTS.md` |

### P2 carries from earlier sprints

| ID | Task | File |
|----|------|------|
| B3 | Orientation-aware PlaceBlock (facing direction) | `HtnTaskLibrary.cs`, `index.js` |
| B5 | Clear-area action before building on slight slope | `index.js` + new tool |
| D2 (S2) | MemorySmithItemRegistry parallel miss race | `Agent.Memory/` |

---

## Phase 7 roadmap (current state)

| Sub-phase | Focus | Sprint estimate |
|-----------|-------|----------------|
| **7-A (done)** | Architecture inventory; planner routing cleanup | Sprint 16 ✅ |
| **7-B (now)** | Resolver growth: ClassifySpec fix + WorldFact source | Sprint 17 |
| 7-C | Observation pipeline normalization | Sprint 18 |
| 7-D | Belief layer + IBeliefState | Sprint 19 |
| 7-E | Planner input migration to world model + beliefs | Sprint 21 |
| 7-F | Reflection service | Sprint 22 |
| 7-G | Page synthesis from memory clusters | Sprint 23 |

---

## Key rules (non-negotiable)

All in `AGENTS.md` at repo root. Critical ones:
1. **Warnings = errors** (`Directory.Build.props`). Fix before pushing.
2. **paramsFile, never inline content** when pushing to GitHub MCP.
3. **CI must be green before council review.**
4. **Enqueue chat response AFTER the switch** in `HandleChatEventAsync`.
5. **ActionQueue is ConcurrentQueue** — don't revert to `Queue<T>`.
6. **GoalNamesMatch compares by suffix** — "GatherItem:X" matches "Gather:X".

---

## Files to read on arrival

- `AGENTS.md` — all rules, patterns, anti-patterns (5 min read)
- `Data/Pages/Tasks/phase6-tasks.md` — sprint tracker (Sprints 1–16)
- `Data/Pages/council/sprint16-council-20260617.md` — latest council; see deferred items D1–D5
- `Agent.Memory/LocalKnowledgeResolver.cs` — start here for P0 ClassifySpec fix
- `Agent.Memory/IKnowledgeResolver.cs` — understand the types before touching the impl
- `MemorySmith.Agent.Tests/KnowledgeResolverTests.cs` — reference for adding P0 tests
