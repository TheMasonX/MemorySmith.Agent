# MemorySmith.Agent Delta Audit — New Findings

**Target:** `TheMasonX/MemorySmith.Agent`  
**Branch / commit context:** `dev/round-3`, commit `57b8fdf79ff7ba6f877e0fcac2211934e0f3279c`  
**Scope:** New findings only. This report excludes issues already captured in the prior audits and earlier delta reports.

## 1) GoalFactory does not canonicalize goal suffixes before constructing typed goals
**Severity:** High  
**Confidence:** 88%

`GoalFactory.CreateAsync()` slices the suffix directly from the goal name and only checks whether it is whitespace. It does not trim or normalize the suffix before handing it to typed goals such as `GenericGatherGoal`, `CraftItemGoal`, `SmeltGoal`, or `PlaceBlockGoal`. That makes the goal boundary brittle for minor formatting differences and can lead to item IDs that never match inventory keys. fileciteturn159file0

**Why it matters:** the parser/LLM boundary should canonicalize once. Right now, the factory assumes perfect upstream formatting.

**Fix direction:** trim and normalize suffixes in one shared helper before goal construction.

## 2) `GoalFactory.GetInt` can overflow silently when converting `long` to `int`
**Severity:** Medium  
**Confidence:** 91%

The shared numeric helper accepts `long l => (int)l` without any range check. Large numeric inputs can truncate or wrap into invalid values without an explicit failure. This affects all branches that rely on `GetInt(...)`. fileciteturn159file0

**Why it matters:** the factory is a trust boundary. Silent narrowing conversions can become malformed goals instead of clean rejections.

**Fix direction:** use checked conversion or bounds validation before converting to `int`.

## 3) `PlaceBlockGoal.HasFailed()` ignores the requested block count
**Severity:** Medium  
**Confidence:** 84%

`PlaceBlockGoal.HasFailed()` only checks whether the inventory has at least one of the item. For multi-block placement goals, that can report “not failed” even when the bot has far fewer blocks than requested. That is a mismatch between `Count`, completion semantics, and failure semantics. fileciteturn179file0

**Why it matters:** the planner can keep treating an under-supplied placement goal as viable even though it cannot actually satisfy the request.

**Fix direction:** compare against `_count`, or define failure in terms of feasibility rather than mere presence.

## 4) `WorldState.SetGameMode()` updates the flat fact map but not `StructuredFacts`
**Severity:** Medium  
**Confidence:** 86%

`WorldState.Builder.SetGameMode()` writes `GameMode`, `Facts["world:gamemode"]`, and `UpdatedAt`, but it does not add a structured fact entry. That creates a split-source-of-truth inside the snapshot object: legacy flat facts see the game mode, but newer provenance-tracked consumers do not. fileciteturn145file0

**Why it matters:** the codebase’s own direction is to prefer structured facts, so game mode should follow the same path.

**Fix direction:** route the write through the same structured fact helper used elsewhere.

## 5) `WebSocketBridge` silently drops unknown or malformed event types
**Severity:** Medium  
**Confidence:** 90%

`ParseEvent()` returns `null` for unknown `event` values or missing `event` fields, and only logs when an exception is thrown. That means protocol drift or adapter typos can disappear without any signal. fileciteturn161file0turn162file0

**Why it matters:** the adapter boundary becomes brittle when unrecognized events are ignored silently.

**Fix direction:** log unknown event names at least at Debug level, and consider Warning for missing required properties.

## 6) The LLM providers treat cancellation as a warning-like fallback
**Severity:** Medium  
**Confidence:** 89%

`OllamaProvider`, `GeminiProvider`, and `OpenAICompatibleProvider` all catch `OperationCanceledException`, log a warning, and return `null`. That collapses user-initiated cancellation, shutdown cancellation, and real timeouts into the same “provider failure” path. fileciteturn168file0turn169file0turn170file0

**Why it matters:** cancellation should be cooperative, not treated as an error-like condition. Returning `null` also makes clean shutdowns look like provider problems.

**Fix direction:** distinguish timeout from external cancellation, and avoid warning logs for expected cancellation paths.

## 7) `PlaceBlockTool` advertises `count` but ignores it during execution
**Severity:** Low  
**Confidence:** 83%

`PlaceBlockTool` exposes a `count` field in its schema and description, but the execution path always dispatches one placement action. The response also does not say the tool only placed one block. That makes the tool contract misleading for direct tool use and for LLM planning. fileciteturn173file0

**Why it matters:** the model can request multi-block placement and get a success result even though only one block was placed.

**Fix direction:** either remove `count` from the schema or honor it by dispatching repeated placement actions.

## Practical priority order

1. Canonicalize and validate goal suffixes in `GoalFactory`.  
2. Make numeric conversions checked, not lossy.  
3. Fix `PlaceBlockGoal.HasFailed()` to respect the requested count.  
4. Write game mode into `StructuredFacts` as well as the flat fact map.  
5. Add logging for unknown WebSocket event types.  
6. Separate cancellation from provider failures in all LLM providers.  
7. Decide whether `PlaceBlockTool.count` is real or remove it.
