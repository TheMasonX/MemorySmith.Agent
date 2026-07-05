# MemorySmith.Agent delta audit — new findings only

This report contains only findings that were not covered in the previous audit pass.

## 1) `IntentManagerImpl` disables the chat distance gate in practice

**Severity:** High  
**Confidence:** 96%

`IntentManagerImpl.ProcessChatAsync` always passes `DefaultOnlinePlayers = 1` and `playerPosition: null` into `IChatInterpreter.InterpretAsync`. That forces the interpreter down the “solo / directly addressed” path and removes the distance-based rejection that the chat pipeline depends on for busy servers. In effect, the bot can treat distant ambient chat as addressed chat. fileciteturn75file0turn22file0

**Impact:** false positives in chat interpretation, more unnecessary LLM calls, and more accidental goal execution on multi-player servers.

**Fix:** thread the live online-player count and player position through this manager instead of hardcoding fallback values. If live position is unavailable, make that explicit in telemetry rather than collapsing it to solo mode.

## 2) Mining inventory can double-count on successful pickup

**Severity:** High  
**Confidence:** 94%

`WorldStateProjector.ApplyBlockMined` now increments inventory from `BlockMinedEvent`, and `ApplyItemCollected` also increments inventory from `ItemCollectedEvent`. The Mineflayer adapter emits `blockMined` on every dig and also emits `itemCollected` when pickup succeeds, so a normal successful mine can increment the same item twice. The comments acknowledge possible double-counting, but the current event flow makes that the common path rather than a rare edge case. fileciteturn54file0turn55file0turn63file0

**Impact:** inflated inventory, premature gather completion, and false confidence in resource availability.

**Fix:** choose one authoritative inventory source for mined items. If `itemCollected` is the ground truth, keep `BlockMinedEvent` diagnostic-only. If you need a fallback for dropped items that never get collected, gate the fallback behind a reconciliation rule instead of applying both paths unconditionally.

## 3) Compound-command support is still not wired end-to-end

**Severity:** Medium  
**Confidence:** 82%

`IntentDraft.NextSteps` exists, and the prompt asks the LLM to populate it, but the audited runtime surface does not consume it. `IntentManager.BuildGoalRequest` only maps a single `IntentDraft` to one `GoalRequest`, and `ParseCommandString` handles standalone strings like `craft 20 planks` rather than sequence expansion from `NextSteps`. I did not find the corresponding sequence goal execution path in the audited tree. fileciteturn79file0turn34file0turn24file0

**Impact:** compound commands can look supported in the prompt/data model while still collapsing to a single step at runtime.

**Fix:** either wire `NextSteps` into a real sequence-builder path, or remove the field/prompt language until the sequence executor is present and tested.

## 4) The dashboard reports queued actions as always zero

**Severity:** Medium  
**Confidence:** 91%

`DashboardPublisherImpl.PublishStatusAsync` hardcodes `QueuedActions: 0`. That makes the dashboard’s status payload inaccurate even when the agent has a non-empty queue. The code comment says to wire this later, but the current UI consumers will still see “0” and infer that nothing is pending. fileciteturn67file0

**Impact:** misleading live status, weaker operator debugging, and incorrect “idle vs busy” interpretation.

**Fix:** wire the real `ActionQueue.Count` through the dashboard publisher or omit the field until it can be truthful.

## 5) The new `AgentRuntime` layer is registered but not actually on the running path

**Severity:** Medium  
**Confidence:** 79%

`Program.cs` now registers `AgentRuntime` and the six manager components, but `AgentBackgroundService` is still the active orchestrator and its constructor does not accept `AgentRuntime`. That leaves the new runtime composition layer side-by-side with the legacy host loop instead of replacing it. This is a split-brain architecture risk and a likely source of future drift. fileciteturn72file0turn73file0turn74file0

**Impact:** duplicated orchestration concepts, harder refactors, and a higher chance that fixes land in one path but not the other.

**Fix:** either wire `AgentRuntime` into the host loop now, or remove the registration until the decomposition is ready to take over.

## What I did not repeat

I did not restate the previously covered thread-safety, silent-stop-swallows, HTTP resilience, or version-drift findings. Those remain valid, but they are not part of this delta pass.
