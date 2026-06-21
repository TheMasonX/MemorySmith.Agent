# MemorySmith.Agent Code Audit Report
**Scope:** PR #1 / `sprint-5-tool-safety` branch snapshot, with emphasis on the current sprint tasks and the next sprint's likely implementation surface.  
**Repo:** `themasonx/memorysmith.agent`  
**Method:** Code-path review of sprint-relevant task docs, planner / goal factory / gather flow, and repository policy guidance.

## Executive summary

This branch is moving in a strong direction architecturally: the planner is becoming goal-type aware, item gathering is being generalized, and the codebase now has the beginnings of an item registry / goal abstraction layer. The main risks are not missing features so much as **wiring correctness**, **scope drift**, and **maintainability debt** introduced by hardcoded planner behavior.

The most important correctness issue is that the **generic gather planner path appears to ignore the requested item count**. `GenericGatherGoal` explicitly exposes `TargetCount` to avoid the old “mine 10 when asked for 1 dirt” behavior, but `HtnPlanner` passes an empty parameter list into the gather decomposition, and the decomposition defaults to 10 when no count is provided. That is a likely user-visible regression and should be treated as high priority. citeturn179762view2turn824331view0turn947641view0

A second high-risk issue is that the gather decomposition only examines the **first two** source blocks. That is brittle even in vanilla, and it conflicts with the current sprint’s explicit goal of “arbitrary item gathering including mods,” where source sets are expected to be broader and less predictable. citeturn742217view0turn947641view0

A third major theme is **policy mismatch**: the repo’s own engineering guidance says tunable constants should be named and centralized, yet the planner contains many hardcoded distances, counts, and thresholds. That will make the next sprint harder to tune, harder to test, and easier to regress. citeturn635246view0turn947641view0

## Highest-priority findings

### 1) Gather count is likely lost in planning
**Severity:** High  
**Confidence:** 97%

`GenericGatherGoal` has a `TargetCount` property specifically because the earlier implementation could over-gather. The planner path for `IItemSpecGoal` currently calls `DecomposeGatherItem` with an empty parameter array, and the decomposition defaults to `10` when no explicit count is present. That means the planner can silently ignore the requested quantity. citeturn179762view2turn824331view0turn947641view0

**Impact:** Players/tasks asking for one unit may get a ten-unit plan. That is a correctness bug, not just a tuning issue.

**Recommended fix:** Thread `TargetCount` (or equivalent) through the planner into `GatherItemDecompose`, and add a regression test proving that `count=1` plans for exactly one when inventory is empty.

---

### 2) Generic gather only considers two source blocks
**Severity:** High  
**Confidence:** 90%

The gather decomposer only iterates over `spec.SourceBlocks.Take(2)`. That can miss valid sources and is especially risky for modded content, where an item may have many valid source variants or non-obvious generation paths. The task doc explicitly calls out mod support as a design goal, so this limit is a mismatch with the intended scope. citeturn742217view0turn947641view0

**Impact:** False negatives, incomplete plans, and inconsistent behavior across item types and mods.

**Recommended fix:** Replace the hard cap with a scored or ordered source-resolution strategy. At minimum, preserve all sources from the registry/spec and let the planner rank them rather than truncating the set.

---

### 3) Hardcoded planner numbers violate repo policy and reduce tunability
**Severity:** Medium-High  
**Confidence:** 95%

The repository guidance says to avoid magic numbers for timeouts, retries, radii, and similar tunables. The planner and decomposition code still contains raw values for search radius, wander distance, flat-area thresholds, and similar control parameters. That is a maintainability smell and a likely source of future churn. citeturn635246view0turn947641view0

**Impact:** Harder balancing, harder review, higher regression risk, and unnecessary coupling between behavior and implementation details.

**Recommended fix:** Move all planner tunables into named constants or config objects, ideally grouped by domain (gather, build, craft, navigation, fallback).

---

### 4) Generic gather design is still too vanilla-biased for “arbitrary items / mods”
**Severity:** Medium-High  
**Confidence:** 88%

The current `ItemSpec` abstraction is compact and useful, but it still centers on a relatively small vanilla-style model: `ItemId`, `DisplayName`, `SourceBlocks`, `RequiresSmelting`, and `MinHarvestLevel`. The task doc explicitly notes that mod item IDs may be unknown at compile time and may need MemorySmith wiki pages or LLM resolution. That gap is still architectural, not just a missing code path. citeturn836891view0turn742217view0

**Impact:** The sprint may appear complete for vanilla items while still failing on the “arbitrary” and “mods” part of the requirement.

**Recommended fix:** Add a stronger resolution layer between user intent and item specs: canonical item identity, alias resolution, confidence scoring, and an explicit fallback path for unknown or partially-known mod items.

---

### 5) Replan behavior drops context and hides failures
**Severity:** Medium  
**Confidence:** 84%

`ReplanAsync` rebuilds a synthetic `SimpleGoal` from prior phases and then calls `PlanAsync`; any exception is swallowed and converted to `null`. That makes recovery weaker than it needs to be and makes diagnosis more difficult because root causes are flattened away. citeturn824331view0

**Impact:** Brittle recovery, silent planning failures, and poorer telemetry for future debugging.

**Recommended fix:** Preserve structured error reasons, keep the original goal context, and only fall back to `null` when the caller explicitly chooses that behavior.

---

### 6) GoalFactory has a sync/async asymmetry that may be an integration footgun
**Severity:** Medium  
**Confidence:** 72%

The factory clearly supports dynamic goal creation for `GatherItem:`, `Build:`, and `CraftItem:` through the async path, but the synchronous `Create` path only covers the static registry. That is fine if the codebase always uses `CreateAsync` for dynamic goals, but it is a potential trap if any caller assumes parity between the two APIs. citeturn179762view6turn179762view7turn179762view8turn920339view1

**Impact:** Inconsistent behavior across call sites, especially if any older or test-only path still uses the sync method.

**Recommended fix:** Either make the async path the only supported path for dynamic goals, or unify the public API so the sync method cannot silently miss the dynamic registry logic.

## Architecture and codebase-health assessment

The branch shows a healthy move toward stronger domain modeling. The separation between `GoalFactory`, `GenericGatherGoal`, `BuildGoal`, and planner decomposition is a good foundation, and the branch’s work is clearly trying to make the agent more extensible rather than more monolithic. citeturn759886view2turn391666view4turn179762view2

The remaining architectural risk is that the planner still behaves like a set of **embedded heuristics** rather than a fully composable planning system. Hardcoded search patterns, direct “mine/wander/search memory” sequences, and count/default behavior hidden in decomposition functions all make the system harder to evolve. That is the exact kind of place where architecture drift shows up: the API looks generic, but the actual behavior is still specialized. citeturn947641view0turn824331view0

From a codebase-health standpoint, the strongest improvement would be to treat planner tuning and source-resolution as first-class subsystems. The current sprint task already hints at that with `ItemRegistry` / MemorySmith page resolution; the next step is to make those subsystems explicit enough that the planner is consuming data, not encoding policy. citeturn742217view0turn836891view0

## Evidence-backed implementation notes

### Generic gather flow
- `GenericGatherGoal` tracks the target count and uses inventory freshness guards to avoid stale completion checks. citeturn179762view2turn391666view0
- `HtnPlanner` delegates `IItemSpecGoal` handling into the gather decomposer. citeturn824331view0
- `GatherItemDecompose` currently defaults the count to 10 when no parameter is passed, searches memory using a simplified query, then wanders and mines using fixed parameters. citeturn947641view0
- The source list is truncated to two candidates, which is too small for the stated “arbitrary items / mods” direction. citeturn947641view0turn742217view0

### Item and goal abstraction
- `ItemSpec` is intentionally compact and explicitly defers legacy block remapping to a later phase. That is a reasonable scope choice, but it is also an explicit open boundary. citeturn836891view0
- `GoalFactory` has moved toward dynamic goal creation using async registry lookups, which is good architecture for extensibility. The remaining issue is consistency across sync and async paths. citeturn179762view6turn179762view7turn179762view8turn920339view1

### Repository policy alignment
- The repo guidance calls for avoiding magic numbers and centralizing tunables. The current planner implementation still violates that principle in several places. citeturn635246view0turn947641view0

## Assumptions

1. This audit is based on the `sprint-5-tool-safety` PR snapshot and the task set visible from the repository and task documents, not on unpublished local changes. citeturn469379view0turn742217view0
2. The async goal-creation path is the intended primary path for dynamic goals. If the codebase still uses the sync factory in production, the sync/async asymmetry becomes more urgent. citeturn179762view6turn920339view1
3. `GatherItemDecompose` is expected to support non-vanilla and modded item resolution as implied by the sprint task. If the real scope is narrower, the source-limit finding becomes less severe but the count-loss bug remains. citeturn742217view0

## Open questions

1. Is any runtime path still calling the synchronous `GoalFactory.Create` for dynamic goals, or is `CreateAsync` the only production entry point? citeturn179762view6turn920339view1
2. Should gather planning preserve all source candidates and rank them dynamically, or is there a deliberate design reason for truncating to two?
3. Is the current `ItemRegistry` already backing mod item resolution elsewhere, or does the sprint still need that subsystem to be implemented?
4. Should replanning preserve the original goal object and failure state, or is the synthetic `SimpleGoal` intentionally discarding context?

## Priority recommendations for the next sprint

1. Fix count propagation in the generic gather path and add a regression test.  
2. Remove the `Take(2)` source truncation or replace it with a rankable selection strategy.  
3. Extract all gather/build/craft planner tunables into named constants or config.  
4. Formalize item resolution for modded content with explicit aliasing / lookup / fallback.  
5. Make replanning and failure handling structured instead of swallowing exceptions.

## Bottom line

This branch is not in bad shape. In fact, the architecture direction is good. The issue is that the most visible generalization work still has a few hidden assumptions from the older implementation, and those assumptions are exactly the kind that become expensive once the system is asked to handle arbitrary items, modded content, and longer autonomous runs. Fix the planner wiring and the tunable-policy debt now, and the next sprint will be much easier to trust. citeturn742217view0turn824331view0turn947641view0turn635246view0
