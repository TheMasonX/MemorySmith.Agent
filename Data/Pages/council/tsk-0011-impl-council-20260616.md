# Council Review — TSK-0011 Phase 4b
**Topic:** Blueprint Construction System + Quick Fixes D1/D2/D3  
**Date:** 2026-06-16  
**File:** Data/Pages/council/tsk-0011-impl-council-20260616.md  
**CI status at review time:** Pending (last green: 8e179d241d on Phase 4a code commit)

---

## Seat 1 — Source-Grounded Archivist

**Confidence:** 0.87 | **Vote:** APPROVE

**Review:**
The implementation faithfully mirrors the Phase 4a ItemSpec/IItemRegistry pattern:

- `BlueprintParser` mirrors `MemorySmithItemRegistry.ParseItemSpec`: static parser, frontmatter extraction, symbol-map grid parsing.
- `MemorySmithBlueprintRepository` mirrors `MemorySmithItemRegistry`: direct slug lookup, search fallback, never calls LLM (D-003 compliant).
- `BuildGoal` mirrors `GenericGatherGoal`: facts-based IsComplete/HasFailed, Name = "Build:{id}", phases array.
- `GoalFactory` mirrors the GatherItem: prefix pattern exactly for the new Build: prefix.

**Observation (non-blocking):** The `IBlueprintExecutor` interface is in `Agent.Construction/Interfaces/` but `BlueprintExecutor` is instantiated directly as `new BlueprintExecutor()` inside `HtnTaskLibrary.DecomposeBuild` rather than being injected. This means the executor cannot be mocked in unit tests via the interface. Acceptable for Phase 4b; defer to Phase 5.

**ADR compliance:**
- D-002 ✓ (MemorySmith is the memory backend)
- D-003 ✓ (deterministic-first; no LLM calls in any new path)
- D-006 ✓ (blueprints are MemorySmith wiki pages)
- D-007 ✓ (solution format unchanged)
- D-008 ✓ (Node.js Mineflayer untouched)
- D-010 ✓ (ActionProtocol wire names unchanged; "PlaceBlock" matches PlaceBlockTool.Name)

---

## Seat 2 — Data Model Architect

**Confidence:** 0.90 | **Vote:** APPROVE

**Review:**
The data model additions are well-scoped and minimal:

1. `PlacementBlock(int X, int Y, int Z, string BlockId)` — immutable record, no excess state. ✓
2. `Blueprint` record unchanged; `RawMarkdown` field preserved so GoalFactory can re-parse blocks without a second round-trip to MemorySmith. ✓
3. `IItemSpecGoal` marker interface correctly placed in `Agent.Core` as a shared boundary type. ✓
4. `BuildGoal` holds both `Blueprint` (metadata) and `IReadOnlyList<PlacementBlock>` (execution data). Appropriate — goal carries all context needed for planning without further async calls. ✓

**Project reference graph is acyclic (verified):**
```
Agent.Core → (none)
Agent.Construction → Agent.Core
Agent.Memory → Agent.Core, Agent.Construction   (new ref added)
Agent.Planning → Agent.Core, Agent.Construction  (new ref added)
Agent.Tests → Agent.Core, Agent.Construction (new), Agent.Planning, Agent.Tools, WebUI.Blazor
```
No circular dependencies. ✓

---

## Seat 3 — Retrieval Specialist

**Confidence:** 0.84 | **Vote:** APPROVE

**Review:**
The wiki retrieval paths for blueprints are correctly implemented:

- Page slug normalization: `blueprintId.Replace('_', '-').ToLowerInvariant()` — consistent with item registry. ✓
- Direct lookup at `blueprints/{slug}` with search fallback mirrors item registry exactly. ✓
- `RawMarkdown` preserved in returned `Blueprint` so GoalFactory can re-parse blocks inline (no second network hop). ✓

**BlueprintParser correctness:**
- `IsValidGridRow` rejects prose lines by requiring every character to be in the legend. ✓
- `ExtractYLevel` handles "### Y=0 (Floor)" and "### Y=2 (Mid walls)" etc. ✓
- Legend override merges with `DefaultLegend`; page can override individual entries. ✓
- `F` (bed foot) and door upper-half both map to null → skipped in block list → not double-placed. ✓

**Deferred concern (non-blocking):** The search fallback uses `r.PageId.Contains("blueprints")` (substring). Should be `r.PageId.StartsWith("blueprints/")` to avoid false matches on hypothetical pages like "my-blueprints-ideas/...". Phase 5 cleanup.

---

## Seat 4 — Human Learning Advocate

**Confidence:** 0.90 | **Vote:** APPROVE

**Review:**
The knowledge assets are production-quality:

- `small-house.md` blueprint: layer grids are visually scannable, legend is self-documenting, build notes explain coordinate conventions and placement quirks, materials table distinguishes directly-mineable vs crafted items, Phase 4b limitations explicitly noted. ✓
- 9 new item registry pages: follow the established format, include crafting recipes and Phase 4b limitation notes where relevant. ✓
- D1: `RegisteredGoals` now returns `["GatherWood", "SurviveNight", "GatherItem:{itemId}", "Build:{blueprintId}"]` — verified non-breaking against existing test `RegisteredGoals_ContainsAllKnownGoals` which uses `GreaterThanOrEqualTo(2)`. ✓

**Deferred (documented in phase4b-tasks.md):** Door and bed facing direction not enforced in PlaceBlock args. Bot facing at placement time determines result. Known gap; CraftItemTool is Phase 5.

---

## Seat 5 — Skeptical Reviewer

**Confidence:** 0.83 | **Vote:** APPROVE

**Pre-review concern resolved:** Initial concern that `GoalFactoryTests.RegisteredGoals_ContainsAllKnownGoals` might assert `Count == 2` — confirmed it uses `Has.Count.GreaterThanOrEqualTo(2)`. D1 change is non-breaking. ✓

**Remaining concerns (all non-blocking):**

1. **Large plan size:** A 9×5×7 house produces ~330+ PlaceBlock actions in a single plan. No resume capability if Mineflayer disconnects mid-build. Chunked-build support is Phase 5 (P5-07 in tasks).

2. **DirectMineBlocks set:** Does not include exotic stone variants (granite, diorite, tuff, calcite) that can drop cobblestone-adjacent blocks. Under-gather in edge cases. Acceptable for Phase 4b.

3. **Build origin default:** If no origin facts are set, house builds at world origin (0,0,0). A `FindBuildSiteTool` or REST endpoint for setting origin facts is needed before live use. Documented in phase4b-tasks.md as known gap.

4. **BlueprintParser grid row validation:** A line with all-legend chars but unusual length (e.g., a row of 1 char) is accepted as valid. No protection against accidentally malformed grids where row widths differ. Acceptable for Phase 4b.

---

## Seat 6 — Synthesizer

**Confidence:** 0.88 | **Vote:** APPROVE — NO BLOCKING FINDINGS

**Summary:**
Phase 4b delivers a complete, well-structured blueprint construction system. The implementation adds 8 new files and modifies 7 existing ones with clean ADR compliance and consistent extension of the Phase 4a patterns.

**No blocking findings.** All concerns raised by Seats 1–5 are deferred to Phase 5 and documented in `phase4b-tasks.md`.

**Acceptance criteria — all met:**
- [x] IItemSpecGoal marker interface in Agent.Core (D2)
- [x] GenericGatherGoal implements IItemSpecGoal (D2)
- [x] HtnPlanner dispatches on `goal is IItemSpecGoal` interface check (D2)
- [x] GoalFactory.RegisteredGoals exposes GatherItem + Build prefixes (D1)
- [x] GoalFactory.CreateAsync warns when repository is null (D3)
- [x] PlacementBlock record added to BlueprintSchema
- [x] BlueprintParser: frontmatter + layer grids + legend override
- [x] IBlueprintExecutor interface + BlueprintExecutor implementation
- [x] BuildGoal: name, phases, IsComplete, HasFailed
- [x] GoalFactory: Build: prefix with IBlueprintRepository support
- [x] HtnPlanner: BuildGoal branch with origin fact reading
- [x] HtnTaskLibrary: DecomposeBuild with material gather + PlaceBlock
- [x] MemorySmithBlueprintRepository: wiki-backed IBlueprintRepository
- [x] Agent.Planning + Agent.Memory + Agent.Tests csproj updated
- [x] small-house.md blueprint (9×5×7: floor, walls, door, windows, torches, crafting table, double chest, bed, roof)
- [x] 9 item registry pages (cobblestone, oak-planks, glass-pane, torch, crafting-table, chest, oak-slab, oak-door, red-bed)
- [x] 60 new tests across 4 test files
- [ ] CI green (pending — expect all green based on analysis)

**Decision:** PROCEED to CI confirmation and handoff.