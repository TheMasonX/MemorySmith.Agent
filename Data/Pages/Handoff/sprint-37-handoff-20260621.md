# Sprint 37 — Agent Debug Handoff

**Date:** 2026-06-21  
**Author:** Agent (Sprint 37 debug cycle)  
**Recipient:** Next agent (Sprint 37 continuation)  
**State:** Unstaged changes in `MemorySmith.Agent` — NOT deployed (requires server + adapter restart)

---

## Executive Summary

The agent has three critical bugs that make it unusable for production gameplay. All three were previously attributed to a single root cause (game-mode detection failure in Mineflayer), but new evidence from the 2026-06-21 21:19 session reveals **deeper issues** beyond game-mode detection:

1. **Inventory resets to empty mid-gather** — `GetStatus` actions dispatched *during* active mining **replace** the accumulated inventory with the adapter's current (empty) snapshot, losing all progress.
2. **MineBlock actions stop being dispatched after the first one** — The dispatch loop skips remaining MineBlock actions because one is already "in-flight," but the in-flight one *never completes* (no `BlockMinedEvent` response from the adapter).
3. **FindFlatArea still picks distant tower** — Even with proximity scoring, the first scan (radius=30) returns area=0, and the second (radius=48) finds the tower at (-200,76,202), area=46.

Partial fixes were applied for game-mode detection (`normalizeGameMode` in both JS files) and FindFlatArea proximity, but the unstaged changes have **not been deployed** — the server and adapter need restarting.

---

## Open Issues (Require Code Changes)

### Issue A: Gather goal inventory resets to 0 mid-gather

**Severity:** Critical — makes all gather goals impossible

#### Evidence

From the 21:19 session log — the critical sequence:

```
[21:22:32] Inventory +1 dirt -> total 10       ← Item pickup via BlockMinedEvent (additive)
[21:22:33] [plan] ... inventory: [dirt: 0/10]  ← GetStatus replaced inventory with empty!
[21:22:33] [action] SearchMemory OK (62ms)
[21:22:33] [action] GetStatus OK (0ms)          ← StatusEvent REPLACES inventory
```

And later:
```
[21:22:38] [chat] bot: Inventory: Inventory is empty.
```

The bot had physically mined and picked up 10 dirt (items in hand, visible in the game screenshot), but the agent reports empty inventory.

#### Root Cause

In `WebUI.Blazor/AgentBackgroundService.cs`, the `DispatchActionsAsync` loop processes actions sequentially from the plan queue. For a gather plan:

```
[SearchMemory → MineBlock × 6 → GetStatus]
```

The loop:
1. Dequeues SearchMemory → dispatches (completes)
2. Dequeues `MineBlock` (1st of 6) → dispatches as fire-and-forget → returns immediately
3. Dequeues `MineBlock` (2nd of 6) → **skipped** because `IsFireAndForgetTool("MineBlock") && HasPendingActionOfTool("MineBlock")` is true
4. ... same for 3rd, 4th, 5th, 6th
5. Dequeues `GetStatus` → dispatches → **StatusEvent REPLACES entire inventory** with `bot.inventory.items()` (which may be empty because the items are mid-pickup)

In `Agent.Core/WorldStateProjector.cs`, `ApplyStatus` does a **replacement** (sets inventory to the StatusEvent's map), while `ApplyBlockMined` does an **additive** update. Since GetStatus fires immediately after the first MineBlock (before any blocks are actually broken), it resets the inventory to empty, undoing any accumulated pickups.

#### Recommended Fix

**Option A (recommended — minimal change, high impact):** In `DispatchActionsAsync`, skip `GetStatus` when there are pending fire-and-forget actions (MineBlock, PlaceBlock, etc.):

```csharp
// In the dispatch loop, when dequeuing an action:
if (action.Tool.Equals("GetStatus", StringComparison.OrdinalIgnoreCase)
    && _pendingActions.Any(a => IsFireAndForgetTool(a.Tool)))
{
    logger.LogDebug("[dispatch] GetStatus skipped — pending fire-and-forget actions");
    continue;
}
```

**Option B (alternative — architectural change):** Remove `GetStatus` from gather-type plans entirely. The goal completion check already uses the inventory from `BlockMinedEvent` additive updates. GetStatus is only needed to clear the stale flag. Instead, clear the stale flag on the first non-empty block mined event.

**Option C (surgical):** In `WorldStateProjector.ApplyStatus`, make inventory **additive** rather than replacement for keys that already exist. This prevents the StatusEvent from wiping out accumulated items:

```csharp
foreach (var (key, count) in NormalizeInventory(e.Inventory))
    b.AddInventoryItem(key, count);
// Only replace keys that DON'T exist in current inventory
```

Risk: if the server clears the bot's inventory (e.g., `/clear`), this would NOT detect it. But that's also handled by the stale flag mechanism.

#### Files to change

| File | Change |
|------|--------|
| `WebUI.Blazor/AgentBackgroundService.cs` | Skip GetStatus when pending fire-and-forget actions exist (Option A) |
| `Agent.Core/WorldStateProjector.cs` | OR: Make ApplyStatus inventory additive (Option C) |
| `Agent.Planning/HtnTaskLibrary.cs` | OR: Remove GetStatus from gather plans (Option B) |

---

### Issue B: MineBlock actions stop after the first dispatch

**Severity:** Critical — no mining progress possible

#### Evidence

```
[21:22:18] [action] MineBlock OK (1ms)       ← First (and only) MineBlock dispatched
... 10 seconds of progress from item pickups ...
[21:22:33] [action] GetStatus OK              ← No more MineBlocks dispatched
...
[21:25:29] [correlation] MineBlock b4a489d6 TIMED OUT after 100.7525665s
```

The first MineBlock is dispatched to the adapter but **never receives a `BlockMinedEvent` response**. The correlation system times out after 100s. Meanwhile, no further MineBlocks are dispatched because one is already "in-flight."

#### Root Cause

The `IsFireAndForgetTool` + `HasPendingActionOfTool` guard prevents dispatching a second MineBlock while one is already pending:

```csharp
if (IsFireAndForgetTool(action.Tool) && HasPendingActionOfTool(action.Tool))
{
    // skip — already in-flight
    continue;
}
```

But the in-flight MineBlock never completes because:
- The adapter's `mineBlock` handler fires once per block break
- If the block breaks instantly (creative mode) or doesn't drop items, no `BlockMinedEvent` is sent
- OR: the adapter fires a `BlockMinedEvent` but the `ProcessEventsAsync` pipeline doesn't match it to the correlation ID

The log shows `Inventory +1 dirt -> total N` events, which means **block mined events ARE arriving**. But the correlation ID might not be matching.

#### Investigation Needed

1. Check if `BlockMinedEvent` includes the `correlationId` from the dispatch:
   - Does the JS adapter send back `correlationId` in the `blockMined` event?
   - Check `MineflayerAdapter/index.js` around line 470-485 for the `blockMined` event
   - Does `CompleteCorrelatedActionByTool("MineBlock")` handle it?

2. Check `ProcessEventsAsync` in `AgentBackgroundService.cs` — how does it correlate block mined events back to pending MineBlock actions?

#### Recommended Fix

**Phase 1 (diagnostic):** Add a log when `BlockMinedEvent` arrives but no matching correlation ID is found. This will confirm whether the events are being correlated.

**Phase 2 (fix):** Either:
- Make the correlation match on block type + tool name rather than correlationId
- OR: Don't skip fire-and-forget MineBlocks — allow multiple MineBlock dispatches even when one is in-flight. The adapter can queue them.
- OR: Increment the pending MineBlock count and clear it when any block mined event arrives (not just the correlated one)

#### Files to investigate

| File | Purpose |
|------|---------|
| `MineflayerAdapter/index.js` lines ~470-485 | `blockMined` event emission |
| `WebUI.Blazor/AgentBackgroundService.cs` | `CompleteCorrelatedAction` usage |
| `WebUI.Blazor/AgentBackgroundService.cs` | `ProcessEventsAsync` correlation matching |

---

### Issue C: FindFlatArea still picks distant tower

**Severity:** High — build "a house" builds on the tower ~80 blocks away instead of nearby ground

#### Evidence

From the log:
```
Bot spawned at Position { X = -280, Y = 63, Z = 103 }
[21:21:05] [plan] Build:small-house: 1 actions [FindFlatArea]
[21:21:05] [findFlatArea] scan area=0 below minimum 25 - auto-origin not updated (consecutiveZero=1)
[21:21:07] [plan] Build:small-house: 1 actions [FindFlatArea]
[21:21:07] Build origin set for 'auto': (-200,76,202)    ← Tower! 80 blocks away and Y=76 (elevated)
[21:21:07] [findFlatArea] auto-set build origin (-200,76,202) area=46
```

The bot spawned at (-280,63,103) — flat ground. But the scan found area=0 on the first pass (radius=30, minArea=25), then area=46 at (-200,76,202) on retry (radius=48, minArea=10).

Screenshot confirms a wooden tower structure at that location.

#### Root Cause Analysis

The proximity scoring fix is in the **unstaged** code. The running code (without the fix) has NO proximity weighting, so a large flat area on a tower roof at distance ~80 blocks scores higher than any smaller ground patches.

Even with the proximity fix, the issue is that the **first scan (radius=30) found area=0**. This means no qualifying flat area exists within 30 blocks of the bot's position. The proximity fix only helps when multiple candidates exist in ONE scan — it doesn't help when ALL candidates are farther than 30 blocks.

The retry expands to radius=48, which finds the tower at distance ~80. But a radius of 48 means it searched within a 96×96 block area centered on the bot. The tower is at distance ~80 (from (-280,63,103) to (-200,63,103) = 80 blocks in X only). This is outside radius 48... unless the scan center drifted.

Wait, (-280,63,103) to (-200,76,202):
- X diff: 80
- Z diff: 99
- Distance: sqrt(80² + 99²) ≈ 127 blocks

That's WAY outside a radius-48 scan. So the tower WAS found by the C# `FindFlatArea` retry with radius=48... but 127 > 48. The math doesn't work unless the scan center moved.

Actually, looking at the log again — the scan center is `botPosObj`. Between spawn and the second scan (2 seconds later), did the bot move? The log doesn't show a MoveTo action. But maybe the bot walked or the adapter's `botPos()` drifted.

OR: `radius=48` is passed from C#, but the scan uses `FLAT_AREA_SCAN_RADIUS=32` as the default in the running (unstaged) code. Wait, no — the scan reads `args.radius`:

```js
const { radius = FLAT_AREA_SCAN_RADIUS, ... } = args;
const r = Math.max(1, Math.min(radius, 64));
```

If C# passes radius=48, then r=48. And the scan covers dx = [-48, 48] and dz = [-48, 48], which is a 97×97 area. The tower at distance ~127 would NOT be found at radius=48.

Hmm, so the tower at (-200,76,202) was found by a different mechanism. Maybe the retry radius calculation is wrong, or there was a previous FlatAreaFound event from a different session.

#### Recommended Fix

1. **Ensure the unstaged changes are deployed** (proximity weighting + scanOrigin + zero-area fallback)
2. **Add diagnostic logging** in the `findFlatArea` result to show the winning candidate's coordinates and distance from center
3. Investigate why the first scan (radius=30) finds area=0 when the bot is standing on flat ground. The height scan (yAbove=10, yBelow=16, maxSlope=3) might not be detecting the grass blocks properly, or the blocks at the bot's position are not loaded yet.
4. Consider adding a **direct ground check**: before running the scan, check if the block directly below the bot is solid ground. If so, use it immediately instead of scanning.

#### Files to change

| File | Change |
|------|--------|
| `MineflayerAdapter/index.js` findFlatArea handler | Log winning candidate coords + distance |
| `MineflayerAdapter/index.js` findFlatArea handler | Add direct ground-check shortcut |
| `WebUI.Blazor/AgentBackgroundService.cs` | (already has zero-area fallback in unstaged) |

---

### Issue D: `bot._client.chat is not a function` error

**Severity:** Medium — prevents bot from chatting

#### Evidence

```
[21:19:20] Game error [chat]: bot._client.chat is not a function
[21:20:34] Game error after cycle (failures=1/3): chat:bot._client.chat is not a function
```

#### Root Cause

Mineflayer version incompatibility. In newer versions of Mineflayer (1.21+), `bot._client` has a different API or the `chat` method has been moved/renamed. The adapter tries to call `bot._client.chat(message)` in the chat handler.

#### Recommended Fix

Check the Mineflayer version in `package.json` and update the chat handler to use the correct API. In Mineflayer 1.21+, the chat method may be `bot.chat(message)` or `bot._client.sendChat(message)`.

---

## Previously Applied Fixes (Unstaged, Not Yet Deployed)

These changes are in the working tree but have NOT been deployed. The server and Mineflayer adapter need to be restarted for them to take effect.

| File | Fix | Status |
|------|-----|--------|
| `MineflayerAdapter/gameModeState.js` | Fixed `if (!mode)` falsy guard — now properly handles `0` (survival mode) | Unstaged |
| `MineflayerAdapter/index.js` (game mode) | Same `normalizeGameMode` fix with numeric mode mapping | Unstaged |
| `MineflayerAdapter/index.js` (proximity) | Added `proximity` weight (0.30) to `FLAT_SCORE_WEIGHTS` | Unstaged |
| `MineflayerAdapter/index.js` (scanOrigin) | `findFlatArea` accepts `scanOriginX/Y/Z` to center scan at user-specified coords | Unstaged |
| `Agent.Planning/Decomposition/BuildGoalDecomposer.cs` | `requireOrigin=true` always; explicit origin triggers FindFlatArea scan instead of direct build | Unstaged |
| `WebUI.Blazor/AgentBackgroundService.cs` | `_consecutiveZeroAreaScans` counter + fallback to bot position after 2 zero-area scans | Unstaged |
| `WebUI.Blazor/AgentBackgroundService.cs` | Diagnostic logging for gather goal completion (gameMode, stale, inventory) | Unstaged |
| `WebUI.Blazor/AgentBackgroundService.cs` | Chat response logging for CreateGoal and NavigateTo intents | Unstaged |
| `Agent.Planning/HtnTaskLibrary.cs` | `ReadLastFlatAreaFact` helper + retry with reduced `minArea` (25→10 on retry) | ✅ Already committed |
| `Agent.Planning/HtnTaskLibrary.cs` | `stderr` diagnostic log for `isCreativeMode` at plan-decomposition time | ✅ Already committed |

---

## Test Results (Latest)

| Suite | Passed | Failed | Skipped |
|-------|--------|--------|---------|
| `MemorySmith.Agent.Tests` | 504 | 0 | 0 |
| `MemorySmith.Tests` | 517 | 0 | 9 |

All passing. The unstaged changes do not break any existing tests.

---

## New Tasks to Create

Based on the 21:19 session evidence, the following tasks should be created in MemorySmith:

### TSK-NNN4: GetStatus resets inventory during active gather
- **Type:** Bug
- **Priority:** Critical
- **Description:** `GetStatus` action dispatched mid-gather (when MineBlock actions are pending) replaces the accumulated inventory with empty. This causes gather progress to be lost and goals to stall. Fix: skip GetStatus when fire-and-forget actions are pending, or make inventory updates additive.

### TSK-NNN5: MineBlock correlation never completes
- **Type:** Bug
- **Priority:** Critical
- **Description:** The first MineBlock is dispatched but never receives a matching `BlockMinedEvent` response. Subsequent MineBlocks are skipped because one is already "in-flight." The correlation times out after ~100s. Fix: allow multiple MineBlock dispatches or fix correlation matching.

### TSK-NNN6: `bot._client.chat is not a function`
- **Type:** Bug
- **Priority:** Medium
- **Description:** Mineflayer version incompatibility — the chat handler uses `bot._client.chat()` which doesn't exist in the installed Mineflayer version.

---

## Deployment Checklist

To test the unstaged fixes:

```powershell
# 1. Kill any running node processes
taskkill /F /IM node.exe

# 2. Restart the MemorySmith server
cd d:\@Repos\MemorySmith\MemorySmith.App
dotnet run --launch-profile http

# 3. Start the Mineflayer adapter
cd d:\@Repos\MemorySmith.Agent\MineflayerAdapter
node index.js
```

---

## Appendix: Key File Locations

| File | Purpose |
|------|---------|
| `MineflayerAdapter/index.js` | Core Mineflayer adapter — all block interaction, inventory, chat, flat-area scan |
| `MineflayerAdapter/gameModeState.js` | Game mode event emission + normalization |
| `WebUI.Blazor/AgentBackgroundService.cs` | Main agent loop — dispatch, replan, event processing, correlation |
| `Agent.Core/WorldStateProjector.cs` | World state projection — inventory add/replace, fact storage |
| `Agent.Planning/HtnTaskLibrary.cs` | Build/gather plan decomposition — creative vs survival paths |
| `Agent.Planning/Decomposition/BuildGoalDecomposer.cs` | Build goal decomposition — origin resolution |
| `Agent.Core/BuildFactKeys.cs` | Fact key constants (LastFlatArea, AutoOriginX/Y/Z, etc.) |
| `Agent.Core/Models/WorldState.cs` | WorldState record — IsCreativeMode, GameMode, Inventory |
| `Agent.Planning/Goals/GenericGatherGoal.cs` | Gather goal IsComplete logic |
| `Agent.Core/CommonMinecraftBlocks.cs` | DirectMineBlocks set |
| `Data/Pages/sprint-37-debug-tasks.md` | Previous sprint 37 task descriptions |
| `Scripts/Create-DebugTasks.ps1` | Script to create tasks in MemorySmith |
