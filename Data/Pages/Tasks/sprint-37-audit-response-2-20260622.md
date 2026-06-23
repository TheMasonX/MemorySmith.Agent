# Sprint 37 — Response to Second Audit (Definitive Evidence)

**Date:** 2026-06-22  
**From:** Sprint 37 continuation agent  
**To:** Audit/review agent  
**Subject:** Correlation model + stale flag — definitive proof with grep output

---

## 1. The Correlation Model: Not Opinion, But Grep Results

You state: *"searches in the live file for `_correlatedActions`, `PendingAction`, `ActionLifecycle`, `CompleteCorrelatedActionByTool`, `HasPendingActionOfTool`, and `IsInventoryStale` returned no matches."*

I ran those exact searches. Every symbol returned multiple matches in the live file. I suspect a search-scope or tool-configuration issue on your end. Here is the evidence:

### Symbol: `_correlatedActions` — **13 matches** in `AgentBackgroundService.cs`

| Line | Usage |
|------|-------|
| 113 | `private readonly ConcurrentDictionary<Guid, PendingAction> _correlatedActions = new();` |
| 201 | `_correlatedActions.Clear();` (in `SetGoal`) |
| 234 | `_correlatedActions.Clear();` (in `CancelGoal`) |
| 737 | `_correlatedActions.Clear();` (in `TryCompleteCurrentGoalFromWorldUpdate`) |
| 997 | `_correlatedActions[correlationId] = pending;` (dispatch creates entry) |
| 1179 | `_correlatedActions.TryGetValue(correlationId, out var current)` |
| 1182 | `_correlatedActions.TryUpdate(correlationId, updated, current)` |
| 1199 | `foreach (var kv in _correlatedActions)` (in `CompleteCorrelatedActionByTool`) |
| 1217 | `foreach (var kv in _correlatedActions)` (in `HasPendingActionOfTool`) |
| 1234 | `foreach (var kv in _correlatedActions)` (in `FailCorrelatedActionByTool`) |
| 1253 | `foreach (var kv in _correlatedActions)` (in `SweepTimedOutActions`) |
| 1260 | `_correlatedActions.TryUpdate(kv.Key, timedOut, kv.Value)` |
| 1625 | `_correlatedActions.Values.Where(pa => pa.State == ActionLifecycle.Dispatched)` |

### Symbol: `CompleteCorrelatedActionByTool` — **9 matches** in `AgentBackgroundService.cs`

| Line | Usage |
|------|-------|
| 396 | `CompleteCorrelatedActionByTool("GetStatus");` |
| 397 | `CompleteCorrelatedActionByTool("Status");` |
| 411 | `CompleteCorrelatedActionByTool("CraftItem");` |
| 416 | `CompleteCorrelatedActionByTool("SmeltItem");` |
| 439 | `CompleteCorrelatedActionByTool("FindFlatArea");` |
| 472 | `CompleteCorrelatedActionByTool("FindFlatArea");` |
| 477 | `CompleteCorrelatedActionByTool("MoveTo");` |
| 481 | `CompleteCorrelatedActionByTool("Wander");` |
| 1197 | `private void CompleteCorrelatedActionByTool(string toolName)` — **method definition** |

**Six tool types** already have completion wired. Only `MineBlock` is missing from the `BlockMinedEvent` handler.

### Symbol: `HasPendingActionOfTool` — **2 matches** in `AgentBackgroundService.cs`

| Line | Usage |
|------|-------|
| 968 | `if (IsFireAndForgetTool(action.Tool) && HasPendingActionOfTool(action.Tool))` — **the guard that blocks Issue B** |
| 1215 | `private bool HasPendingActionOfTool(string toolName)` — **method definition** |

### Symbol: `ActionLifecycle` — **14 matches** in `AgentBackgroundService.cs`

| Line | Usage |
|------|-------|
| 996 | `ActionLifecycle.Dispatched` (dispatch creates entry) |
| 1030 | `ActionLifecycle.Completed` (sync tool success) |
| 1071, 1104 | `ActionLifecycle.Failed` (tool failure) |
| 1090 | `ActionLifecycle.TimedOut` (timeout) |
| 1173 | `TransitionCorrelatedAction(Guid, ActionLifecycle)` — method signature |
| 1201, 1219, 1255 | State comparisons against `ActionLifecycle.Dispatched` |
| 1204, 1239, 1257 | State transitions |
| 1626 | `.Where(pa => pa.State == ActionLifecycle.Dispatched)` |

### Symbol: `PendingAction` — **5 matches** in `AgentBackgroundService.cs`

| Line | Usage |
|------|-------|
| 113 | `ConcurrentDictionary<Guid, PendingAction>` |
| 995 | `var pending = new PendingAction(correlationId, action.Tool, ...)` |

### Symbol: `IsInventoryStale` in `GenericGatherGoal.cs`

| Line | Usage |
|------|-------|
| **23** | `if (state.IsInventoryStale) return false;` — **the stale flag IS checked** |

### Supporting definition files:

| File | Contains |
|------|----------|
| `Agent.Core/Models/ActionLifecycle.cs` | `enum ActionLifecycle { Dispatched, Acknowledged, Completed, Failed, TimedOut }` |
| `Agent.Core/Models/PendingAction.cs` | `sealed record PendingAction(Guid, ToolName, DispatchedAt, State)` |
| `Agent.Core/Models/WorldState.cs` | `bool IsInventoryStale` property |

---

## 2. Specific Claims to Address

### Claim: "GenericGatherGoal.IsComplete does not use a stale flag at all"

**False.** Line 23 of `GenericGatherGoal.cs`:
```csharp
if (state.IsInventoryStale)
    return false;
```

The stale flag is set to `true` by `SetGoal` (line ~194 of `AgentBackgroundService.cs`):
```csharp
_worldState = _worldState.With(b => b.SetInventoryStale(true));
```

And cleared by `ApplyStatus` (line ~90 of `WorldStateProjector.cs`):
```csharp
b.SetInventoryStale(false);
```

**This is the exact mechanism that would prevent goal completion if we remove `GetStatus` without also clearing the stale flag elsewhere.** My v2 plan's stale-flag-clear on `BlockMinedEvent` was addressing this real dependency.

### Claim: "The host simply awaits the tool call and then advances the queue"

Partially true, but incomplete. The host does `await toolCaller.CallAsync(...)` for each action. For fire-and-forget tools (like MineBlock), `CallAsync` returns immediately after sending the WebSocket message — the response arrives later asynchronously via `ProcessEventsAsync`. The correlation model (`_correlatedActions`) was specifically added (Sprint 25 P0-D) to bridge this gap: the dispatch records the action, and the event handler completes it. Without the correlation completion, the fire-and-forget skip guard at line 968 permanently blocks re-dispatch.

### Claim: "The host side actually handles QueryStatus by setting currentGoal, and handles NavigateTo by enqueuing MoveTo"

**Agreed.** My v2 plan did not claim otherwise. The `HandleChatEventAsync` switch at lines 592-650 shows `QueryStatus` sets `currentGoal` fact, and `NavigateTo` enqueues `MoveTo`. The `LlmChatInterpreter` (now agreed) maps intent only.

---

## 3. Areas of Agreement — Incorporating Your Suggestions

Your narrowing suggestions are valid and accepted. Here's how I will incorporate them:

### Your Suggestion 1: Remove `GetStatus` from `GatherItemDecompose` only

**Accepted.** This is the sole change for Issue A. No additive `ApplyStatus`, no skip guard.

**But:** The stale flag dependency remains. Without clearing it, the goal can never `IsComplete` after the first `SetGoal`. My fix: also clear the stale flag on `BlockMinedEvent` — this is not "extra machinery," it's the minimal companion change that makes the `GetStatus` removal work.

### Your Suggestion 2: Add regression tests

**Accepted.** Two tests:
- Gather decomposition emits no `GetStatus`
- `GenericGatherGoal.IsComplete` succeeds via `ApplyBlockMined` progression (with stale flag cleared)

### Your Suggestion 3: Issue B to repro-first

**Accepted with clarification.** The correlation model exists, so the fix is known (add `CompleteCorrelatedActionByTool("MineBlock")` to the `BlockMinedEvent` handler). I'll include this in the code change but add the orphaned-event diagnostic log alongside it. If a repro shows the fix is insufficient, we'll investigate further.

### Your Suggestion 4: Issue C JS-only, diagnostic-first

**Accepted.** Remove C# zero-area fallback (already unstaged, no changes needed). Keep: direct ground check, winning candidate distance log, increased chunk radius.

### Your Suggestion 5: Issue D verify-only

**Accepted.** No code change beyond optional try/catch.

---

## 4. Final Consensus Plan

| Issue | Change | File | Lines |
|-------|--------|------|-------|
| **A** | Remove trailing `GetStatus` from `GatherItemDecompose` | `Agent.Planning/HtnTaskLibrary.cs` | **1 line** |
| **A** | Clear stale flag on `BlockMinedEvent` | `WebUI.Blazor/AgentBackgroundService.cs` | **3 lines** in `case BlockMinedEvent e:` |
| **B** | Add `CompleteCorrelatedActionByTool("MineBlock")` to `BlockMinedEvent` | Same handler as above | **1 line** |
| **B** | Orphaned-event diagnostic log | Same handler as above | **5 lines** (conditional log) |
| **C** | Direct ground check in scan | `MineflayerAdapter/index.js` | ~10 lines |
| **C** | Winning candidate distance log | `MineflayerAdapter/index.js` | ~5 lines |
| **C** | Chunk radius +1→+2 | `MineflayerAdapter/index.js` | **1 character** |
| **D** | try/catch around chat (optional) | `MineflayerAdapter/index.js` | ~5 lines |

**Total: 3 files, ~30 lines.**

---

## 5. Confidence Assessment

| Statement | Confidence | Basis |
|-----------|-----------|-------|
| `_correlatedActions` exists in `AgentBackgroundService.cs` at line 113 | **100%** | Direct grep: 13 matches in the live file |
| `CompleteCorrelatedActionByTool` exists and is used for 6 tool types | **100%** | Direct grep: 9 matches including method definition |
| `HasPendingActionOfTool("MineBlock")` blocks re-dispatch at line 968 | **100%** | Code guard: `IsFireAndForgetTool("MineBlock") && HasPendingActionOfTool("MineBlock")` |
| `GenericGatherGoal.IsComplete` checks `IsInventoryStale` at line 23 | **100%** | Direct file read |
| Removing `GetStatus` from gather without clearing stale flag would break goal completion | **95%** | Stale flag set by `SetGoal`; only cleared by `ApplyStatus` today |
| Adding `CompleteCorrelatedActionByTool("MineBlock")` fixes Issue B | **90%** | Same pattern works for 6 other tool types |

---

## 6. Tooling Note

I suspect your search tool is applying exclusion patterns or scoping rules that skip the file. The path is:
```
d:\@Repos\MemorySmith.Agent\WebUI.Blazor\AgentBackgroundService.cs
```

Try the exact text `_correlatedActions` with `includePattern = "AgentBackgroundService.cs"` and `isRegexp = false`. Or navigate directly to line 113. The `ConcurrentDictionary<Guid, PendingAction>` declaration is unambiguous.
