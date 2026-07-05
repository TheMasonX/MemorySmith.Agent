# MemorySmith.Agent delta audit — additional new findings

## 1) Build replan preservation is dropping the build checkpoint namespace

**Severity:** High  
**Confidence:** 95%

`HtnPlanner.ReplanAsync` only preserves action-context keys with prefixes `CraftItem:`, `FindFlatArea:`, `Build:`, and `MoveTo:`. The build pipeline now stores per-block progress under `build:progress:blueprintId` and `build:progress:blockIndex`, and build-origin facts under `build:{blueprintId}:origin:*`. Because the prefix check is case-sensitive and does not include the lowercase `build:` namespace, those keys are not carried across replans. fileciteturn131file0turn134file0

Impact: a replan during construction can lose checkpoint/origin state, which risks duplicate placements and bad resume behavior.

Recommendation: preserve the build namespace explicitly and make the check case-insensitive, or centralize the build keys in one shared list.

## 2) The “place without coordinates” path is not actually using facing

**Severity:** Medium  
**Confidence:** 87%

`PlaceBlockGoal` documents a no-coordinate mode that should place “in front” of the bot. `PlaceBlockGoalDecomposer` instead fills in the bot’s current world position from `state.Position.X/Y/Z` when coordinates are absent. That is a different semantic than facing-aware placement, and no facing vector is threaded through the goal model or decomposer. fileciteturn87file0turn123file0

Impact: the no-coordinate form can place at the bot’s current location rather than in front of it, which is likely to cause collisions or geometry errors.

Recommendation: either add explicit facing/orientation to the goal contract or remove the facing-based promise from the comments/docs until it is implemented.

## 3) Compound chat sequences can silently drop the primary step

**Severity:** High  
**Confidence:** 91%

In the compound-command branch, `HandleChatEventAsync` builds the first goal from `goalRequest`, then appends `IntentDraft.NextSteps`. If the first goal fails to build but a later step succeeds, the code can still emit a sequence or execute a later step alone. That lets the user’s main instruction vanish without an explicit parse failure. fileciteturn99file0

Impact: “do A then B” can degrade into “only do B,” which is a real behavioral bug, not just a missing feature.

Recommendation: treat failure of the first step as a hard stop for the whole sequence and surface it visibly.

## 4) The replan/dispatch context bridge is still brittle and incomplete

**Severity:** Medium  
**Confidence:** 84%

`DispatchActionsAsync` does merge `ToolResult.Data` into a local `planContext`, but `ActionPlan` has no plan-level context field, and the generic LLM fallback creates fresh `ActionData` objects with only `Tool` and `Arguments`. The result is that context preservation only works on hand-authored action paths that manually set `ActionData.Context`; it is not a universal plan concept. fileciteturn116file0turn132file0turn131file0

Impact: search-derived coordinates and similar intermediate results can disappear across replan boundaries or alternate planner paths.

Recommendation: make plan context a first-class model, or make the planner/queue explicitly clone the working context into each queued action.

## 5) There are still bare catches that erase the real failure mode

**Severity:** Medium  
**Confidence:** 90%

`AgentBackgroundService` still has empty catches in a couple of important paths: reconnect cleanup ignores `DisconnectAsync` errors, and the entity-targeting navigation fallback ignores JSON parse failures entirely before falling back to player position. Both are survivable, but they make the fallback reason invisible in logs. fileciteturn92file0turn118file0

Impact: repeated fallback behavior becomes hard to diagnose, and transient adapter/network problems can look like normal behavior.

Recommendation: keep the fallback but log the exception at Debug or Warning with the chosen fallback path.

## Correction

The code does show some context propagation in the dispatch loop, so this is not a total “no context bridge exists” situation. The concrete problem is narrower: the bridge is partial, ad hoc, and easy to break at replan boundaries or when keys do not match the preserve whitelist. fileciteturn116file0turn131file0turn132file0
