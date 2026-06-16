# Design Council: TSK-0010 — GenericGatherGoal + ItemRegistry

Date: 2026-06-16  
Type: Pre-implementation design review  
Design doc: Data/Pages/Tasks/tsk-0010-generic-gather-goal-design.md

---

## Decision

**Accept the design with modifications.** Three open questions resolved. One blocking refinement required before implementation begins.

---

## Evidence Reviewed

- TSK-0010 design document (GenericGatherGoal, ItemSpec, IItemRegistry, MemorySmithItemRegistry)
- Existing `GatherWoodGoal.cs`, `HtnTaskLibrary.cs`, `GoalFactory.cs`
- D-002 (MemorySmith as memory backend), D-003 (deterministic-first planning)
- Phase 3 council: SearchMemory result not yet feeding into MoveTo (deferred to Phase 4)

---

## Findings

| Seat | Recommendation | Confidence | Blocking Concern |
|---|---|---:|---|
| Source-Grounded Archivist | The design correctly identifies all current hardcoding sites. `GatherWoodGoal.IsComplete` (sums `*_log`), `HtnTaskLibrary.GatherWoodDecompose` (hardcoded `"minecraft:oak_log"`, `"minecraft:birch_log"`), and Node.js mine loop (hardcoded block search) — three separate sites that need updating. The proposal addresses the C# side completely; the Node.js mine loop is flagged as deferred (Phase 4b). The wiki-page front-matter format for ItemSpec is parseable with a 10-line parser — no LLM needed for vanilla items. | 91% | None. |
| Data Model Architect | **ItemSpec placement**: `Agent.Core` is correct — `Agent.Core` already defines `WorldState`, `ActionData`, and all domain primitives. ItemSpec belongs there. `IItemRegistry` belongs in `Agent.Core`; implementation in `Agent.Memory`. **`IsComplete` correctness**: The multi-source summation is correct for logs but has a subtle edge for smelting items. `iron_ore` and `iron_ingot` are different inventory keys. When `RequiresSmelting = true`, `IsComplete` checks `iron_ingot` count. When the bot mines `iron_ore`, `blockMined` events add `iron_ore` to inventory (not `iron_ingot`). So `IsComplete` returns false even after mining, as expected — it only returns true after smelting. This is intentional and correct. **`SourceBlocks` list**: Should this be `IReadOnlyList<string>` or `string[]`? Use `IReadOnlyList<string>` on the record interface; the front-matter parser returns an array. | 93% | **Blocker**: The `ItemSpec` record has `LegacyBlockIds` as `IReadOnlyDictionary<string, string>` with a default init. But `record` types with `IReadOnlyDictionary` defaults need careful handling — the default value is shared across all instances if it's a static dict. Recommend: use `IReadOnlyDictionary<string, string>? LegacyBlockIds { get; init; } = null` and handle null at call site, or make it a non-default property. This is a correctness issue for future mod version support. |
| Retrieval Specialist | The MemorySmith wiki page format needs a definitive schema before implementation. Proposing a minimal YAML-like front-matter block embedded in the wiki page markdown body: `source_blocks: oak_log, birch_log` (comma-separated). The `MemorySmithItemRegistry.ParseItemSpec` parser should be tolerant of extra whitespace and case-insensitive field names. **Critical question answered**: should `SearchAsync` use the exact slug `item-registry/oak-log` or a semantic query? Answer: use slug-based lookup as primary (exact match on page title/slug), semantic search as fallback. This is deterministic and fast (D-003). The slug convention is `item-registry/{itemId}` where `itemId` is the short form (no `minecraft:` prefix). | 89% | None. |
| Human Learning Advocate | The `GoalFactory` integration for `"GatherItem:{itemId}"` is correct. The existing `"GatherWood"` registration should be kept as an alias pointing to `"GatherItem:oak_log"` so that all existing API calls and tests continue to work without change. New REST API callers can use `"GatherItem:iron_ore"`. The seed wiki pages (`item-registry/oak-log`, `item-registry/iron-ore`, `item-registry/diamond`) should include a human-readable comment explaining the front-matter format so the user can add modded items by example. | 90% | None. |
| Skeptical Reviewer | **Open question 3 resolved**: HtnTaskLibrary should receive `ItemSpec` directly (not deconstructed into `blockIds: string[]`). `Agent.Planning` already imports `Agent.Core`; adding `ItemSpec` is not a new coupling. Decoupling it would add indirection with no benefit. **Open question 2 resolved**: Front-matter parsing for vanilla; LLM fallback for mod pages. The fallback LLM path must be clearly gated behind a `null` return from `ParseItemSpec` — the fallback should be invoked by the caller (`GoalFactory`), not inside `MemorySmithItemRegistry`. This keeps the registry pure (no LLM dependency). **`GatherItemDecompose` task limit**: `Take(2)` on source blocks is a reasonable default. But if an item has only one source block (e.g. `diamond`), a single `MineBlock` action is generated. If it has 3+ (cobalt ore, cobalt deeplayer ore, cobalt vein ore), only the first 2 are tried per plan cycle. This is acceptable. | 88% | **Deferred (not blocking)**: `MemorySmithItemRegistry.ParseItemSpec` has no test coverage in the current design. Add `ItemSpecParserTests` (unit tests against raw markdown strings) before or alongside `ItemRegistryTests`. Parser correctness is critical — a bad parse silently produces a wrong ItemSpec. |
| Synthesizer | This is the correct abstraction boundary for Phase 4. The key insight: item acquisition is now a data-driven problem (what wiki page says about the item) rather than a code problem (what the developer hardcoded). The LLM is correctly positioned as a fallback for unknown items, not the primary path. The phased plan (Phase 4a: C# side only; Phase 4b: Node.js mine loop and smelting) is appropriate given Node.js changes require a running Minecraft server to test. **One open question for council not yet resolved**: should `GenericGatherGoal.Phases` be `["FindSource", "Mine", "Collect"]` or `["FindSource", "Mine", "Smelt", "Collect"]` for smelting items? Recommend: always use the 3-phase version for now; `Smelt` is added when `FurnaceTool` is implemented. The phases are informational for the planner's fallback path; for direct-decomposition goals (which `GenericGatherGoal` will be), they're documentation only. | 92% | None. |

---

## Resolutions to Open Questions

| # | Question | Resolution |
|---|---|---|
| 1 | ItemSpec in which project? | **`Agent.Core`** — domain primitive, no dependencies. |
| 2 | Wiki page parsing strategy? | **Front-matter for all items**; LLM extraction (via GoalFactory caller) for pages that don't match the schema. `MemorySmithItemRegistry` returns null for unrecognizable pages — it never calls the LLM itself. |
| 3 | HtnTaskLibrary + ItemSpec coupling? | **Pass ItemSpec directly** — coupling already exists (Agent.Planning imports Agent.Core). |
| 4 | IsComplete for multi-source items? | **Sum all source blocks** — "10 logs" means any log variant counts. Correct. |
| 5 | Mod wiki page authoring? | **Phase 4a: user-authored via MemorySmith UI**. Phase 4b: LLM-driven `CreatePage` for unknown items. |

---

## Synthesis

**Design accepted with one blocking refinement:**

1. **BLOCKER (fix before coding):** Change `LegacyBlockIds` default from `new Dictionary<string, string>()` to `null` (nullable), or remove the field entirely from the MVP (it's a pre-1.13 concern). The shared-mutable-default risk in record types is real. For the MVP, omit `LegacyBlockIds` — a `MinecraftVersion` config field (planned, see D-004 background) will handle this in Phase 5.

**Deferred (not blocking):**
2. Add `ItemSpecParserTests` covering: missing fields, malformed values, extra whitespace, case-insensitive field names.
3. `GenericGatherGoal.Phases` stays `["FindSource", "Mine", "Collect"]`; `"Smelt"` added in Phase 4b.

---

## Dissent

- Data Model Architect is more concerned about the `LegacyBlockIds` default than the Synthesizer ranked it. It is a blocking concern — record initializers with shared mutable defaults are a C# footgun. The MVP should omit the field rather than ship a subtle bug.
- Skeptical Reviewer notes that `"GatherItem:oak_log"` as a goal name string (`"Gather:oak_log"`) uses a colon — the REST API client must URL-encode this if passed as a query parameter. Should be fine for the JSON body path (`POST /api/agent/plan`), but worth noting.

---

## Acceptance Criteria Before Phase 4a Implementation

- [ ] `LegacyBlockIds` field removed from `ItemSpec` MVP (handle in Phase 5)
- [ ] `IReadOnlyList<string>` used for `SourceBlocks` (not `string[]`)
- [ ] `MemorySmithItemRegistry` returns `null` for unknown pages (no LLM call inside)
- [ ] `ItemSpecParserTests` added alongside `ItemRegistryTests`
- [ ] Seed wiki pages use agreed front-matter schema
- [ ] `GatherWoodGoal` kept as factory alias pointing to `GenericGatherGoal` with `oak-log` spec
- [ ] 12+ `GenericGatherGoalTests` covering: smelting-on/off IsComplete, multi-source summation, HasFailed
