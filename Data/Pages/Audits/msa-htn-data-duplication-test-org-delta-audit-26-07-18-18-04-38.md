# MemorySmith.Agent — Delta Audit #4: HTN Data Duplication & Test-Suite Organization

**Repo/commit:** `TheMasonX/MemorySmith.Agent` @ `0f27af1befb72e7534421a1bd5550aee1e077d96` (`dev/round-3`) — same commit as prior three reports.
**This is a delta report.** Contains only findings not already in Reports #1–3 or `Data/Tasks/*.json`. This pass closes out the previously-flagged-but-unverified jscpd hits from Report 2's D11, plus a first pass over the 806-test NUnit suite (`MemorySmith.Agent.Tests/`, 61 files, 14,370 lines).

---

## Executive Summary

| # | Finding | Class | Confidence | Impact |
|---|---|---|---|---|
| G1 | **Verbatim duplicate lookup dictionaries between the designated shared-constants file and its consumer.** `Agent.Core/CommonMinecraftBlocks.cs` (`public static readonly`, meant to be the canonical source per its name and doc comments) and `Agent.Planning/HtnTaskLibrary.cs` (`private static readonly`) each independently define `PlankToLogMap` (8 entries), `IronIngotRequirements` (9 entries), and `CobblestoneRequirements` (5 entries) — all three character-for-character identical. **Root cause identified via git history**: the commit that introduced the duplicates (`b11f977`, Sprint 14 P0) is literally titled *"...use CommonMinecraftBlocks"* — the intent to consolidate was recorded in the commit message, but the diff pasted local copies instead of referencing the shared file. This has silently persisted, unnoticed, for 46+ sprints. | Duplicated Code (data) / commit-message-doesn't-match-diff | 96% | Medium — same drift class that already caused a real bug elsewhere in this codebase (Report 1's `BLOCK_MINING_ALIASES` finding) |
| G2 | **~25-line near-verbatim duplicate control-flow block** in `HtnTaskLibrary.cs` between the "creative mode" block-placement path and the regular block-placement path: both independently implement "skip already-placed blocks (TSK-0125) → skip placing at the bot's own position (TSK-0123) → tag context → collect actions → append GetStatus," differing only in local variable names (`creativeBlockActions`/`creativeBotPos` vs. `blockActions`/`botPos`). | Duplicated Code | 93% | Medium — this is exactly the kind of logic where the two copies drifting (e.g., a future fix to the skip-own-position check landing in only one branch) would produce a hard-to-reproduce, mode-dependent build bug |
| G3 | **Test suite has two competing organizational schemes**: 41 files named by subject (`ChatInterpreterTests.cs`, `GoalFactoryTests.cs`, `HtnPlannerTests.cs`, ...) and **20 files named by sprint number** (`Sprint19Tests.cs` through `Sprint60Tests.cs`, plus `Sprint57ExecutionContextTests.cs`/`Sprint60AntiforgeryTests.cs`). Tests for the same subsystem are consequently scattered — e.g., `HtnTaskLibrary` behavior is covered across `HtnTaskLibraryCraftingTests.cs`, `HtnTaskLibraryExtraTests.cs`, **and** `Sprint19Tests.cs` (confirmed: 3 of `Sprint19Tests.cs`'s 8 tests directly instantiate and exercise `HtnTaskLibrary`). This is the file-organization-level counterpart to the "Sprint N comment-as-changelog" anti-pattern already flagged in Delta #2 (D10), and it's not hypothetical risk — this audit series has already found real, drifted, duplicate production code (Report 1 F6, Report 2 D4/D6, this report's G1/G2), and scattering tests for the same class across sprint-numbered files makes it correspondingly harder for a contributor to check "is this already tested?" before adding new coverage. | Test organization / AI-codesmell (sprint-numbered artifacts) | 88% | Low-Medium (maintainability, not correctness) |
| G4 | Confirmed, minor: `WorldStateProjector.StoreFacts`'s event-type switch has two branches (`SpawnEvent`, `StatusEvent`) that set the identical `Pos`/`Health`/`Food` fact triple. Inherent to a switch-per-event-type mapper (not all branches can reasonably be deduplicated), but these two specifically could share a local `SetVitals(builder, prefix, pos, health, food, source)` helper. Low priority — included for completeness since it was carried over as an unverified item from Report 2's D11. | Duplicated Code (minor) | 80% | Low |

**Test-suite health, overall:** contrary to what G3 might suggest, the tests themselves are **not shallow** — a manual sample of `Sprint19Tests.cs` and others showed specific, well-named, behavior-asserting tests (e.g. `GatherPlan_NoBlockNotFound_OmitsWander`), and the suite averages ~2 `Assert` calls per test (1,624 asserts / 806 `[Test]` methods) with no evidence of trivial "assert not null and stop" patterns in the files sampled. G3 is a findability/organization concern, not a coverage-quality concern.

---

## Detailed Findings

### G1 — `HtnTaskLibrary.cs` re-declares 3 dictionaries that `CommonMinecraftBlocks.cs` already owns

**Evidence.**
```
$ jscpd (C# tree): 3 clone pairs between Agent.Core/CommonMinecraftBlocks.cs and Agent.Planning/HtnTaskLibrary.cs
  CommonMinecraftBlocks.cs:150-165  <->  HtnTaskLibrary.cs:68-79    (PlankToLogMap, 8 entries)
  CommonMinecraftBlocks.cs:180-192  <->  HtnTaskLibrary.cs:139-151  (IronIngotRequirements, 9 entries)
  CommonMinecraftBlocks.cs:195-203  <->  HtnTaskLibrary.cs:157-165  (CobblestoneRequirements, 5 entries)
```
All three pairs are verbatim-identical key/value data, hand-confirmed by direct comparison (not just jscpd's token-match). `CommonMinecraftBlocks.cs`'s fields are `public static readonly` — designed to be consumed externally; `HtnTaskLibrary.cs`'s copies are `private static readonly` — self-contained duplicates, not references.

**Root cause (git-history confirmed):**
```
$ git log --diff-filter=A --format="%ai %s" -- Agent.Core/CommonMinecraftBlocks.cs
2026-06-17 12:32:46 -0500 feat(Sprint14-P1a): add CommonMinecraftBlocks shared block constant

$ git log -S"private static readonly IReadOnlyDictionary<string, int> IronIngotRequirements" --oneline -- Agent.Planning/HtnTaskLibrary.cs
b11f977 feat(Sprint14-P0): DecomposeCraftItem pre-gathers iron/stone materials; use CommonMinecraftBlocks
```
The commit that added the *local, private* copies of `IronIngotRequirements`/`CobblestoneRequirements` to `HtnTaskLibrary.cs` is the **same commit**, two seconds after `CommonMinecraftBlocks.cs` was created, whose own message says *"use CommonMinecraftBlocks."* The intent to consolidate is right there in the commit history — the actual diff just didn't do it. This is a clean example of a stated-intent/actual-diff mismatch that's gone unnoticed for the entire Sprint 14→60 span (46+ sprints, ~1 month of wall-clock time in this project's cadence).

**Why this matters beyond tidiness:** this codebase has already demonstrated (Report 1, finding F6) that exactly this pattern — the same lookup table maintained in two places — causes real bugs when one copy gets updated (a new block/item added, a recipe rebalance) and the other doesn't. A future Minecraft-version bump adding new plank/ingot/cobblestone-tool variants would need to remember to update both files, with nothing enforcing that.

**Recommendation.** Delete the three private fields from `HtnTaskLibrary.cs` and replace their ~6 call sites with references to `CommonMinecraftBlocks.PlankToLogMap` / `.IronIngotRequirements` / `.CobblestoneRequirements` (already `public`, so no visibility change needed — `Agent.Planning` already has a project reference to `Agent.Core`, confirmed by other `Agent.Core` usages in the same file). Mechanical, near-zero-risk, ~20-minute fix. Recommend a matching unit test asserting reference/value equality between the two, or simply deleting one side, so this can't silently re-diverge.

**Confidence: 96%.**

---

### G2 — Duplicate "filter and tag block-placement actions" loop (creative vs. regular build path)

**Evidence** (`Agent.Planning/HtnTaskLibrary.cs`, ~lines 500–524 vs. ~579–605):
```csharp
// "creative" branch
for (int i = 0; i < creativeBlockActions.Count; i++)
{
    // TSK-0125: skip blocks already marked as placed
    var statusKey = BuildFactKeys.BlockStatus(blueprint.Name, i);
    if (state.Facts.TryGetValue(statusKey, out var statusVal) &&
        statusVal?.ToString() == BuildFactKeys.BlockStatusPlaced)
        continue;
    var placeAction = creativeBlockActions[i];
    // TSK-0123: skip PlaceBlock at bot's current position — ...
    if (placeAction.Arguments.TryGetValue("x", out var px) && px is int placeX && ...)
    {
        if (placeX == creativeBotPos.X && placeZ == creativeBotPos.Z && placeY == creativeBotPos.Y)
            continue;
    }
    placeAction.Context[BuildFactKeys.PlaceBlockProgressBlueprintId] = blueprint.Name;
    placeAction.Context[BuildFactKeys.PlaceBlockProgressBlockIndex]  = i;
    actions.Add(placeAction);
}
actions.Add(ActionFactory.Create("GetStatus"));
```
and the near-identical "regular" branch immediately after, operating on `blockActions`/`botPos` instead of `creativeBlockActions`/`creativeBotPos`. Both copies carry the *same two TSK-# comments* (TSK-0125, TSK-0123) verbatim — strong evidence this was a copy-paste of the whole block when the creative-mode path was added, rather than a call to a shared helper.

**Recommendation.** Extract `FilterAndTagPlaceActions(IReadOnlyList<ActionData> blockActions, WorldState state, Blueprint blueprint, Position botPos, List<ActionData> actions)` and call it from both branches with their respective variables. Same risk profile as G1 — mechanical, low-risk, and removes a second live drift vector in the same file.

**Confidence: 93%.**

---

### G3 — Sprint-numbered test files scatter coverage for the same subsystems

**Evidence.**
```
$ find MemorySmith.Agent.Tests -name "*.cs" | xargs -n1 basename | sort
... 41 subject-named files (ChatInterpreterTests.cs, GoalFactoryTests.cs, HtnPlannerTests.cs, ...) ...
Sprint19Tests.cs, Sprint20Tests.cs, Sprint21Tests.cs, Sprint22Tests.cs, Sprint23Tests.cs,
Sprint25Tests.cs, Sprint26Tests.cs, Sprint27Tests.cs, Sprint28Tests.cs, Sprint30Tests.cs,
Sprint32Tests.cs, Sprint35Tests.cs, Sprint36Tests.cs, Sprint37Tests.cs, Sprint38Tests.cs,
Sprint39Tests.cs, Sprint44Tests.cs, Sprint46Tests.cs, Sprint47Tests.cs, Sprint48Tests.cs,
Sprint51Tests.cs, Sprint57ExecutionContextTests.cs, Sprint60AntiforgeryTests.cs
```
20 files, named purely by when they were written rather than what they test. Confirmed overlap: `Sprint19Tests.cs`'s 8 tests are about gather-plan wander-omission behavior — 3 of them directly instantiate `new HtnTaskLibrary()` and assert on its output, the same class covered by the purpose-named `HtnTaskLibraryCraftingTests.cs` and `HtnTaskLibraryExtraTests.cs`. `Sprint48Tests.cs`'s own doc comment (visible in its header) literally lists which TSK-#s and classes it covers (`TSK-0105`, `TSK-0103`, `TSK-0082`/`SmeltableMapping`) — useful metadata, but it's metadata that belongs in a subject-named file so it's discoverable by class, not by sprint.

This mirrors Delta #2's D10 finding (1,080 "Sprint N" comments scattered through production code) at the test-file level: the same underlying habit (dating artifacts to when they were written instead of organizing by what they're about) shows up in both places, and it's a specific instance of the recurring root cause this audit series keeps surfacing — things get added in the moment without checking whether an existing, better-organized home for them already exists (the same root cause as G1's dictionary duplication and Report 1/2's dead-scaffolding findings).

**Recommendation.** Not urgent, and not something to batch-fix in one PR (61 files, real risk of merge conflicts / breaking CI mid-flight) — but worth a standing convention going forward: new tests for an existing class go into that class's existing test file (or a new subject-named file if none exists), not into a new `SprintNTests.cs`. As a lower-effort partial win, the existing `SprintNTests.cs` files' doc-comment headers (which already state what TSK-#/class each file covers, as seen in `Sprint48Tests.cs`) could be used to progressively re-file tests into subject-named files opportunistically whenever one of those files is touched anyway, rather than as a dedicated migration project.

**Confidence: 88%.** The organizational pattern and the `Sprint19`/`HtnTaskLibrary` overlap are directly confirmed; the "this makes duplicate-test-risk worse" causal claim is a reasoned inference (supported by this audit series' independent findings of real production-code duplication) rather than a directly observed instance of a duplicated *test*.

---

### G4 — Minor: `WorldStateProjector` two switch branches set identical fact triple

**Evidence** (`Agent.Core/WorldStateProjector.cs`, `StoreFacts`): the `SpawnEvent` branch (~line 280) and the `StatusEvent` branch (~line 399) both execute the identical three-line `SetFact($"{prefix}Pos"...)` / `SetFact($"{prefix}Health"...)` / `SetFact($"{prefix}Food"...)` sequence. This is largely inherent to a switch-per-event-type fact mapper (most branches necessarily differ, since each event type carries different fields) — flagged for completeness per the "keep looking" instruction and to close out Report 2's D11 item, but it's genuinely low-priority: extracting a 2-call-site, 3-line helper has marginal payoff compared to G1/G2.

**Recommendation.** Optional: a private `SetVitalsFacts(FactBuilder b, string prefix, Position pos, double health, double food, FactSource source)` helper, called from both branches. Low priority — do opportunistically if touching this method for other reasons, not worth a standalone PR.

**Confidence: 80%.**

---

## Assumptions & Open Questions

1. **G1's fix assumes `Agent.Planning` already has a project reference to `Agent.Core`** sufficient to access `CommonMinecraftBlocks`'s public fields — this was confirmed by other existing `Agent.Core` type usages already present in `HtnTaskLibrary.cs` (e.g. its use of `WorldState`, `ActionFactory` from `Agent.Core`), so no new project reference should be needed, but worth a sanity-check build after the change given no `dotnet build` was possible in this sandbox (see prior reports' network-limitation note).
2. **G3's recommendation is intentionally conservative** (convention going forward, not a mass re-file) given the size of the change a full reorganization would represent (61 files) relative to its payoff (findability, not correctness) — a full migration is a reasonable team call to make separately, not something this report is asserting as necessary.
3. **This closes out all outstanding jscpd-flagged, previously-unverified items from Report 2's D11** except `GoalFactory.cs`'s self-clone, which Report 2 already characterized as a likely-defensible nullable-overload pair and is not re-litigated here.
4. **Test-suite quality assessment (the "not shallow" claim in the Executive Summary) is based on a sample**, not all 806 tests individually read — reasonable confidence given the consistent style observed across the files sampled, but not an exhaustive per-test audit.

---

## Suggested Sequencing (additive to prior reports)

1. G1 (delete duplicate dictionaries, reference `CommonMinecraftBlocks`) — mechanical, highest-confidence, do first; pairs naturally with Report 2's D6 (coal-fuel-math extraction) since both touch `HtnTaskLibrary.cs`'s relationship to shared Minecraft-domain data — consider one combined PR.
2. G2 (extract shared block-placement filter/tag loop) — mechanical, same file, do alongside G1 while already in `HtnTaskLibrary.cs`.
3. G4 — opportunistic, bundle with any other `WorldStateProjector.cs` work.
4. G3 — process/convention change, not a PR; adopt going forward, re-file opportunistically.
