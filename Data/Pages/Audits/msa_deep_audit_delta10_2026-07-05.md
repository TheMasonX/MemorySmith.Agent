# MemorySmith.Agent — Deep-Dive Audit: Delta Report #10
**Scope:** First pass over `MemorySmith.Agent.Tests` as a corpus (14,156 lines, 37 files) rather than as individual reference lookups — specifically hunting for the *coverage gaps* that let this audit series' prior findings go undetected, and for test-suite-level duplication/design issues in their own right.
**Format:** Deltas only.

---

## Summary

| # | Type | Item | Severity | Confidence |
|---|------|------|----------|------------|
| 1 | New, confirms delta report #5 finding 1 | `IntentManager` has no dedicated test file; every test that exercises its craft/item resolution passes an already-canonical item name, never raw shorthand — the exact gap that let the "craft a pickaxe" bug ship undetected | High (as coverage evidence) | 96% |
| 2 | New, confirms delta report #1 findings 1–3 | `WorldModel.Predict` has **zero** test coverage for its actual prediction logic, across all ~10 tool branches — the only `WorldModel` tests cover an unrelated defensive-copy concern | High (as coverage evidence) | 97% |
| 3 | New, confirms delta report #7 finding 1 | `ToolDispatchTests.cs` tests "missing argument" for every numeric-argument tool but never "malformed-type argument" — no regression test exercises the throwing-`GetInt32()` brittleness | Medium (as coverage evidence) | 93% |

This report doesn't introduce new *production* bugs — it closes the loop on three previously-reported findings by identifying and confirming the specific test-coverage gaps that allowed each to ship unnoticed, which is directly useful for scoping the fix-plus-regression-test work for each. Also checked and ruled out: `ToolDispatchTests.cs` vs. `ToolDispatcherTests.cs` initially looked like a possible duplicate-file pair (similar names) — confirmed they test genuinely different surfaces (per-tool argument shaping vs. dispatcher routing mechanics) and are not redundant.

---

## 1 — `IntentManager`'s resolution logic has no dedicated tests, and every indirect test uses pre-canonicalized input (96%)

There is no `IntentManagerTests.cs` anywhere in the 37-file test project. `IntentManager`'s craft/item resolution is only ever exercised *incidentally*, as a side effect of tests focused on other things (`Sprint37Tests.cs`, `Sprint39Tests.cs`) — and in every single case found, the `IntentDraft.Item` value supplied is already a canonical Minecraft ID:

```csharp
// Sprint37Tests.cs:123
new IntentDraft("yes", "craft", Item: "iron_pickaxe", Blueprint: null, ...)
// Sprint39Tests.cs:558
new IntentDraft("yes", "craft", "iron_pickaxe", null, 1, null, null, null, 0.9, null, "")
```

Not one test anywhere passes `Item: "pickaxe"`, `"axe"`, `"table"`, or any other `CraftAliases`-only shorthand through `IntentManager` and asserts on the resolved output. The one test that touches `CraftAliases` at all —

```csharp
// Sprint46Tests.cs:237-241
public void AliasRegistry_CraftAliases_ResolvesKnownCraft()
{
    Assert.That(AliasRegistry.CraftAliases["plank"], Is.EqualTo("oak_planks"));
    Assert.That(AliasRegistry.CraftAliases["torch"], Is.EqualTo("torch"));
    Assert.That(AliasRegistry.CraftAliases["furnace"], Is.EqualTo("furnace"));
}
```

— asserts against the dictionary directly, never through `IntentManager.CreateAsync`/`ResolveItem`. All three items it checks (`plank`, `torch`, `furnace`) happen to be ones that *also* exist in `ItemAliases` (confirmed in delta report #5) — so even if this test had gone through `IntentManager`, it would have passed anyway, since it never touches the specific entries (`pickaxe`, `axe`, `table`, armor pieces) where the two dictionaries diverge. This is precise, first-hand confirmation of delta report #5's root-cause analysis: the bug isn't just plausible, it's provably untested at every layer between the alias table and the goal-creation call site.

**Recommendation:** When fixing delta report #5's finding 1, add `IntentManagerTests.cs` covering `CreateAsync`/`ParseCommandString` with each `CraftAliases`-only entry (`pickaxe`, `axe`, `shovel`, `sword`, `table`, `workbench`, `helmet`, `chestplate`, `leggings`, `boots`, etc.) as raw input, asserting the resulting `GoalRequest`'s item is the canonical form — not just that `AliasRegistry`'s dictionaries contain the right values in isolation.

---

## 2 — `WorldModel.Predict` has zero correctness tests across all tool branches (97%)

Full-corpus search for any test referencing `WorldModel` found exactly one file, `Sprint25Tests.cs`, and its `WorldModel`-related tests are entirely about a different, earlier concern:

```csharp
// Sprint25Tests.cs:284-296
[Test]
public void WorldModel_Constructor_SeparateInstances() { ... }   // P1-A: defensive copy
[Test]
public void WorldModel_Observe_DoesNotAliasInventory() { ... }   // P1-A: defensive copy
```

These verify that `WorldModel`'s internal `_observed`/`_belief` dictionaries don't alias each other — nothing to do with `Predict()`'s per-tool inventory logic. Searching specifically for the ten or so `Predict*` private methods (`PredictMine`, `PredictCraft`, `PredictPlace`, `PredictSmelt`, `PredictMove`, `PredictWander`, etc.) by name across the whole test project returns zero matches. There is no test anywhere that calls `WorldModel.Predict(...)` for any tool and asserts on the resulting `PredictedInventory`.

This is a striking gap given `Predict()` is, per delta report #1's findings, the **sole** source of "expected outcome" data for the entire `WorldStateDiff`/evaluator pipeline built across Sprint 58–59 (`TSK-0309`, `TSK-0320`, `TSK-0344`) — a load-bearing method for a multi-sprint feature arc, with no direct unit tests at any point in that arc. This fully explains how `PredictPlace`/`PredictSmelt` returning zero inventory change, and `PredictCraft` never modeling ingredient consumption, shipped and remained unnoticed through three sprints of related work.

**Recommendation:** When fixing delta report #1's findings 1–3, add a `WorldModelPredictTests.cs` with one test per tool branch, each asserting the specific inventory delta `Predict()` claims will happen — this is the natural regression suite that should have existed before `TSK-0344` was built on top of `Predict()`'s output.

---

## 3 — `ToolDispatchTests.cs` covers missing arguments but not malformed-type arguments (93%)

`ToolDispatchTests.cs`'s own doc comment states its purpose precisely: *"Tests that each tool correctly shapes the ActionData dispatched to MockWorldAdapter... verify what crosses the seam."* Reviewing its test names for the six tools flagged in delta report #7 finding 1 (`MoveToTool`, `PlaceBlockTool`, `FurnaceTool`, `CraftItemTool`, `MineBlockTool`, `WanderTool`):

```
MoveToTool_SendsCorrectActionData
MoveToTool_MissingCoords_ReturnsFailure
MineBlockTool_SendsCorrectActionData
MineBlockTool_MissingBlock_ReturnsFailure
WanderTool_DefaultParams_SendsWanderAction
WanderTool_CustomRadius_PassesThrough
PlaceBlockTool_SendsPlaceAction
PlaceBlockTool_MissingMaterial_ReturnsFailure
```

Every "failure" test covers an **absent** property (handled gracefully today via `TryGetProperty` returning `false`) — none covers a **present-but-wrong-type** value (a JSON string where a number is expected, or a non-integer number), which is precisely the case where the throwing `GetInt32()` vs. safe `TryGetInt32()` distinction from delta report #7 actually bites. No test in this file (or elsewhere) currently exercises that path for any of the six affected tools.

**Recommendation:** When generalizing the `TryGetInt32` fix per delta report #7, add one test per affected tool passing a malformed numeric argument (e.g. `"count": "5"` as a JSON string, or `"radius": 3.5`) and asserting a graceful failure/default rather than an unhandled exception bubbling to `ToolDispatcher`'s catch-all — this both documents the intended graceful behavior and would have caught the original gap.

---

## Assumptions & Open Questions (this pass)

1. This was a targeted search (grep for known method/type names relevant to three specific prior findings), not a full line-by-line read of all 14,156 test lines — a genuinely exhaustive pass over the test corpus for its *own* independent bugs (e.g., tests with weak assertions, copy-paste drift between `Sprint*Tests.cs` files, mock setup duplication across the ~20 sprint-numbered files) would be a substantially larger undertaking and is a reasonable candidate for a dedicated follow-up if wanted, distinct from this delta-confirmation pass.
2. Did not verify whether any of the three gaps identified here are already implicitly on someone's radar as "obviously we should test that when we fix it" — these are offered as concrete scoping detail for whoever picks up the underlying fixes, not as standalone new tasks in their own right.
