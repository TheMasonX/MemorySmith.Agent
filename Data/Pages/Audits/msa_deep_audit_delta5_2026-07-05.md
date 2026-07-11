# MemorySmith.Agent — Deep-Dive Audit: Delta Report #5
**Scope:** Continuation of the audit series. This pass covered `ChatInterpreter.cs` (full), the item/blueprint resolution logic in `IntentManager.cs`, `AliasRegistry.cs` (all three alias dictionaries, full), and the relevant slice of `LlmChatInterpreter.cs`'s system-prompt construction (`GetAliasesForPrompt` and surrounding context) — following up directly on the open question flagged in delta report #3, finding #2.
**Format:** Deltas only.

---

## Summary

| # | Type | Item | Severity | Confidence |
|---|------|------|----------|------------|
| 1 | New | Crafting a tool/armor piece by common shorthand ("pickaxe", "axe", "workbench", "helmet", etc.) via the primary chat/LLM path resolves to an unresolvable raw string, not the canonical ID | **High** | 90% |
| 2 | New, consolidation | `ChatInterpreter.ResolveItem`/`ResolveBlueprint`/`ResolveCraftItem` are 100% dead code (zero callers anywhere) while `IntentManager` maintains its own weaker private duplicates that are actually load-bearing | Medium | 95% |

This closes the open question from delta report #3 (finding #2): I originally flagged `ParseCommandString`'s missing space→underscore normalization as a 70%-confidence, "plausible but unconfirmed" gap. Having now read `AliasRegistry.cs` and `ChatInterpreter.cs` in full, the picture is clearer and more serious than that finding suggested — it's not just a missing normalization step, it's a missing dictionary lookup entirely, and it affects the *primary* LLM-driven craft path, not only the secondary `NextSteps` regex parser. Finding 1 below supersedes and significantly upgrades delta report #3's finding #2.

---

## 1 — Craft-shorthand resolution gap: tools and armor don't resolve to canonical IDs (High, 90%)

**The gap:** `IntentManager.TryBuildGoal`/`.CreateAsync` (the path used for *every* chat-driven "craft" intent, both from the primary structured LLM output and the `NextSteps` chaining parser) resolves `draft.Item` through a private method that only checks one of the two relevant alias dictionaries:

```csharp
// IntentManager.cs:107-112
private static string ResolveItem(string item)
{
    if (AliasRegistry.ItemAliases.TryGetValue(item, out var alias))
        return alias;
    return item;   // no CraftAliases check, no normalization fallback
}
```

```csharp
// IntentManager.cs:61 (and again at line 42 for smelt)
return new CraftGoalRequest(ResolveItem(draft.Item), draft.Count ?? 1);
```

`AliasRegistry.CraftAliases` (`AliasRegistry.cs:103-140+`) is a ~35-entry dictionary specifically curated for craft-shorthand disambiguation — mapping ambiguous player language to a *specific tier* (`"pickaxe"` → `"wooden_pickaxe"`, `"axe"` → `"wooden_axe"`) or a *specific canonical form* (`"table"`/`"workbench"` → `"crafting_table"`). Cross-checked against `ItemAliases`: **none** of the following `CraftAliases`-only entries exist in `ItemAliases` at all: `pickaxe`, `axe`, `shovel`, `sword`, `workbench`, `crafting table`, `helmet`, `chestplate`, `leggings`, `boots`, `bowl`, `ladder`, `door`, `trapdoor`, `slab`, `sign` (confirmed via direct grep against both dictionaries). `table`/`workbench` are separately, partially covered by a *different*, hand-curated list injected into the LLM's own system prompt (`AliasRegistry.GetAliasesForPrompt()`, `LlmChatInterpreter.cs:376`, added under TSK-0304) — but that list does **not** include `pickaxe`, `axe`, `shovel`, `sword`, or any of the four armor pieces.

**Why this is a live bug, not a theoretical one:** the LLM's own system prompt uses *"craft a pickaxe"* as its literal few-shot example for the `"craft"` intent (`LlmChatInterpreter.cs:269`), and nothing in the prompt tells the LLM that `"pickaxe"` needs to become `"wooden_pickaxe"` (that specific alias isn't in `GetAliasesForPrompt`'s curated list). So the most natural, LLM-encouraged phrasing for one of the most common early-game crafts — a tool — is very likely to arrive at `IntentManager` as `draft.Item = "pickaxe"`, which `ResolveItem` cannot resolve (not in `ItemAliases`), producing `CraftGoalRequest("pickaxe", ...)`. Every downstream consumer of the item ID (`HtnTaskLibrary.DecomposeCraftItem`, the Mineflayer `craft` handler's `bot.registry.itemsByName[...]` lookup) expects real Minecraft IDs (`wooden_pickaxe`), not player shorthand — this would fail to find a valid item/recipe.

**Scope of impact:** all four basic tools (pickaxe, axe, shovel, sword), all four iron armor pieces, plus `bowl`, `ladder`, `door`, `trapdoor`, `slab`, `sign` — a substantial fraction of `CraftAliases`' ~35 entries are affected. Items that exist identically in both dictionaries (`stick`, `chest`, `torch`, `furnace`, `planks`, and their variants) are unaffected — they resolve correctly regardless of which dictionary is checked, which is likely why this hasn't surfaced as an obvious, universally-reproducing bug report: it depends on which specific item is requested.

**Distinct from the already-tracked `TSK-0034`** (torch-craft failure): confirmed by reading that task directly — its root cause is a Mineflayer/JS-side `recipesFor()` call behavior issue with `torch`, a fully-resolved identity-mapped item name; it is unrelated to alias resolution and doesn't overlap with this finding.

**Recommendation:** Change `IntentManager`'s craft-goal creation (both the primary path at line 61 and the `NextSteps` parser at line 133) to check `CraftAliases` before falling back to `ItemAliases` — i.e., give `IntentManager` access to the same two-tier resolution `ChatInterpreter.ResolveCraftItem` already implements correctly (see Finding 2 — that method exists, is correct, and is simply never called). Also consider extending `GetAliasesForPrompt()`'s curated LLM-facing list with the tool-tier and armor entries, as defense in depth (belt-and-suspenders: even if the LLM is told the right canonical ID up front, `IntentManager` should still resolve correctly for the cases where it isn't).

---

## 2 — `ChatInterpreter`'s three `Resolve*` methods are dead code; `IntentManager` maintains weaker live duplicates (Medium, 95%)

Repo-wide search confirms `ChatInterpreter.ResolveItem`, `ChatInterpreter.ResolveBlueprint`, and `ChatInterpreter.ResolveCraftItem` (all `public static`, `ChatInterpreter.cs:322-354`) have **zero callers anywhere in the codebase** — not from `IntentManager`, not from `LlmChatInterpreter`, and not even from within `ChatInterpreter.ParseIntent` itself (the gather/build/craft regex blocks that would have called them were removed under Sprint 35 P1-D / `TSK-0118`, and nothing replaced those call sites). They are fully unreachable.

Meanwhile, `IntentManager` — which *is* on the live path for every chat-driven goal — maintains its own private re-implementations:

| | `ChatInterpreter` (dead) | `IntentManager` (live) |
|---|---|---|
| Item resolution | `ResolveItem`: trims, lowercases, checks `ItemAliases`, falls back to lowercased+underscored raw string | `ResolveItem`: checks `ItemAliases` only, no normalization, returns raw string verbatim on miss |
| Craft resolution | `ResolveCraftItem`: checks `CraftAliases` first, then falls back to `ResolveItem` | *(none — craft path calls the generic `ResolveItem` directly, see Finding 1)* |
| Blueprint resolution | `ResolveBlueprint`: trims, lowercases, checks `BlueprintAliases`, falls back to lowercased+hyphenated raw string | `ResolveBlueprint`: checks `BlueprintAliases` only, no normalization, returns raw string verbatim on miss (null-safe) |

This is the same root cause behind Finding 1 (and behind the now-superseded, lower-confidence finding in delta report #3): two independent implementations of "resolve player-facing name to canonical ID" exist in the same namespace, one of them unreachable and strictly more correct, the other unreachable-*to-fix* only in the sense that nobody noticed the live one needed the extra dictionary check.

**Recommendation:** Consolidate into a single source of truth — most naturally as static methods on `AliasRegistry` itself, since it already owns all three dictionaries (`ItemAliases`, `BlueprintAliases`, `CraftAliases`) and is referenced by both classes already. Concretely:
1. Move `ResolveItem`/`ResolveBlueprint`/`ResolveCraftItem` (with their normalization + fallback logic) from `ChatInterpreter` into `AliasRegistry`.
2. Delete `IntentManager`'s three private duplicates; call the `AliasRegistry` versions instead — this fixes Finding 1 as a side effect of removing the duplication, rather than needing a separate targeted patch.
3. Delete the now-empty `ChatInterpreter` static methods (or leave them as thin wrappers calling `AliasRegistry` if external callers are ever added later — but as of today, deleting is the more honest reflection of actual usage).

No existing task tracks this consolidation or the underlying dead-code observation (`TSK-0118` already removed the *regex match blocks* that used to call these methods, under the stated rationale "All gather/build/craft intent is handled by `LlmChatInterpreter` → LLM → `IntentDraft` pipeline" — but that task's own comment shows awareness that resolution still needs to happen somewhere downstream ("preserved for use by `LlmChatInterpreter`'s item normalization in Sprint 36") — that follow-through evidently never happened; `LlmChatInterpreter` does not call any of these three methods either, confirmed by grep).

---

## Assumptions & Open Questions (this pass)

1. Did not verify whether any **unit test** currently exercises `IntentManager`'s craft-goal creation for a tool/armor item (e.g., `"craft a pickaxe"` end-to-end) — if such a test exists and passes, it would mean either my reading is wrong or the test itself doesn't assert on the resolved item ID strongly enough to catch this. Worth a quick check before filing the fix as a bug rather than a "confirm and fix" task.
2. Did not trace what `HtnTaskLibrary.DecomposeCraftItem` or the Mineflayer `craft` action handler actually do when handed a genuinely unresolvable item string like `"pickaxe"` (immediate failure with a clear error vs. a more confusing downstream symptom) — the claim that it "fails" is based on the general expectation that recipe/registry lookups require exact Minecraft IDs, not on having reproduced the failure directly, since this audit doesn't have live-game access.
3. This finding narrows/refines (rather than duplicates) delta report #3's finding #2 — recommend treating that finding as superseded by this one in any task filed, since the root cause identified here is more precise and the fix (Finding 2's consolidation) naturally subsumes the space-normalization concern raised earlier.
