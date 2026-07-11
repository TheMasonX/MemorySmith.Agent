# MemorySmith.Agent — Sprint 59 Deep-Dive Audit
**Scope:** `dev/round-3` @ `eceb01e` (Sprint 59 Wave A: prediction pipeline) + in-progress backlog (TSK-0004, TSK-0166, TSK-0337/0339/0340/0341/0343)
**Method:** Full clone via `codeload.github.com` tarball, line-by-line review of all C#/JS source (≈22.4k LOC prod, 14.2k LOC tests), cross-referenced against `Data/Tasks/*.json`, `council-review-consolidated-audits-7-5-26.md`, and 40+ prior audit docs to avoid duplication.
**Confidence key:** based on direct code citation + call-graph verification (not inference), unless noted.

---

## Executive Summary

Sprint 59 Wave A just landed 4 fixes (TSK-0336, 0338, 0342, 0344) closing F1/F6/F9/F11/F31 from the council synthesis. All four are **structurally correct in isolation** — verified line-by-line, no regressions in the mechanisms they touch.

However, **the Wave A changes interact with a pre-existing, previously-undetected dead-code path to produce a new, high-confidence functional bug**: the entire "structured effects" producer (`ActionOutcome.Effects`) that `WorldStateDiff`/`TSK-0344` and `LlmEvaluatorImpl`/`TSK-0320` are built to consume **is never populated by any production code path**. Every action's expected-outcome prediction therefore falls through 100% of the time to `WorldModel.Predict`'s rule-based fallback — and two of that fallback's rules (`PredictPlace`, `PredictSmelt`) don't model inventory change at all, and a third (`PredictCraft`) only models half of it. The practical effect: **every successful `PlaceBlock` and `SmeltItem` action, and every `CraftItem` action with real ingredient cost, will now be flagged as an "unexpected inventory change"** by the very code shipped today — the opposite of Sprint 59's stated goal ("evaluator checks world diff" without spurious noise on success).

This was not caught because F1 (tool-name normalization) and F31 (unexpected-change detection) each look correct read in isolation against their own unit tests; the bug only appears when tracing the full data flow from `ToolDispatcher.MapResultToOutcome` → `ActionOutcome.Effects` → `ComputeWorldStateDiff` → `WorldStateDiff.HasUnexpectedChanges`, which no existing audit document appears to have done end-to-end.

Secondary findings: two in-progress backlog items (TSK-0004 MoveTo context-carry, TSK-0166 Mineflayer modularization) have task descriptions/status that no longer match code reality — one is more done than tracked, the other is actively regressing (file growing, not shrinking) while marked "InProgress."

**Net new findings in this pass: 8** (2 Critical, 1 High, 3 Medium, 2 Low/process). Zero duplicate re-reports of prior audit findings — see "Explicitly Not Re-reported" section.

| # | Severity | Finding | Confidence |
|---|----------|---------|------------|
| 1 | **Critical** | `ActionOutcome.Effects` is never populated in production — the entire "primary" prediction path in `ComputeWorldStateDiff` is dead code | 95% |
| 2 | **Critical** | `WorldModel.PredictPlace`/`PredictSmelt` model zero inventory change → combined with #1, every real Place/Smelt now false-triggers `HasUnexpectedChanges` | 93% |
| 3 | High | `WorldModel.PredictCraft` never models consumed ingredients → every real craft with ingredient cost false-triggers the same flag | 88% |
| 4 | Medium | Stale/lying doc-comment in `HtnTaskLibrary.GatherItemDecompose` (`SearchMemory → MineBlock → GetStatus`) — neither action is actually emitted | 97% |
| 5 | Medium | TSK-0004 task description is stale — schema/context-carry pieces it lists as blocked are already implemented; only the decomposer wiring (finding #4) remains | 90% |
| 6 | Medium | TSK-0166 modularization is net-regressing: `index.js` grew from ~1867→2084 lines while "InProgress"; the one function needing extraction most (`dispatch`, 1458 lines / 70% of file) is untouched | 96% |
| 7 | Low/Process | TSK-0338 marked "Done" without adding its own recommended DI-container regression test | 92% |
| 8 | Low/Process | Council doc's task-ID→finding mapping table doesn't match the IDs actually assigned to created tasks | 85% |

---

## Finding 1 — `ActionOutcome.Effects` producer is dead code (Critical, 95%)

**Evidence chain:**
- `Agent.Core/Models/ActionOutcome.cs:109-137` — six factory methods (`Failed`, `Succeeded`, `NoProgress`, `Blocked`, `Unreachable`, `TimedOut`) all pass `[]` for `Effects`. Only `Collected(...)` (line 109-113) populates `Effects`, with a single `StructuredEffect("ItemCollected", ...)`.
- `Agent.Tools/ToolDispatcher.cs:224-241` (`MapResultToOutcome`) — the **only** place `ActionOutcome` is constructed from a live tool call — exhaustively switches over `OutcomeType` and calls only the six empty-`Effects` factories. `ActionOutcome.Collected` is never referenced here or anywhere else in `WebUI.Blazor/*` or `Agent.*`.
- Repo-wide grep confirms `ActionOutcome.Collected(` has exactly one call site in the whole codebase: `MemorySmith.Agent.Tests/Sprint35Tests.cs:391`, a unit test that constructs it directly to assert its own shape. No production caller exists.
- `WebUI.Blazor/AgentBackgroundService.cs:3240-3253` (`ComputeWorldStateDiff`, "Primary" branch) iterates `outcome.Effects` to build `expectedGained`/`expectedLost`. Since `Effects` is always `[]` in practice, this loop is a no-op on every call, and control always falls through to the "Fallback: use WorldModel.Predict" branch (line 3255).

**Why this matters:** the code comments at lines 218, 2187, and 3235 all describe the fallback as covering only "fire-and-forget tools (MineBlock, PlaceBlock, Wander)" — implying most tools use the structured-effects primary path. That's incorrect as written today: **100% of tools, 100% of the time, use the fallback.** This is a "Producer → ∅" pattern (per the council's Data Model Architect seat) but in the opposite direction from the ones already tracked (F1/F2/F3/F4): here the *consumer* (`ComputeWorldStateDiff`) has correct code but its *producer* silently never fires, so the bug is invisible to any test that mocks `ActionOutcome` directly (as `Sprint35Tests.cs:388-395` does) instead of exercising the real `ToolDispatcher` → `ActionOutcome` path.

**Recommendation:**
1. Either (a) wire `ToolDispatcher`/tool implementations to actually call `ActionOutcome.Collected`/new `Consumed`/`Placed` factories with real item/count data where known at dispatch time, or (b) delete the dead "Primary" branch in `ComputeWorldStateDiff` and the `Collected` factory, and document that `WorldModel.Predict` is the sole source of expected-outcome data. Given `Predict` already has full tool coverage post-TSK-0336, **(b) is the lower-risk, less-code path** — but only after fixing Findings 2 and 3 below, or the fallback inherits the same defects.
2. Add an integration test that dispatches a real `PlaceBlockTool`/`FurnaceTool` call through `ToolDispatcher.CallWithOutcomeAsync` (not a hand-built `ActionOutcome`) and asserts `outcome.Effects` — this is the exact gap that let this go undetected for (per the comments) at least two sprints of "Sprint 58 (TSK-0309)" work built on top of it.

---

## Finding 2 — `PredictPlace`/`PredictSmelt` model zero inventory change (Critical, 93%)

**Evidence:** `Agent.Core/Models/WorldModel.cs:188-194`:
```csharp
private static PredictionState PredictPlace(BeliefState b, IReadOnlyDictionary<string, object?> args) =>
    new("place", args, b.Position, b.Health, b.Food, b.Inventory,
        0.90, "Place block — inventory unchanged (consumed by action)");

private static PredictionState PredictSmelt(BeliefState b, IReadOnlyDictionary<string, object?> args) =>
    new("smelt", args, b.Position, b.Health, b.Food, b.Inventory,
        0.80, "Smelt — outcome depends on furnace state");
```
`PredictPlace`'s own summary string says "consumed by action" but the returned `PredictedInventory` is `b.Inventory` — byte-for-byte unchanged, no decrement of the placed `material` argument. `PredictSmelt` doesn't touch input or output items at all. Both are lying comments — the same pattern previously fixed under TSK-0126 ("Fix Lying Comment in GatherItemDecompose"), recurring here in a file none of the prior 40+ audits appear to have scrutinized at this line level.

**Combined effect with Finding 1:** since Finding 1 means these two functions are the *only* source of "expected" inventory change for `PlaceBlock`/`SmeltItem` (confirmed: `PlaceBlockTool.cs` and `FurnaceTool.cs` are both pure fire-and-forget dispatchers with zero `ToolResult.Data`/effects), the pipeline now works out to:

| Action | Expected (predicted) delta | Actual delta (real gameplay) | `HasUnexpectedChanges` |
|---|---|---|---|
| `PlaceBlock(cobblestone)` | none | `cobblestone: -1` | **true** (false positive) |
| `SmeltItem(iron_ore)` | none | `iron_ore: -1, iron_ingot: +1` | **true** (false positive) |

This directly undermines `TSK-0320`'s fast-path preservation goal (G1.2: "returns replan... despite all outcomes succeeding") and `TSK-0344`'s stated purpose (detect genuine anomalies like mob drops) — as shipped, it will fire on **every** successful build/smelt action instead of only genuine anomalies. Net effect is not runaway auto-replanning (confirmed: `LlmEvaluatorImpl`'s fast-path-2 only *skips the LLM call*; `HasMismatch` doesn't force `ShouldReplan=true` directly — the LLM still decides), but it does mean the LLM evaluator will now be invoked on effectively every dispatch cycle involving a build or smelt goal once ≥3 outcomes accumulate, burning latency/cost and injecting noisy "Unexpected inventory: cobblestone -1" text into every build-goal evaluation prompt — exactly the kind of signal-to-noise degradation the council's Retrieval Specialist seat flagged as a general risk pattern, but not previously identified at this specific site.

**Recommendation:**
1. `PredictPlace`: read the `material`/`block` arg (available in `args`) and decrement it by 1 (or by `count` if present) in `PredictedInventory`.
2. `PredictSmelt`: read `item`/`count` and decrement the input; look up the smelt output via `SmeltableMapping` (already exists per `Agent.Planning/SmeltableMapping.cs` — extracted under TSK-0082 specifically to avoid this kind of drift) and increment the output.
3. Add unit tests asserting `Predict("PlaceBlock", {material: "cobblestone"}).PredictedInventory["cobblestone"] == before - 1`, and equivalent for smelt input/output.
4. This should be sequenced **before** any further work is layered on `WorldStateDiff`/`TSK-0344` consumers (e.g., surfacing it in dashboards) — right now it would just increase visible noise.

---

## Finding 3 — `PredictCraft` never models ingredient consumption (High, 88%)

**Evidence:** `Agent.Core/Models/WorldModel.cs:176-186`:
```csharp
private static PredictionState PredictCraft(BeliefState b, IReadOnlyDictionary<string, object?> args)
{
    var item = GetStrArg(args, "item");
    var count = GetIntArg(args, "count", 1);
    var newInv = new Dictionary<string, int>(b.Inventory);
    newInv[item] = newInv.GetValueOrDefault(item) + count;
    ...
}
```
Only the *output* item is added; no ingredient (planks, sticks, coal, etc.) is ever subtracted. Same mechanism as Finding 2: `CraftItemTool` is fire-and-forget with no `Effects`, so this is the sole prediction source. Any craft that consumes a real ingredient (which is nearly all of them — torches, tools, tables) will show the ingredient's real decrease as "unexpected."

**Confidence rationale for 88% (vs. 93%/95% above):** slightly lower because it's plausible some ingredient consumption is small enough / same-tick enough with other actions that it's absorbed into a batch where the item also happens to appear in `InventoryLost` from a different code path — I did not find evidence of such a path, but did not exhaustively trace every goal type's call into `ComputeWorldStateDiff`'s `expectedLost` population (which, per Finding 1, is always empty from the `Effects` side regardless).

**Recommendation:** Extend `PredictCraft` to accept a recipe-cost lookup (a natural extension point once `HtnTaskLibrary`'s existing recipe constants / `SmeltableMapping`-style tables are consulted) and subtract ingredient counts. Bundle with Finding 2's fix as a single "WorldModel.Predict inventory-accuracy" task — they share root cause and test infrastructure.

---

## Finding 4 — Stale/lying doc-comment in `GatherItemDecompose` (Medium, 97%)

**Evidence:** `Agent.Planning/HtnTaskLibrary.cs:704-716` (doc comment above `GatherItemDecompose`):
> "Default plan: SearchMemory → MineBlock → GetStatus (3 actions)."

Actual implementation (`HtnTaskLibrary.cs:717-787`) builds `actions` starting empty (line 732-734), optionally adds `Wander` (line 774, only when `BlockNotFound`), then adds `MineBlock` per source block (line 777-778), and explicitly does **not** add `GetStatus` — confirmed by the comment immediately below it citing **TSK-0080 / Sprint 38 P0-A**: "GetStatus removed — inventory truth now comes from ItemCollectedEvent." No `SearchMemory` action appears anywhere in this method. The doc comment describes a 3-action plan that has not existed since at least Sprint 38, and never included `SearchMemory` at all despite the claim.

This is the same defect class as `TSK-0126` ("Fix Lying Comment in GatherItemDecompose... + Resolve _agentRuntime Dead Code") — that task closed a *different* stale comment in the same method; this one was apparently introduced or left behind afterward and is a fresh instance, not a re-open.

**Recommendation:** Rewrite the comment to describe the actual plan (`[Wander?] → MineBlock × N`), and treat "wire SearchMemory into GatherWoodDecompose" as its own explicit, trackable sub-item of TSK-0004 (see Finding 5) rather than an implied-but-undelivered behavior.

---

## Finding 5 — TSK-0004 task description is stale relative to code (Medium, 90%)

TSK-0004 ("Wire MoveToTool to read coordinates from ActionData.Context") lists 5 "Remaining items (Sprint 51+)." Cross-checked against current code:

| Remaining item (per task doc) | Actual status |
|---|---|
| "Fix context merge... schema rejects undeclared properties" | **Done.** `Agent.Tools/Tools/MoveToTool.cs:19-24` schema explicitly declares `nearestX/Y/Z` as accepted properties — this was the exact fix the item calls for. |
| "Update SearchMemoryTool to emit... nearestX/Y/Z" | **Done.** `Agent.Tools/Tools/SearchMemoryTool.cs:120-128` emits all three via regex-based coordinate extraction (two patterns, Sprint 51). |
| "Update MoveToTool.ExecuteAsync: if Arguments has x/y/z, use those; else check Context[...]" | **Done.** `MoveToTool.cs:33-45` implements exactly this priority order. |
| "Add ContextCarryTests..." | Not verified in this pass (test suite not exhaustively cross-checked here) — flagged as open, not confirmed done or missing. |
| "Update HtnTaskLibrary.GatherWoodDecompose: add SearchMemory → MoveTo flow" | **Not done** — confirmed via Finding 4: no `SearchMemory` or `MoveTo` action is ever emitted by `GatherItemDecompose`. |

**Why this matters:** the task is correctly marked `InProgress`, but 3 of its 5 listed blockers are stale — someone reading the task fresh would think more work remains on the tool/schema layer than actually does, and could duplicate already-done work, or (worse) miss that the *only* real remaining code change is the decomposer wiring itself.

**Recommendation:** Update TSK-0004's description to strike the 3 completed items, keep the test-coverage question open pending verification, and narrow the task to exactly: (a) confirm/add `ContextCarryTests`, (b) wire `SearchMemory → MoveTo → MineBlock` into `GatherItemDecompose` (or a sibling decomposer) with a fallback to direct-mine when the memory search returns nothing, consistent with the existing progressive-wander fallback pattern already in the same method (lines 736-775).

---

## Finding 6 — TSK-0166 (Mineflayer modularization) is regressing, not progressing (Medium, 96%)

**Evidence:**
- TSK-0166's description states the baseline as "~1500 lines" (its own audit citation) needing to be split into ~20 focused modules (`config.js`, `logging.js`, `bot-factory.js`, `ws-server.js`, `events/*`, `actions/*`, `world/*`).
- Current `MineflayerAdapter/index.js` line count: **2084 lines** — larger than the cited baseline, despite the task being marked `InProgress`.
- Extraction that *has* happened: `config.js` (94 ln), `logger.js` (38 ln), `movements.js` (50 ln), `vec3.js` (231 ln), `stopState.js` (45 ln), `gameModeState.js` (37 ln), `creativeProvider.js` (133 ln) — 628 lines total, matching the task's own prescribed order-of-operations step 1 ("lowest-risk split: config.js and logging.js... pure functions, no bot dependency").
- What has **not** happened: steps 2–5 of the task's own plan (`ws-server.js`, then per-action modules under `actions/`, then event listeners). Confirmed via `grep` of top-level function boundaries in `index.js`: a single function `dispatch({ action, arguments, correlationId })` spans **lines 619–2077** — **1458 lines, ~70% of the entire file** — and contains every action handler (move, mine, place, craft, smelt, wander, findFlatArea, stop, etc.) inline in one switch/if-chain, exactly the anti-pattern the task exists to fix.

**Why this matters:** the "no legacy systems, no technical debt" project philosophy (per `AGENTS.md`/project memory) is being actively violated on this file — every sprint that adds a new Mineflayer capability (recent additions: creative-mode fixes, hazard checks, armor/eat actions per TSK-0267/0268 in the backlog) grows `dispatch()` further, while the modularization task sits `InProgress` extracting only the parts that were never the actual pain point. The task's own risk-ordering (config/logging first, low risk) was followed, but stalled before reaching the high-value, high-risk `dispatch()` extraction — and nothing is currently stopping `dispatch()` from continuing to grow in the interim.

**Recommendation:**
1. Re-baseline TSK-0166's description to reflect current (2084-line) state and explicitly call out that `dispatch()` (619-2077) is the single highest-value remaining extraction target — not a general "continue modularizing" task.
2. Consider a stopgap CI/lint guard (e.g., max-function-length or max-file-length check scoped to `MineflayerAdapter/index.js`) to prevent further growth of `dispatch()` while the extraction is pending — cheap, directly enforces the "no more technical debt" principle the project has stated, and gives objective forward progress signal instead of relying on periodic audits to notice regression.
3. Sequence extraction by action type (`move.js`, `mine.js`, `place.js`, `craft.js`, `smelt.js`, `wander.js`) as the task's own plan specifies — each is a self-contained `case`/`if` branch in `dispatch()` today and should lift out cleanly.

---

## Finding 7 — TSK-0338 closed without its own required regression test (Low/Process, 92%)

TSK-0338's own "Recommendation" section (embedded in `Data/Tasks/tsk-0338-...json`) lists three required actions:
1. Consolidate to a single constructor — **done**, verified in `HtnTaskLibrary.cs:35-53`.
2. "Add a regression test that resolves `HtnTaskLibrary` through an actual `ServiceCollection`/`ServiceProvider` (not `new HtnTaskLibrary()`) and asserts `HasTask("GatherWood")` is true" — **not found.** Repo-wide search for `ServiceProvider`/`ServiceCollection` combined with `HtnTaskLibrary` in the test project returns no matches.
3. "Verify DI resolution matches expected behavior" — no evidence of a DI-container-based verification distinct from #2.

The task's closing comment claims "dotnet build succeeds... dotnet test passes 815/815 tests" as validation, but that's necessarily true regardless of whether the specific regression test was added, since the underlying bug is now structurally impossible (only one constructor exists) — 815/815 passing does not confirm the *originally identified failure mode* (DI selecting the wrong constructor) is guarded against for the future, only that current tests don't regress.

**Why this matters is limited but non-zero:** the specific DI-selection hazard is now moot (single constructor), so this is not exploitable today. But it's a second data point (alongside Finding 6) of tasks being marked `Done`/making progress claims that don't fully match their own stated acceptance bar — worth a lightweight process fix given the project's stated goal of avoiding exactly this kind of drift (per `AGENTS.md` and the Human Learning Advocate council seat's "process changes recommended: build-time DI assertion, wire-name contract test").

**Recommendation:** Add the ServiceProvider-based resolution test as a small follow-up (low effort, ~10 lines) — not because the current code is at risk, but because it's the kind of regression guard that prevents a *future* refactor (e.g., someone reintroducing a second constructor) from reintroducing this exact bug silently.

---

## Finding 8 — Council doc task-ID↔finding mapping doesn't match created tasks (Low/Process, 85%)

`Data/Pages/Audits/council-review-consolidated-audits-7-5-26.md`, "New Tasks to Create" table (lines 158-170), maps:
- TSK-0336→F1, TSK-0337→F6, TSK-0338→F7, TSK-0339→F8, TSK-0340→F4, TSK-0341→F19, TSK-0342→F29, TSK-0343→F31, TSK-0344→F33.

Actual created tasks (`Data/Tasks/tsk-0336..0344*.json`, confirmed by filename+content):
- tsk-0336 = F1 (matches) · tsk-0337 = **F7/F16** (WebSocketBridge, table said F6) · tsk-0338 = **F6** (HtnTaskLibrary DI, table said F7) · tsk-0339 = **F8/F18** (Node reconnect, table said F8 — matches) · tsk-0340 = **F4** (matches) · tsk-0341 = **F29** (PlaceBlockGoalDecomposer, table said F19) · tsk-0342 = **F9/F11** (ReplanGovernor, table said F29) · tsk-0343 = **F33** (EntityObservedEvent, table said F31) · tsk-0344 = **F31** (WorldStateDiff, table said F33).

Net effect: IDs 0337/0338 are swapped relative to the table, and 0341/0342/0343/0344 are each shifted by one relative to the table's F19/F29/F31/F33 sequence. F19 (creative CancellationToken, ruled P0 by the Synthesizer) doesn't appear to have a task at all in this range — worth confirming it wasn't dropped in the ID-shuffle.

**Recommendation:** Low priority, but since Lucas's audit workflow explicitly deduplicates against this table, a quick pass reconciling the table to the actual `Data/Tasks/*.json` IDs (or vice versa) will prevent a future audit from either re-flagging an already-tracked finding under the wrong ID, or — more importantly — assuming F19 is tracked when it may not be. **Recommend explicitly confirming F19 (creative provisioning `CancellationToken.None`) has a live task**; TSK-0331 ("Replace CancellationToken.None in creative provisioning with linked CTS") appears to be the actual implementation and is marked Done, so F19 is very likely covered — just not traceable through this table without independent verification, which is the process gap itself.

---

## Explicitly Not Re-reported

To honor the no-duplication requirement, the following were checked and confirmed already correctly tracked/handled — not repeated here:
- `PlaceBlockGoal._dispatched` race (P0-1) — confirmed fixed by TSK-0330 (`Interlocked` usage present).
- `AliasRegistry` static-dict liveness nuance (F12) — confirmed as described (only `TryResolve`/`Search` dead, dictionaries themselves live).
- WebSocketBridge/Node reconnect backoff gaps (F7/F8/F16/F18) — left as-is per existing TSK-0337/0339 (Backlog); spot-checked `WebSocketBridge.cs` structure only far enough to confirm the task's file/line references still resolve, did not re-derive the fix.
- `EvaluationResult.Diagnosis` stub (F4/TSK-0340), `PlaceBlockGoalDecomposer` same-coordinate issue (F29/TSK-0341), `EntityObservedEvent`→`StructuredFacts` wiring (F33/TSK-0343) — all Backlog, descriptions read and consistent with current code; no new information to add beyond what's already in each task file.
- Bare/generic catch-block sweep (5 bare, 38 `catch (Exception)`) — all either have prior `TSK-01xx`/`TSK-03xx` history of deliberate "best-effort" annotation or already log via `ILogger`; none found unlogged/unlabeled beyond what prior audits catalogued.

---

## Assumptions & Open Questions

1. **Assumption:** `WorldState.Inventory` (used as the "actual" side in `ComputeWorldStateDiff`) is itself accurate at the time of diffing — i.e., Findings 1–3 are about the *expected* side being wrong, not the *actual* side. Not independently re-verified against `WorldStateProjector.ApplySmeltComplete`/`ApplyItemCollected` in this pass (those were audited and fixed under TSK-0084/TSK-0096/TSK-0108 previously); recommend a quick smoke-test alongside the Finding 2/3 fix to confirm both sides move together for a real Place→Smelt→Craft sequence.
2. **Open question:** does `MinOutcomesBeforeEval = 3` (LlmEvaluatorImpl.cs:36) combined with Findings 1–3 mean a pure-building goal (all `PlaceBlock`) will invoke the LLM evaluator on *every* dispatch cycle once 3 blocks have been placed, for the entire remainder of the build? If so the cost/latency impact is larger than "occasional noise" — recommend instrumenting actual evaluator call frequency during the next live build test to quantify before deciding fix priority for Finding 2.
3. **Assumption:** the `ContextCarryTests` referenced in TSK-0004 either exist under a different name or genuinely don't exist — not resolved with certainty in this pass (grep for the literal name found nothing, but test naming conventions in this repo are inconsistent — e.g. `Sprint35Tests.cs`, `ToolDispatchTests.cs` — so a differently-named equivalent may exist).
4. **Scope boundary:** per-file review depth was uneven by design — the 5 files touched by the Sprint 59 commit received full line-by-line scrutiny plus call-graph tracing; adjacent in-progress items (TSK-0004, TSK-0166) received targeted verification of their specific claims; the remaining ~150 source files were covered by the repo-wide pattern sweep (bare catches, `.Result`/`.Wait()`, `CancellationToken.None`, `Thread.Sleep`, `TODO`/`FIXME`) rather than full manual line review, since 40+ prior audits already cover general architecture/dead-code/security ground and re-doing that from scratch would either duplicate or require re-verifying every prior finding against current HEAD, which was out of scope for a "currently in progress areas" focus.

---

## Recommended Sequencing

1. **Immediate (blocks nothing else, ~30 min):** Fix Finding 4 (comment) — trivial, zero risk.
2. **Immediate (before any further WorldStateDiff/dashboard work builds on TSK-0344):** Findings 1+2+3 as a single "WorldModel.Predict inventory accuracy" task — fix `PredictPlace`/`PredictSmelt`/`PredictCraft` to model real inventory deltas using existing `SmeltableMapping`/tool-argument data; either wire `ActionOutcome.Effects` for real or delete the dead branch (Finding 1's option b). Suggest new task, e.g. `TSK-0346`.
3. **Next sprint planning pass:** Findings 5, 6 — update TSK-0004 and TSK-0166 descriptions to match reality; for TSK-0166 specifically, consider the file-length CI guard as a cheap forward-progress enforcement mechanism.
4. **Low-priority cleanup, batchable with other doc work:** Findings 7, 8.
