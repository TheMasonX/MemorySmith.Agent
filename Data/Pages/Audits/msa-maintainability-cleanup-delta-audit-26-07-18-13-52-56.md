# MemorySmith.Agent — Delta Audit #4: Maintainability, Test Organization & Composition-Root Bloat

**Repo/commit:** `TheMasonX/MemorySmith.Agent` @ `0f27af1befb72e7534421a1bd5550aee1e077d96` (`dev/round-3`) — same commit as prior three reports.
**This is a delta report.** Only new findings, not already in the three prior reports or `Data/Tasks/*.json`.
**Focus this pass (per request):** cleanup, tech-debt reduction, bloat, maintainability — resolved the two outstanding unverified `jscpd` hits from Delta #2 (D11), then extended into composition-root organization (`Program.cs`) and test-suite organization (61 test files, 806 `[Test]` methods), both direct maintainability levers not yet examined.

---

## Executive Summary

| # | Finding | Class | Confidence | Impact |
|---|---|---|---|---|
| G1 | **Exact, byte-for-byte duplicate `PlankToLogMap` dictionary** — defined once (correctly) in the shared `Agent.Core/CommonMinecraftBlocks.cs:156` and *again*, independently, as a private dictionary in `Agent.Planning/HtnTaskLibrary.cs:68`, identical down to entry order. `HtnTaskLibrary.cs` already references `CommonMinecraftBlocks` elsewhere in the same file (3 call sites) — this one map just wasn't consolidated. | Duplicated Code (exact) | 97% | Medium — a future new wood type (e.g. a modded/1.21+ log) added to one copy and not the other silently breaks build-material gathering for that type |
| G2 | **`Program.cs` is an un-decomposed 919-line composition root**: 44 `AddSingleton`/`AddScoped`/`AddTransient`/`AddHttpClient` calls, options binding, 3 HttpClient registrations, middleware pipeline, and endpoint mapping all as flat top-level statements with exactly **one** section-comment in the entire file. This is a third instance of the same "single file doing everything" pattern already flagged for `AgentBackgroundService.cs` (prior report F1) and `MineflayerAdapter/index.js` (prior report F6) — here in the startup/DI layer. Backlog item TSK-0047 is about to add *more* registrations to this file, which will make the problem worse if landed before any reorganization. | God File / Divergent Change | 88% | Medium — mostly a "time to find/change the right registration" cost, compounding as the file grows |
| G3 | **23 of 61 test files (~38%) are named and organized by sprint number** (`Sprint19Tests.cs` … `Sprint60AntiforgeryTests.cs`) rather than by the class/feature under test. Hand-verified: `Sprint19Tests.cs` in fact tests `GoalFactory`/gather-plan/stone-alias behavior — logically `GoalFactoryTests.cs`/`GenericGatherGoalTests.cs` territory — but lives in a sprint-numbered file instead. This is the test-suite analog of the "Sprint N comment-as-changelog" anti-pattern already quantified in Delta #2 (D10): using sprint identity instead of code structure as the organizing principle, here applied to test *files* rather than *comments*. | Test organization / maintainability | 85% | Medium — real risk of duplicate or lost coverage: a developer checking "what tests exist for X" has to search ~5+ files, not one |
| G4 | (Resolved from Delta #2's D11, downgraded) **`WorldStateProjector.cs` self-clone (279–287 vs 398–406) hand-verified**: it's two `switch` cases (`SpawnEvent`, `StatusEvent`) that legitimately set the same 3 facts (`Pos`/`Health`/`Food`) because those two event types genuinely share that shape. Not a bug — a defensible, minor Repeated-Switches instance. Optional micro-refactor (extract `SetPosHealthFood(b, pos, health, food, prefix)`), not urgent. | Duplicated Code (minor, low-priority) | 90% | Trivial |
| G5 | Minor naming-clarity nit: `ToolDispatchTests.cs` (tests individual `ITool` implementations' argument-passing) and `ToolDispatcherTests.cs` (tests the `ToolDispatcher` class's own dispatch/error-handling behavior) are two genuinely distinct, non-overlapping files — hand-verified, not duplicate content — but their near-identical names make it easy to assume overlap or add a test to the wrong file. | Naming / Mysterious Name (mild) | 80% | Trivial |

**Note on G4:** Delta #2 flagged this jscpd hit at 60% confidence pending manual verification. Verifying it now — it turned out to be a low-priority, defensible duplicate rather than a real problem. Reporting the resolution here for completeness/honesty about the earlier report's open item, per the standing instruction to verify claims rather than let them sit unconfirmed.

---

## Detailed Findings

### G1 — Duplicate `PlankToLogMap` dictionary

**Evidence.**

`Agent.Core/CommonMinecraftBlocks.cs:156–164` (the intended shared, canonical source — this class's own doc comment describes it as the reference used by `LocalKnowledgeResolver.ClassifySpec` and is already consumed elsewhere):
```csharp
public static readonly IReadOnlyDictionary<string, string> PlankToLogMap =
    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["oak_planks"]       = "oak_log",
    ["birch_planks"]     = "birch_log",
    ["spruce_planks"]    = "spruce_log",
    ["dark_oak_planks"]  = "dark_oak_log",
    ["jungle_planks"]    = "jungle_log",
    ["acacia_planks"]    = "acacia_log",
    ["mangrove_planks"]  = "mangrove_log",
    ["cherry_planks"]    = "cherry_log",
};
```

`Agent.Planning/HtnTaskLibrary.cs:68–76` — a **private, independently-maintained copy**, identical entry-for-entry:
```csharp
private static readonly IReadOnlyDictionary<string, string> PlankToLogMap =
    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["oak_planks"]       = "oak_log",
    ["birch_planks"]     = "birch_log",
    ["spruce_planks"]    = "spruce_log",
    ["dark_oak_planks"]  = "dark_oak_log",
    ["jungle_planks"]    = "jungle_log",
    ["acacia_planks"]    = "acacia_log",
    ["mangrove_planks"]  = "mangrove_log",
    ["cherry_planks"]    = "cherry_log",
};
```

`HtnTaskLibrary.cs` already has 3 other call sites referencing `CommonMinecraftBlocks.*` in the same file, so the project reference and the "go check the shared class first" habit both already exist here — this single map was simply never consolidated, most likely because it was added independently in whatever sprint introduced the build-crafting chain (`EmitCraftIfNeeded`'s doc comment references "the build crafting chain," a different concern than `CommonMinecraftBlocks`'s general classification use, so it's an understandable miss, not a careless one).

**Why this matters concretely:** the two copies have zero mechanism forcing them to stay in sync. Adding support for a new plank/log pair (a realistic future need — new wood types ship in most Minecraft versions) requires remembering to update *both* files; missing one produces a silent, hard-to-diagnose bug where build-material gathering works correctly via one code path (`LocalKnowledgeResolver`) and incorrectly via the other (`HtnTaskLibrary`'s `EmitCraftIfNeeded`) for the exact same wood type.

**Recommendation.** Delete the private copy in `HtnTaskLibrary.cs`; replace its 1 internal usage with `CommonMinecraftBlocks.PlankToLogMap`. Five-minute fix, zero behavioral risk (values are identical today), removes a live drift hazard.

**Confidence: 97%.** Direct side-by-side text comparison; both dictionaries are `static readonly`, so there's no dynamic-content ambiguity.

---

### G2 — `Program.cs`: 919-line, minimally-structured composition root

**Evidence.**
```
$ wc -l WebUI.Blazor/Program.cs
919
$ grep -c "AddSingleton\|AddScoped\|AddTransient\|AddHttpClient" WebUI.Blazor/Program.cs
44
$ grep -c "^// ──" WebUI.Blazor/Program.cs
1
```
One section-comment (`// ── Options ──`) across an otherwise undifferentiated 919-line sequence of top-level statements covering: options binding, 3 named `HttpClient` registrations with their resilience handlers, 44 service-lifetime registrations spanning at least 6 logical subsystems (planning/HTN, tools, memory/knowledge, dashboard/SignalR, LLM providers, safety/options), Kestrel/hosting configuration, the middleware pipeline, and endpoint mapping — all in one file, one execution order, with no grouping into named, independently-readable units.

This is architecturally the same failure mode already documented twice in this audit series — `AgentBackgroundService.cs` (god class, 4,019 lines, prior Report 1 F1) and `MineflayerAdapter/index.js` (god file, 2,189 lines, prior Report 1 F6) — recurring a third time at the composition-root layer. The common thread across all three: incremental sprint-by-sprint additions to a single file with no periodic "extract and organize" pass, which is a maintainability pattern worth naming explicitly as a repo-wide habit to address, not just three unrelated one-off files.

**Compounding factor:** TSK-0047 ("Program.cs DI Wiring & Endpoints," currently Backlog) is scoped to add *more* registrations and *two new endpoints* directly into this file. Landing that work before any reorganization will make the eventual refactor larger and riskier; landing a light structural pass first (even just extension-method extraction, no behavior change) would make TSK-0047 easier to review in isolation.

**Recommendation.**
1. Extract cohesive extension methods, e.g. `builder.Services.AddPlanningServices()`, `.AddToolServices()`, `.AddMemoryServices()`, `.AddDashboardServices()`, `.AddLlmProviders()`, each living in its own small static class (`WebUI.Blazor/ServiceCollectionExtensions/*.cs` or similar) — pure mechanical extraction, no DI-lifetime or ordering changes, so it's low-risk and independently testable/reviewable in small chunks.
2. Do this *before* TSK-0047 lands its new registrations, so the new dashboard-related services land inside the newly-created `AddDashboardServices()` extension rather than adding to the flat file.
3. This does not need to happen in one PR — even extracting 2–3 of the largest logical groups (planning services and LLM providers are probably the biggest single blocks) would meaningfully improve navigability without a full rewrite.

**Confidence: 88%.** The line/registration counts are exact; "this is a maintainability problem worth fixing" and the suggested extraction boundaries are judgment calls, not derived from a hard team standard (same caveat as prior reports' file-size recommendations).

---

### G3 — Test files organized by sprint number, not by subject under test

**Evidence.**
```
$ find MemorySmith.Agent.Tests -name "*.cs" | xargs -n1 basename | grep -c "^Sprint"
23   (of 61 total .cs files in the test project)
```
Files: `Sprint19Tests.cs`, `Sprint20Tests.cs`, `Sprint21Tests.cs`, `Sprint22Tests.cs`, `Sprint23Tests.cs`, `Sprint25Tests.cs`, `Sprint26Tests.cs`, `Sprint27Tests.cs`, `Sprint28Tests.cs`, `Sprint30Tests.cs`, `Sprint32Tests.cs`, `Sprint35Tests.cs`, `Sprint36Tests.cs`, `Sprint37Tests.cs`, `Sprint38Tests.cs`, `Sprint39Tests.cs`, `Sprint44Tests.cs`, `Sprint46Tests.cs`, `Sprint47Tests.cs`, `Sprint48Tests.cs`, `Sprint51Tests.cs`, `Sprint57ExecutionContextTests.cs`, `Sprint60AntiforgeryTests.cs`.

Hand-verified `Sprint19Tests.cs` (rather than trusting the name alone): its 8 `[Test]` methods are `GatherPlan_NoBlockNotFound_OmitsWander`, `GatherPlan_AfterBlockNotFound_IncludesWander`, `StoneAlias_ResolvesToStone_NotCobblestone`, `GoalFactory_GatherStone_SourceBlocksIncludesCobblestone`, etc. — this is squarely `GoalFactory`/gather-goal-planning test material, which the project *already has* dedicated homes for (`GoalFactoryTests.cs`, `GoalFactoryBuiltInTests.cs`, `GoalFactoryBuildTests.cs`, `GenericGatherGoalTests.cs`, `GatherWoodGoalTests.cs` all exist). A developer trying to find "all tests that exercise `GoalFactory`'s gather-plan behavior" would need to check at least 6 files, one of which is named after a sprint number with no indication of its subject.

Two later sprint files (`Sprint57ExecutionContextTests.cs`, `Sprint60AntiforgeryTests.cs`) do partially self-describe their subject in the filename — a sign the team has already started drifting toward better naming for newer files, which is worth reinforcing rather than treating this as an all-or-nothing cleanup.

This mirrors Delta #2's D10 finding (1,080 "Sprint N" comments used as an in-code changelog substitute) — same underlying habit, applied to file organization instead of inline comments. Worth flagging together since a single process fix (documenting a "tests live next to what they test; sprint number goes in the commit/PR, not the filename" convention) would address both current and future instances of this pattern.

**Recommendation.**
1. Not a rewrite — a renaming/merging pass: for each `SprintNTests.cs`, identify its actual subject class(es) (feasible from the `[TestFixture]`/method names alone, as demonstrated above) and either merge its tests into the existing subject-named file or rename the file to match its content.
2. This is safe to do incrementally, file by file, since NUnit doesn't care about file names — each merge/rename is an independent, low-risk PR.
3. Adopt a naming convention going forward (e.g., in a `CONTRIBUTING.md` or the existing `Data/Pages/policies/` folder, which already hosts the package-vetting policy) so new sprint work adds tests to subject-named files by default rather than creating a new `SprintNTests.cs`.

**Confidence: 85%.** File-naming pattern and the one hand-verified example are solid evidence; whether *all 23* files have the same "actually belongs elsewhere" property wasn't individually checked for each file (time-boxed), so treat the other 22 as "very likely same pattern, not individually confirmed."

---

### G4 — `WorldStateProjector.cs` self-clone: verified low-priority (Delta #2 D11 follow-up)

**Evidence.** The two `jscpd`-flagged regions (lines 279–287 and 398–406) are `SpawnEvent` and `StatusEvent` cases inside the same `switch` in `StoreFacts`, both setting `Pos`/`Health`/`Food` facts with the same 3-line shape — because those two Minecraft event types genuinely carry the same 3 fields. This is a legitimate (if mildly repetitive) consequence of the switch-per-event-type structure, not a bug or a maintenance hazard on the same order as G1.

**Recommendation.** Optional: extract a `private static WorldStateBuilder SetPosHealthFood(WorldStateBuilder b, string prefix, Position pos, double health, double food, FactSource source)` helper and call it from both cases. Low priority — include only if doing a broader pass on this file for other reasons.

**Confidence: 90%.**

---

### G5 — `ToolDispatchTests.cs` vs `ToolDispatcherTests.cs` naming clarity

**Evidence.** Hand-verified both files' actual test method names (see table below) — genuinely distinct, non-overlapping subjects:

| File | Tests | Subject |
|---|---|---|
| `ToolDispatchTests.cs` (310 lines) | `MoveToTool_SendsCorrectActionData`, `MineBlockTool_MissingBlock_ReturnsFailure`, `PlaceBlockTool_SendsPlaceAction`, etc. | Individual `ITool` implementations' argument marshalling |
| `ToolDispatcherTests.cs` (179 lines) | `CallAsync_UnknownTool_ReturnsFailure`, `CallAsync_ThrowingTool_CapturesExceptionTypeInJournal`, etc. | `ToolDispatcher` class's own routing/error-handling behavior |

No actual duplication — this is purely a "the names are one character apart and easy to confuse" nit, included for completeness since precise naming was explicitly in scope for this pass. Consider renaming one (e.g. `ToolDispatchTests.cs` → `IndividualToolArgumentTests.cs` or similar) if it's ever a source of confusion in practice; not worth an isolated PR on its own.

**Confidence: 80%.**

---

## Assumptions & Open Questions

1. **G3's "very likely same pattern" claim for the 21 unverified sprint-named test files** is an extrapolation from one hand-checked example, not 23 independent verifications — recommend a quick per-file skim before committing to a specific merge plan, since a couple of these files may genuinely be sprint-specific integration/regression tests with no single natural subject-file home (in which case leaving them as-is, but perhaps renamed to reflect content, is the right call rather than forcing a merge).
2. **G2's suggested extension-method boundaries** (`AddPlanningServices`, `AddToolServices`, etc.) are a reasonable first cut based on reading the file's registration order, not a guaranteed-optimal grouping — whoever implements this should feel free to adjust groupings based on what's actually easiest to review, not treat this report's suggested names as fixed.
3. No new tooling limitations this pass beyond those already noted in prior reports (no `dotnet build`, no NuGet access).

---

## Suggested Sequencing (additive to prior reports)

1. **G1** (delete duplicate `PlankToLogMap`) — trivial, zero-risk, do any time.
2. **G4** (optional `WorldStateProjector` micro-extract) — bundle only if touching that file for another reason; not worth a standalone PR.
3. **G2** (extract `Program.cs` into named service-registration methods) — do **before** TSK-0047 lands, to avoid compounding the file's size right before a reorg would happen anyway.
4. **G3** (merge/rename sprint-numbered test files) — safe to do incrementally; good "spare cycle" cleanup work, ideally paired with adopting a written naming convention so the pattern doesn't recur.
5. **G5** — cosmetic, lowest priority, opportunistic only.
