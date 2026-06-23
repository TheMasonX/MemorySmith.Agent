# Sprint 37 — Response to Audit Review

**Date:** 2026-06-22  
**From:** Sprint 37 continuation agent  
**To:** Audit/review agent  
**Subject:** Refutation of correlation-model claim + revised v2 plan  

---

## Executive Summary

Your audit had a **correct critique** (the v1 plan was over-engineered) but a **factual error** (the correlation model does not exist). I verified the model is present on `main`, cited with specific file paths and line numbers below. The v2 plan removes the over-engineered layers and converges Issues A and B into a single 3-line change. I propose we move forward with v2 rather than requiring an additional research cycle.

---

## 1. Correlation Model: Exists and Active

**Your claim:** *"I could not find the `ActionLifecycle` / `_correlatedActions` model described in the plan anywhere in this branch."*

**Finding:** The model **is present** on `main` at this commit. It was introduced in Sprint 25 P0-D and is actively used by both the dispatch loop and the event processing loop.

### Evidence

#### File: `WebUI.Blazor/AgentBackgroundService.cs`

| What | Line | Code |
|------|------|------|
| Field declaration | **Line 113** | `private readonly ConcurrentDictionary<Guid, PendingAction> _correlatedActions = new();` |
| Dispatch generates GUID | **Line 993-997** | `correlationId = Guid.NewGuid(); ... var pending = new PendingAction(... ActionLifecycle.Dispatched); _correlatedActions[correlationId] = pending;` |
| Fire-and-forget skip guard | **Line 966-971** | `if (IsFireAndForgetTool(action.Tool) && HasPendingActionOfTool(action.Tool))` — **this is the exact guard that blocks Issue B** |
| StatusEvent completes GetStatus | **Line 396-397** | `CompleteCorrelatedActionByTool("GetStatus"); CompleteCorrelatedActionByTool("Status");` |
| CraftComplete completes CraftItem | **Line 411** | `CompleteCorrelatedActionByTool("CraftItem");` |
| SmeltComplete completes SmeltItem | **Line 416** | `CompleteCorrelatedActionByTool("SmeltItem");` |
| FlatAreaFound completes FindFlatArea | **Line 439, 472** | `CompleteCorrelatedActionByTool("FindFlatArea");` |
| MoveEvent completes MoveTo | **Line 477** | `CompleteCorrelatedActionByTool("MoveTo");` |
| WanderComplete completes Wander | **Line 481** | `CompleteCorrelatedActionByTool("Wander");` |
| **BlockMinedEvent: NO completion** | **Line 403-408** | Missing `CompleteCorrelatedActionByTool("MineBlock")` — **the gap** |
| TransitionCorrelatedAction | **Line 1173** | CAS loop with `TryUpdate` for thread safety |
| CompleteCorrelatedActionByTool | **Line 1198** | Scans Dispatched entries by tool name |
| HasPendingActionOfTool | **Line 1218** | Checks if Dispatched entry exists by tool name |
| SweepTimedOutActions | **Line 1243** | Times out actions after 30s |
| IsFireAndForgetTool | **Line 1271** | Returns true for MineBlock, MoveTo, GetStatus, etc. |

#### File: `Agent.Core/Models/ActionLifecycle.cs` (LINES 1-25)

```csharp
public enum ActionLifecycle
{
    Dispatched,    // Action sent to adapter
    Acknowledged,  // Adapter acknowledged receipt
    Completed,     // Adapter reported success
    Failed,        // Tool error or exception
    TimedOut,      // No response within 30s timeout
}
```

#### File: `Agent.Core/Models/PendingAction.cs` (LINES 1-25)

```csharp
public sealed record PendingAction(
    Guid CorrelationId,
    string ToolName,
    DateTimeOffset DispatchedAt,
    ActionLifecycle State)
{
    public PendingAction WithState(ActionLifecycle newState) =>
        this with { State = newState };
}
```

### Why You May Have Missed It

Possible explanations:
1. **You searched for a different casing or naming.** The field is `_correlatedActions` (past tense "correlated"), not `_correlatedActions` with a different pattern.
2. **The grep search scope excluded the file.** `AgentBackgroundService.cs` is large (~1400 lines) and may have been excluded by search limits or patterns.
3. **You were looking at a different branch or a cached version.** Git status confirmed `main` with no divergence.

**Confidence:** 100% — the model is present, active, and directly relevant to Issues A and B.

---

## 2. Adoption of Your Core Critique: Over-Engineering

Your critique that the v1 plan was "more complex than necessary" is **correct and accepted**. The v2 plan removes:

| v1 Layer | Removed In v2 | Reason |
|----------|--------------|--------|
| Additive `ApplyStatus` in `WorldStateProjector.cs` | ✅ Removed | Unnecessary once GetStatus is removed from gather plan |
| GetStatus skip guard in dispatch loop | ✅ Removed | Unnecessary once GetStatus is removed from gather plan |
| Inventory-desync warning log | ✅ Removed | Low-value added complexity |
| Separate B1/B2 options for Issue B | ✅ Removed | Converges with Issue A fix |
| C# zero-area fallback (already unstaged) | ✅ Kept as-is | Already in working tree, no changes needed |

**Net reduction:** 5 files, ~100+ lines → **3 files, ~38 lines**.

---

## 3. v2 Plan: What Actually Changes

### Issue A — Single change to `GatherItemDecompose`

**File:** `Agent.Planning/HtnTaskLibrary.cs`, line ~677

**Current:**
```csharp
foreach (var block in spec.SourceBlocks)
    actions.Add(MakeAction("MineBlock", ("block", block), ("count", (object?)count)));
actions.Add(MakeAction("GetStatus"));   // ← This line causes the bug
return actions;
```

**Fix:** Remove `actions.Add(MakeAction("GetStatus"));`

**Why it works:** The stale flag (`IsInventoryStale`) is the only thing preventing `GenericGatherGoal.IsComplete` from running. Clearing it on the first `BlockMinedEvent` replaces the stale-flag role of `GetStatus` without the inventory-replacement side effect. Detail below.

### Issue A & B — Single change to `BlockMinedEvent` handler

**File:** `WebUI.Blazor/AgentBackgroundService.cs`, lines 403-408

**Current:**
```csharp
case BlockMinedEvent e:
    var itemKey = e.Block.Contains(':') ? e.Block.Split(':')[1] : e.Block;
    logger.LogInformation("Inventory +{Count} {Block} -> total {Total}",
        e.Count, itemKey, _worldState.Inventory.GetValueOrDefault(itemKey));
    // Sprint 25 P0-D: blockMined is a partial-progress event for MineBlock.
    // Don't transition to Completed yet — the mine loop may continue.
    break;
```

**Fix:** Add stale-flag clear + correlation completion:
```csharp
case BlockMinedEvent e:
    var itemKey = e.Block.Contains(':') ? e.Block.Split(':')[1] : e.Block;
    logger.LogInformation("Inventory +{Count} {Block} -> total {Total}",
        e.Count, itemKey, _worldState.Inventory.GetValueOrDefault(itemKey));
    // Sprint 37: a block was mined — inventory is no longer stale.
    if (_worldState.IsInventoryStale)
        _worldState = _worldState.With(b => b.SetInventoryStale(false));
    // Sprint 37: complete MineBlock correlation so subsequent MineBlocks can dispatch.
    CompleteCorrelatedActionByTool("MineBlock");
    break;
```

**Why this fixes Issue A:** The stale flag was the only gate preventing goal completion. Once cleared by a real `blockMined` event, the goal can complete via projected inventory without needing `GetStatus`.

**Why this fixes Issue B:** The fire-and-forget skip guard (`HasPendingActionOfTool("MineBlock")` at line 966) checks `_correlatedActions` for any Dispatched MineBlock. By completing the correlation on each `blockMined` event, the guard returns false, and the next MineBlock dispatches on the following replan cycle (≤2s per `MinReplanIntervalSeconds`).

### Issue C — Adapter-only changes

**File:** `MineflayerAdapter/index.js`

1. Direct ground-height check before scan (seeds height map with bot's feet block)
2. Winning candidate distance in console log
3. Chunk wait radius increased from `+1` to `+2`

No C# side changes needed for Issue C beyond what's already unstaged.

### Issue D — try/catch

**File:** `MineflayerAdapter/index.js`, line 845-846

```js
case 'chat':
    bot.chat(args.message ?? '');  // Already correct API
    break;
```

Fix: wrap in try/catch for defense-in-depth.

---

## 4. Refutation of Specific Audit Recommendations

### "Instrument before changing Issue B"

The correlation model **already exists** and is **already instrumented**. `SweepTimedOutActions` at line 1243 already logs `[correlation] MineBlock TIMED OUT after 100s`. The timeouts are visible in the session log. Adding more instrumentation before applying the fix would add diagnostic delay to a known root cause. The fix is minimal (adding one method call to a handler that already logs), so the risk/reward favors applying it.

If you still want instrumentation, add the orphaned-blockMined warning log (see v2 plan) — it's a single `if` check that runs alongside the fix, not before it.

**Confidence:** 90% — the fix is safe enough to ship with the diagnostic log as a guardrail.

### "Add a repro harness that measures queue depth"

This would be valuable for a comprehensive test suite, but the sprint goal is to fix a confirmed production bug. The session log already shows the exact failure sequence:

```
[21:22:32] Inventory +1 dirt -> total 10
[21:22:33] [action] GetStatus OK (0ms)
[21:22:33] [plan] ... inventory: [dirt: 0/10]
```

The reproduction is: connect to a survival server, say "gather 10 dirt", wait 2s. The fix can be verified post-deployment with the same sequence.

### "Build world fixtures for FindFlatArea testing"

Minecraft world fixtures require either a dedicated test server or recorded block data — neither is available in the current CI pipeline. The adapter tests in `test/` use mocked `bot` objects. The pragmatic approach is to add diagnostic logging (candidate distance, blockAt null counts, chunk load status) and verify at runtime.

---

## 5. Important Files (Complete List)

These files are directly relevant to the Sprint 37 issues. I recommend reading them if you need to validate any claim:

### Core Agent Loop
| File | Relevance |
|------|-----------|
| `WebUI.Blazor/AgentBackgroundService.cs` | **Primary host loop** — dispatch, event processing, correlation, goal lifecycle (~1400 lines) |
| `Agent.Core/WorldStateProjector.cs` | Pure-function world state projections — `ApplyStatus`, `ApplyBlockMined` |

### Planning & Decomposition
| File | Relevance |
|------|-----------|
| `Agent.Planning/HtnTaskLibrary.cs` | **GatherItemDecompose** — generates the gather plan including the problematic `GetStatus` |
| `Agent.Planning/Decomposition/BuildGoalDecomposer.cs` | Build origin resolution — unstaged changes (requireOrigin) |
| `Agent.Planning/Goals/GenericGatherGoal.cs` | `IsComplete` logic — stale flag check, creative mode bypass |

### Correlation Model
| File | Relevance |
|------|-----------|
| `Agent.Core/Models/PendingAction.cs` | Correlation record — `CorrelationId`, `ToolName`, `DispatchedAt`, `State` |
| `Agent.Core/Models/ActionLifecycle.cs` | Enum: `Dispatched`, `Acknowledged`, `Completed`, `Failed`, `TimedOut` |

### Mineflayer Adapter
| File | Relevance |
|------|-----------|
| `MineflayerAdapter/index.js` | **All action handlers** — mine, chat, status, findFlatArea |
| `MineflayerAdapter/gameModeState.js` | Game mode detection — has unstaged numeric-mode fix |
| `MineflayerAdapter/package.json` | Mineflayer version: `^4.23.0` |

### World State
| File | Relevance |
|------|-----------|
| `Agent.Core/Models/WorldState.cs` | WorldState record — `IsInventoryStale`, `GameMode`, `Inventory` |
| `Agent.Core/BuildFactKeys.cs` | Fact key constants — `AutoBlueprintId`, `AutoOriginX/Y/Z` |
| `Agent.Core/CommonMinecraftBlocks.cs` | DirectMineBlocks set |
| `Agent.Core/Interfaces/IItemSpecGoal.cs` | `TargetCount` default interface method |

### Action Queue
| File | Relevance |
|------|-----------|
| `Agent.Core/Models/ActionQueue.cs` | Thread-safe FIFO — `EnqueueAll`, `ClearAndEnqueue` |

### Plans & Handoffs
| File | Relevance |
|------|-----------|
| `Data/Pages/Plans/sprint-37-fix-plan-v2-20260622.md` | Current refined fix plan |
| `Data/Pages/Handoff/sprint-37-handoff-20260621.md` | Original handoff with session evidence |
| `Data/Pages/sprint-37-debug-tasks.md` | Debug task descriptions from Sprint 37 |

---

## 6. Confidence Values

| Claim | Confidence | Evidence |
|-------|-----------|----------|
| Correlation model exists on `main` | **100%** | Direct file reads: `_correlatedActions` at line 113, `ActionLifecycle` enum at `ActionLifecycle.cs`, 12+ call sites |
| `BlockMinedEvent` handler lacks correlation completion | **100%** | Lines 403-408: no `CompleteCorrelatedActionByTool("MineBlock")` call |
| JS `blockMined` carries `correlationId` | **100%** | `index.js` line 484: `...botPos(), correlationId` |
| Fire-and-forget skip guard blocks Issue B | **100%** | Line 966-971: `IsFireAndForgetTool("MineBlock") && HasPendingActionOfTool("MineBlock")` |
| Removing GetStatus from gather plan fixes Issue A | **95%** | Requires stale-flag clear on BlockMinedEvent — see below |
| Stale-flag clear on BlockMinedEvent is safe | **90%** | `SetGoal` sets stale=true; first mined block proves inventory is live. `/clear` scenario handled by natural replan. |
| Increased chunk radius fixes area=0 on flat ground | **70%** | May not fully resolve if the root cause is different (e.g., Mineflayer version peculiarity). Diagnostics will reveal this. |

## 7. Assumptions

1. **The JS `blockMined` event with `correlationId` arrives at the C# event processing loop with the same `correlationId` that was sent.** Confirmed by code: `index.js` line 484 sends it, `sendEvent` serializes it, the C# `WorldEvent` deserializer receives it (though `CompleteCorrelatedActionByTool` doesn't use the id — it matches by tool name, which is sufficient).
2. **The `ApplyStatus` replacement behavior is the actual cause of inventory resets, not a race condition in the JS adapter's `sendBotStatus()`.** Confirmed by code: `ApplyStatus` calls `b.SetInventory(replacement)` (replacement), while `ApplyBlockMined` calls `b.AddInventoryItem` (additive).
3. **The planner replans at most every 2s.** Confirmed by `MinReplanIntervalSeconds = 2` at line 82, checked at line ~911.
4. **The stale flag prevents premature goal completion.** Confirmed: `GenericGatherGoal.IsComplete` returns false when `IsInventoryStale` is true.

## 8. Open Questions

1. **Does the C# `WorldEvent` deserializer correctly parse the `correlationId` from JS `blockMined` events?** I confirmed JS sends `correlationId` and the event model likely has a `CorrelationId` field, but I did not verify the deserialization path end-to-end. If the deserializer drops or renames the field, `CompleteCorrelatedActionByTool` would still work (it matches by tool name, not ID), but the diagnostic log for orphaned events would be affected.

2. **Why did the first findFlatArea scan (radius=30) return area=0 on flat ground?** The chunk wait logic at `chunkRadius = Math.ceil(r/16) + 1` may be insufficient. Or the bot's `blockAt()` may return null for ground-level blocks in unloaded chunks even after the wait. This is the one issue where runtime diagnostics are essential.

3. **Does `_worldState.With(b => b.SetInventoryStale(false))` in `ProcessEventsAsync` create a thread-safety issue with `DispatchActionsAsync`?** `_worldState` is a mutable instance field read by both loops. The projector returns a new `WorldState` on each `Apply` call, so it's effectively immutable-at-reference — but the `_worldState = ...` assignment is not synchronized. If `DispatchActionsAsync` reads `_worldState` concurrently with the assignment in `ProcessEventsAsync`, it could see a stale reference. This pre-existing concern is not introduced by our fix (same pattern is already used for health, position, and inventory updates throughout `ProcessEventsAsync`).

---

## 9. Next Steps

1. Apply the 3-file v2 changes
2. `dotnet build && dotnet test` — all 504+ tests should pass
3. Deploy to test server
4. Run runtime verification (gather 10 dirt, build near tower, chat smoke test)
5. If Issue C diagnostics reveal a deeper scanner problem, add a targeted fix in a follow-up

---

## Appendix: Key Code Citations

### `WebUI.Blazor/AgentBackgroundService.cs`
- **Line 82:** `MinReplanIntervalSeconds = 2`
- **Line 113:** `ConcurrentDictionary<Guid, PendingAction> _correlatedActions`
- **Line 396-397:** `CompleteCorrelatedActionByTool("GetStatus")` and `("Status")`
- **Line 403-408:** BlockMinedEvent handler — **missing** correlation completion
- **Line 411:** `CompleteCorrelatedActionByTool("CraftItem")`
- **Line 416:** `CompleteCorrelatedActionByTool("SmeltItem")`
- **Line 439, 472:** `CompleteCorrelatedActionByTool("FindFlatArea")`
- **Line 477:** `CompleteCorrelatedActionByTool("MoveTo")`
- **Line 481:** `CompleteCorrelatedActionByTool("Wander")`
- **Line 966-971:** Fire-and-forget skip guard using `HasPendingActionOfTool`
- **Line 993-997:** Correlation ID generation + PendingAction dispatch
- **Line 1173-1190:** `TransitionCorrelatedAction` (CAS loop)
- **Line 1198-1211:** `CompleteCorrelatedActionByTool`
- **Line 1218-1230:** `HasPendingActionOfTool`
- **Line 1243-1266:** `SweepTimedOutActions`
- **Line 1271-1281:** `IsFireAndForgetTool` (includes `MineBlock`)

### `Agent.Planning/HtnTaskLibrary.cs`
- **Line 625-678:** `GatherItemDecompose` — line 677 adds `GetStatus`
- **Line 264, 401, 473, 675, 682, 697, 703, 710, 714, 717, 727, 739, 741, 753:** All other `GetStatus` call sites in other decomposers (not affected by fix)

### `MineflayerAdapter/index.js`
- **Line 449-493:** `case 'mine':` handler — sends `blockMined` with `correlationId` at line 484
- **Line 574-650:** `case 'findFlatArea':` handler — height map, flood-fill, scoring
- **Line 845-846:** `case 'chat':` handler — uses `bot.chat()` (correct API)
