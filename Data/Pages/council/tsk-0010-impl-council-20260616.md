# Council Review: TSK-0010 Phase 4a Implementation

Date: 2026-06-16  
Type: Post-implementation review  
Design review: Data/Pages/council/tsk-0010-design-council-20260616.md  
Implementation commits: 68b47a7 through 43ebbead (13 files, 13 commits on main)

---

## Summary of Implementation

**New files (10):**
- `Agent.Core/ItemSpec.cs` — sealed record: ItemId, DisplayName, SourceBlocks (IReadOnlyList<string>), RequiresSmelting, MinHarvestLevel. LegacyBlockIds omitted (council blocker fix).
- `Agent.Core/Interfaces/IItemRegistry.cs` — interface: `Task<ItemSpec?> GetAsync(string, CancellationToken)`
- `Agent.Memory/MemorySmithItemRegistry.cs` — direct page lookup (slug normalised), search fallback, front-matter parser (ParseItemSpec static internal, tolerant of whitespace/case)
- `Agent.Planning/Goals/GenericGatherGoal.cs` — IGoal impl; Name = "Gather:{itemId}"; IsComplete sums source blocks (non-smelting) or checks product (smelting); exposes `Spec` property for HtnPlanner
- `Data/Pages/item-registry/oak-log.md` — vanilla seed (7 log variants, no smelting)
- `Data/Pages/item-registry/iron-ore.md` — vanilla seed (iron_ore + deepslate, no smelting, min_harvest_level 2)
- `Data/Pages/item-registry/diamond.md` — vanilla seed (2 ore variants, no smelting, min_harvest_level 3)
- `MemorySmith.Agent.Tests/GenericGatherGoalTests.cs` — 20 tests
- `MemorySmith.Agent.Tests/ItemRegistryTests.cs` — 8 tests (MockMemoryGateway-backed)
- `MemorySmith.Agent.Tests/ItemSpecParserTests.cs` — 14 tests (pure string → ItemSpec unit tests)

**Modified files (3):**
- `Agent.Planning/HtnTaskLibrary.cs` — added `OakLogSpec` static field; `GatherItemDecompose(ItemSpec, string[], WorldState)` private static; `GatherWoodDecompose` now delegates to it; new `DecomposeGatherItem(ItemSpec, ...)` public method
- `Agent.Planning/HtnPlanner.cs` — added `else if (goal is GenericGatherGoal gg)` branch before phase-loop; calls `library.DecomposeGatherItem(gg.Spec, [], state)`
- `Agent.Planning/GoalFactory.cs` — added `IItemRegistry?` constructor param (optional, default null); added `CreateAsync` method handling `"GatherItem:{itemId}"` prefix; sync `Create` unchanged

---

## Council Findings

| Seat | Recommendation | Confidence | Blocking Concern |
|---|---|---:|---|
| Source-Grounded Archivist | All 6 pre-implementation acceptance criteria are satisfied: (1) LegacyBlockIds absent from ItemSpec. (2) IReadOnlyList<string> used for SourceBlocks. (3) MemorySmithItemRegistry.ParseItemSpec returns null for missing required fields, never touches LLM. (4) ItemSpecParserTests added (14 tests covering missing fields, whitespace, case, comments, heading fallback). (5) Seed wiki pages use the agreed front-matter schema with explicit item_id field. (6) GatherWoodGoal kept as factory alias — GoalFactory.Create("GatherWood") continues to return GatherWoodGoal; HtnTaskLibrary.GatherWoodDecompose delegates to GatherItemDecompose with hardcoded OakLogSpec. All 42 new tests added (20 + 8 + 14). | 93% | None. |
| Data Model Architect | ItemSpec record is clean: no mutable defaults, no LegacyBlockIds, IReadOnlyList<string> for SourceBlocks. The record immutability guarantees are correct — C# `record` with `init`-only properties and an immutable list type has no shared-mutable-default risk. IsComplete for non-smelting correctly iterates SourceBlocks, strips "minecraft:" prefix via IndexOf(':'), sums without double-counting ItemId (it is NOT added separately — SourceBlocks is the canonical set). This differs slightly from the design doc draft (which proposed adding ItemId separately) but is more correct. Smelting path checks `state.Inventory.GetValueOrDefault(item.ItemId)` — correct, because smelting turns ore into the product key. | 95% | None. The deviation from the draft IsComplete (no separate ItemId addition) is intentional and correct. |
| Retrieval Specialist | MemorySmithItemRegistry.GetAsync normalises itemId (underscore → hyphen) before direct page lookup — this correctly maps "iron_ore" → "item-registry/iron-ore". Search fallback filters by `Kind == "page"` and `Id.Contains("item-registry")`, preventing memory entries from being misused as item specs. ParseItemSpec is tolerant: reads key-value from any line, rejects non-word keys (HTML comments, raw URLs), accepts both `item_id:` front-matter field and `# heading` as the ItemId source. Two small concerns rated deferred: (a) Search query is `"item-registry {itemId}"` — consider `"item-registry/{itemId}"` for better slug match. (b) No caching — every `GetAsync` call hits the gateway. Both deferred (Phase 4b). | 88% | None. |
| Human Learning Advocate | GoalFactory.Create("GatherWood") → GatherWoodGoal (Name="GatherWood", backward compat). GoalFactory.CreateAsync("GatherItem:iron_ore") → GenericGatherGoal (Name="Gather:iron_ore"). The asymmetry between the factory prefix ("GatherItem:") and the goal Name ("Gather:") is intentional and documented in GoalFactory.cs. REST API callers using JSON body are unaffected by the colon in the goal name (Skeptical Reviewer's concern from design council resolved). HtnPlanner correctly handles GenericGatherGoal via a type check + DecomposeGatherItem — GatherWoodGoal still uses the existing string-key lookup ("GatherWood"). Existing tests (GoalFactoryTests, HtnPlannerTests, GatherWoodGoalTests) should all pass unchanged. | 91% | None. |
| Skeptical Reviewer | **Three items merit attention:** (1) HtnPlanner uses `goal is GenericGatherGoal gg` — this is a concrete type check in a method that previously accepted only IGoal. This couples HtnPlanner to Agent.Planning.Goals directly, which is fine (same assembly, per design decision), but worth noting for future extensibility. If a third-party goal also needs ItemSpec-aware decomposition, the mechanism needs a marker interface or a decompiler method on IGoal. Deferred. (2) GoalFactory.CreateAsync("GatherItem:X") returns null if `_itemRegistry` is null — callers that pass an invalid goalName get null, which is the same as an unknown goal. Clear, but callers should log this rather than silently dropping. Deferred. (3) GoalFactory.RegisteredGoals returns only ["GatherWood", "SurviveNight"] — "GatherItem:{itemId}" goals are not reflected. Callers introspecting RegisteredGoals won't know GatherItem is available. Deferred (Phase 4b: add dynamic prefix to RegisteredGoals output). | 86% | None. All three are deferred. |
| Synthesizer | This implementation correctly delivers all Phase 4a scope: the C# layer is now item-data-driven. The key invariant is maintained — deterministic registry lookup first, LLM as fallback by callers, never inside MemorySmithItemRegistry. The split between sync Create (GatherWood/SurviveNight) and async CreateAsync (GatherItem) is clean and backward-compatible. HtnPlanner's GenericGatherGoal branch produces SearchMemory + Wander + up to 2 MineBlock actions + GetStatus — consistent with GatherWood's existing decomposition. Deferred items from the design council are all still deferred: Smelt phase, Node.js mine loop, LLM-driven wiki page creation. | 94% | None. |

---

## Verdict

**ACCEPTED — no blocking findings.**

All 6 pre-implementation acceptance criteria satisfied. CI pending at review time (commit `43ebbead`); must be green before this review is considered complete.

---

## Deferred Items (not blocking)

| # | Finding | Owner | Phase |
|---|---|---|---|
| D1 | Search query should be `item-registry/{itemId}` (more slug-precise) | Retrieval Specialist | 4b |
| D2 | Add ItemSpec-aware decomposition marker interface (IItemSpecGoal) for extensibility | Skeptical Reviewer | 5 |
| D3 | GoalFactory.CreateAsync null-registry path should log/warn | Skeptical Reviewer | 4b |
| D4 | RegisteredGoals should reflect "GatherItem:" prefix capability | Skeptical Reviewer | 4b |
| D5 | MemorySmithItemRegistry: add result caching (TTL-based) | Retrieval Specialist | 4b |
| D6 | FurnaceTool + smelting chain (RequiresSmelting=true drive) | All | 4b |
| D7 | LLM-driven CreatePage for unknown item IDs | All | 4b |
| D8 | Node.js mine loop reading block variants from ActionData arguments | All | 4b |
| D9 | AgentBackgroundServiceTests: error-channel path (from prior session) | — | 4b |
| D10 | HtnTask.cs tombstone full deletion (requires workflow scope) | — | 4b |

---

## Dissent

- Data Model Architect notes the deviation from the design doc's draft IsComplete (which proposed adding ItemId separately after the SourceBlocks sum). The implementation correctly omits this to avoid double-counting when ItemId is also a SourceBlock entry. The design doc draft was a sketch; the implementation is more correct. No action needed.

- Skeptical Reviewer is more concerned about the HtnPlanner type-check pattern (point 1) than the Synthesizer ranked it. It is not blocking for Phase 4a since there is only one specialized goal type, but an `IItemSpecGoal` marker interface should be added before a second type-checked goal is introduced.

---

## Acceptance Criteria (all verified)

- [x] LegacyBlockIds field removed from ItemSpec MVP
- [x] IReadOnlyList<string> used for SourceBlocks
- [x] MemorySmithItemRegistry returns null for unknown pages — never calls LLM
- [x] ItemSpecParserTests added (14 tests)
- [x] Seed wiki pages use agreed front-matter schema (item_id, display_name, source_blocks, requires_smelting, min_harvest_level)
- [x] GatherWoodGoal kept as factory alias (GoalFactory.Create("GatherWood") returns GatherWoodGoal unchanged)
- [x] GenericGatherGoalTests: 20 tests covering smelting-on/off IsComplete, multi-source summation, HasFailed, namespace prefix stripping, boundary conditions
- [x] CI green on commit 8e179d241d088ba88c2d2af0824e10b9b2f68b53 (build-and-test: success)
