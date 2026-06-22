# Sprint 37 — Refined Fix Plan (v2)

**Date:** 2026-06-22  
**Author:** Agent (Sprint 37 continuation)  
**Status:** Plan — ready for implementation  
**Based on:** `sprint-37-handoff-20260621.md`, code analysis of `main` branch, audit feedback  

---

## Executive Summary

Four issues remain from Sprint 37 debugging. This v2 plan incorporates audit feedback by removing over-engineered layers and keeping each fix surgical. The correlation model (`_correlatedActions`, `ActionLifecycle`, `CompleteCorrelatedActionByTool`) **does** exist on this branch (Sprint 25 P0-D), making the fixes for Issues A and B convergent into a single tiny change.

### Issue Inventory

| # | Title | Severity | Fix Complexity | Primary File |
|---|-------|----------|----------------|-------------|
| A | Inventory resets to empty mid-gather | Critical | **1 file, 3 lines** | `HtnTaskLibrary.cs` + `AgentBackgroundService.cs` |
| B | MineBlock actions stop after first dispatch | Critical | **1 file, 3 lines** (converges with A) | `AgentBackgroundService.cs` |
| C | FindFlatArea picks distant tower | High | **1 file** | `index.js` |
| D | `bot._client.chat` error | Medium | **1 file, 5 lines** | `index.js` |

---

## Finding: Correlation Model Exists

The audit reported the `_correlatedActions` / `ActionLifecycle` model could not be found. **It is present.** The `ConcurrentDictionary<Guid, PendingAction>` lives at line 113 of `AgentBackgroundService.cs`, with `CompleteCorrelatedActionByTool`, `HasPendingActionOfTool`, `SweepTimedOutActions`, and `IsFireAndForgetTool` methods all implemented. The model was added in Sprint 25 P0-D.

This means Issue B's fix is **not** blocked by missing infrastructure — the correlation model is ready. The only gap is that `BlockMinedEvent` handler never calls `CompleteCorrelatedActionByTool("MineBlock")`.

---

## Issue A: Inventory Resets to Empty Mid-Gather

### Root Cause

`GatherItemDecompose` in `HtnTaskLibrary.cs` emits this plan:

```
SearchMemory → MineBlock(dirt, 10) → GetStatus
```

`DispatchActionsAsync` dequeues all three in one cycle. `GetStatus` dispatches while `MineBlock` is in-flight on the adapter. The JS `sendBotStatus()` reads `bot.inventory.items()` — which may be empty/partial during active mining — and sends a `StatusEvent`. `WorldStateProjector.ApplyStatus` **replaces** the entire inventory via `b.SetInventory(...)`, wiping out items accumulated by `ApplyBlockMined`'s additive `AddInventoryItem`.

**Evidence from session log:**
```
[21:22:32] Inventory +1 dirt -> total 10       ← BlockMinedEvent additive
[21:22:33] [action] GetStatus OK (0ms)          ← StatusEvent replaces inventory
[21:22:33] [plan] ... inventory: [dirt: 0/10]   ← Wiped!
```

### Fix: Two Surgical Changes

#### Change 1: Remove `GetStatus` from `GatherItemDecompose`

**File:** `Agent.Planning/HtnTaskLibrary.cs`

```csharp
// Current (last line of GatherItemDecompose):
        actions.Add(MakeAction("GetStatus"));
        return actions;

// Fix: remove the GetStatus line:
        return actions;
```

**Rationale:** The planner replans every 2s (see `MinReplanIntervalSeconds`). Each replan generates a new `SearchMemory → MineBlock → GetStatus`. Removing `GetStatus` from the gather decomposition means it never appears in mid-gather plans. The stale flag (see Change 2) handles the "inventory freshness" role that `GetStatus` previously served.

**Scope:** Only `GatherItemDecompose`. Other decomposers (`DecomposeCraftItem`, `DecomposeBuild`, `FindFlatAreaDecompose`, etc.) keep their existing `GetStatus` calls — those paths have different pacing characteristics.

#### Change 2: Clear stale flag + complete correlation on `BlockMinedEvent`

**File:** `WebUI.Blazor/AgentBackgroundService.cs`

In `ProcessEventsAsync`, `case BlockMinedEvent e:`:

```csharp
case BlockMinedEvent e:
    var itemKey = e.Block.Contains(':') ? e.Block.Split(':')[1] : e.Block;
    logger.LogInformation("Inventory +{Count} {Block} -> total {Total}",
        e.Count, itemKey, _worldState.Inventory.GetValueOrDefault(itemKey));
    // Sprint 37: a block was mined — inventory is no longer stale.
    // This replaces the stale-flag-clearing role formerly served by GetStatus
    // in the gather plan.
    if (_worldState.IsInventoryStale)
        _worldState = _worldState.With(b => b.SetInventoryStale(false));
    // Sprint 37: complete MineBlock correlation so subsequent MineBlocks can
    // dispatch. Each blockMined is one dig completion; completing the correlation
    // allows the next MineBlock to fire (JS queues sequential dispatches).
    CompleteCorrelatedActionByTool("MineBlock");
    break;
```

**Why this works:** `GenericGatherGoal.IsComplete` defers completion when `IsInventoryStale` is true. Previously only `ApplyStatus` cleared this flag. Now the first `BlockMinedEvent` also clears it — proving that mining has started and inventory is live. This allows the gather goal to detect completion without needing a `GetStatus` action.

**Why this is safe:** The stale flag is set to `true` by `SetGoal`. Once a block is physically mined (confirmed by the adapter's `blockMined` event), the inventory is demonstrably not stale. If the server clears inventory later (`/clear`), the next replan cycle will encounter the mismatch naturally and can trigger a GetStatus through `TryRecoverFromGameErrorAsync` or the health interrupt path.

### Side Effect: Unlocks Issue B Fix

Adding `CompleteCorrelatedActionByTool("MineBlock")` in the same handler automatically fixes Issue B. The MineBlock correlation transitions from `Dispatched` to `Completed` on the first mined block. The fire-and-forget skip guard (`HasPendingActionOfTool("MineBlock")`) then returns `false`, allowing the next MineBlock to dispatch on the next replan cycle.

---

## Issue B: MineBlock Actions Stop After First Dispatch

### Root Cause

The JS `case 'mine':` handler sends `blockMined` events per dig (with `correlationId`), but after the `while (mined < count)` loop completes, it only logs `mine: complete` — it sends **no completion event**. On the C# side, `case BlockMinedEvent e:` explicitly avoids calling `CompleteCorrelatedActionByTool("MineBlock")` (comment: "partial-progress event — don't transition").

Result: MineBlock stays in `Dispatched` state forever. The fire-and-forget skip guard blocks further dispatches. After 30s, `SweepTimedOutActions` transitions it to `TimedOut`. Next replan dispatches a new MineBlock, which times out again. Net effect: ~1 block per 30 seconds.

### Fix: Converges with Issue A Change 2

The `CompleteCorrelatedActionByTool("MineBlock")` call added in `case BlockMinedEvent e:` (Issue A Change 2) is **also the complete fix for Issue B**. Each `blockMined` event transitions the pending MineBlock correlation to `Completed`, allowing the next MineBlock to dispatch on the following replan cycle.

**Pacing after fix:** Each mined block completes its correlation → next replan (≤2s) dispatches new MineBlock → JS queues it behind running mine loop → continuous mining. No 30s timeout delay.

### Diagnostic Additions

For debugging, add a warning when a `blockMined` event arrives but no MineBlock correlation is pending (orphaned event):

```csharp
case BlockMinedEvent e:
    // ... existing code ...
    // Sprint 37: diagnostic — check if MineBlock correlation exists
    var hasMineBlockPending = _correlatedActions.Values.Any(a =>
        a.State == ActionLifecycle.Dispatched &&
        a.ToolName.Equals("MineBlock", StringComparison.OrdinalIgnoreCase));
    if (!hasMineBlockPending)
        logger.LogWarning(
            "[correlation] BlockMinedEvent for {Block} arrived but no MineBlock pending — " +
            "possible orphaned event or completed/abandoned goal",
            itemKey);
    // ... correlation fix from Change 2 ...
    break;
```

---

## Issue C: FindFlatArea Picks Distant Tower

### Root Cause

The first scan (radius=30) returns area=0 even when the bot stands on flat grass at (-280,63,103). The proximity weighting (unstaged, not deployed) only helps when multiple candidates exist in one scan — it doesn't help when the scan finds zero.

The height map builds by scanning `scanCenter.y + yAbove` down to `scanCenter.y - yBelow`. At the bot's Y=63, this is y=73 down to y=47. Ground at y=62 (grass_block) should match: `boundingBox === 'block'` with air above. The most likely cause: **unloaded chunks** causing `bot.blockAt()` to return `null` for ground-level blocks, producing an empty height map.

The unstaged changes include proximity weighting + scanOrigin + zero-area fallback. These need deployment. But they don't fix the "why is area=0 on flat ground?" question.

### Fix: All in Adapter

#### Change 1: Direct ground-height check before scan

In `index.js`, `case 'findFlatArea':`, before the main scan loop:

```js
// Sprint 37: direct ground check before full scan.
// Seeds the height map with the block directly below the bot's feet,
// ensuring at least one entry even if chunks aren't fully loaded.
const botPosObj = botPos();  // already exists before scan
const feetPos = toVec3(botPosObj.x, botPosObj.y - 1, botPosObj.z);
const feetBlock = bot.blockAt(feetPos);
if (feetBlock && feetBlock.boundingBox === 'block' && !LIQUID_BLOCK_NAMES.has(feetBlock.name)) {
    const aboveFeet = bot.blockAt(toVec3(botPosObj.x, botPosObj.y, botPosObj.z));
    if (!aboveFeet || aboveFeet.name === 'air' || aboveFeet.boundingBox === 'empty') {
        heightMap.set(`${botPosObj.x},${botPosObj.z}`, {
            x: botPosObj.x, z: botPosObj.z, y: botPosObj.y
        });
        console.log(
            `[findFlatArea] direct ground hit at (${botPosObj.x},${botPosObj.y},${botPosObj.z})` +
            ` block=${feetBlock.name}`
        );
    }
}
```

**Note:** This code must be placed AFTER the `heightMap` is declared, BEFORE the main scan loop. Currently the height map is declared inside the scan function. We need to extract the `const heightMap = new Map();` declaration before the scan loop, then add the ground check, then run the scan.

#### Change 2: Add winning candidate distance to console log

```js
if (bestCandidate) {
    const distFromCenter = Math.sqrt(
        ((bestCandidate.minX + bestCandidate.maxX) / 2 - scanCenter.x) ** 2 +
        ((bestCandidate.minZ + bestCandidate.maxZ) / 2 - scanCenter.z) ** 2
    );
    console.log(
        `[findFlatArea] best at (${bestCandidate.x},${bestCandidate.y},${bestCandidate.z})` +
        ` area=${bestCandidate.area} yRange=${bestCandidate.yRange}` +
        ` compact=${bestCandidate.compactness} score=${bestScore.toFixed(1)}` +
        ` distFromCenter=${distFromCenter.toFixed(1)} (${scanElapsed}ms)`
    );
}
```

#### Change 3: Increase chunk wait radius

```js
const chunkRadius = Math.ceil(r / 16) + 2;  // was +1, now +2 for safety margin
```

This increases the chunk-load wait from ~3 chunks to ~4 chunks for radius=30, which may catch boundary chunks missed by the tighter margin.

### Verification Criteria

1. First scan (radius=30) on flat ground returns area >= 25
2. Console log shows `[findFlatArea] direct ground hit` when standing on solid ground
3. Console log shows winning candidate distance from center

---

## Issue D: `bot._client.chat is not a function`

### Investigation

The current `index.js` already uses `bot.chat(args.message ?? '')` at line 846. No reference to `_client.chat` exists in the file. The error in the session log came from a previous version of the code that has since been corrected.

### Fix: Defensive try/catch

```js
case 'chat':
    try {
        bot.chat(args.message ?? '');
    } catch (e) {
        console.error(`[chat] failed to send message: ${e.message}`);
        sendEvent('error', { action: 'chat', message: e.message, correlationId });
    }
    break;
```

---

## Unstaged Changes: Status

The following changes from the previous Sprint 37 agent are in the working tree (unstaged) and must be deployed. They do NOT conflict with this plan.

| File | Change | Verified? |
|------|--------|-----------|
| `gameModeState.js` | Fix falsy guard (`if (!mode)` → `if (mode === undefined \|\| mode === null)`) | ✅ |
| `index.js` | `normalizeGameMode` handles numeric values | ✅ |
| `index.js` | `FLAT_SCORE_WEIGHTS` with `proximity: 0.30` | ✅ |
| `index.js` | `scanOriginX/Y/Z` support in findFlatArea | ✅ |
| `BuildGoalDecomposer.cs` | `requireOrigin=true` always | ✅ |
| `AgentBackgroundService.cs` | Zero-area scan counter + fallback | ✅ |
| `AgentBackgroundService.cs` | Diagnostic logging for gather completion | ✅ |
| `AgentBackgroundService.cs` | Chat response logging for CreateGoal/NavigateTo | ✅ |

---

## Complete File Change Summary

| File | Lines Changed | What |
|------|--------------|------|
| `Agent.Planning/HtnTaskLibrary.cs` | **1 line** | Remove `MakeAction("GetStatus")` from `GatherItemDecompose` |
| `WebUI.Blazor/AgentBackgroundService.cs` | **~12 lines** | Add stale-flag clear + correlation completion + diagnostic log in `case BlockMinedEvent e:` |
| `MineflayerAdapter/index.js` | **~25 lines** | Direct ground-height check, winning candidate distance log, increased chunk radius, try/catch for chat |

---

## Test Plan

### Unit Tests

All existing 504+ tests must pass with zero failures.

New tests to add:

| Test | File | What It Verifies |
|------|------|-----------------|
| Gather decomposition has no GetStatus | `HtnTaskLibraryTests.cs` | `GatherItemDecompose` output does not contain `GetStatus` action |
| BlockMinedEvent clears stale flag | `AgentBackgroundServiceTests.cs` | After `BlockMinedEvent`, `IsInventoryStale` is `false` |
| BlockMinedEvent completes MineBlock correlation | `AgentBackgroundServiceTests.cs` | After `BlockMinedEvent`, no MineBlock pending in correlation tracker |
| Gather goal completes via blockMined only | `GenericGatherGoalTests.cs` | Goal completes correctly when inventory filled solely by `ApplyBlockMined` (no `ApplyStatus`) |

### Runtime Verification

#### Test 1: Gather 10 Dirt (Survival)
1. Start agent (survival mode)
2. `leo gather 10 dirt`
3. **Expected:**
   - ✅ No `GetStatus` in `[plan]` line
   - ✅ `Inventory +1 dirt -> total N` increments monotonically
   - ✅ No `[correlation] MineBlock TIMED OUT`
   - ✅ Goal completes with `inventory: [dirt: 10/10]`
   - ✅ Console shows `[goal] completed: Gather:dirt` with correct final count

#### Test 2: Build Near Tower (Survival)
1. Stand on flat ground with a tower >40 blocks away
2. `leo build a house`
3. **Expected:**
   - ✅ `[findFlatArea] direct ground hit` appears
   - ✅ First scan returns area > 0
   - ✅ Bot builds near spawn, not on tower

#### Test 3: Consecutive Gather Goals
1. `leo gather 10 dirt` → wait for completion
2. `leo gather 10 oak_log` → immediately after
3. **Expected:** Second goal starts clean. No stale-flag cross-contamination.

#### Test 4: Chat API
1. Send any command to bot
2. **Expected:** Bot responds in-game. No `Game error [chat]:` in logs.

#### Test 5: Creative Mode
1. Set game mode to creative
2. `leo gather 100 diamond`
3. **Expected:** Goal completes immediately (`IsCreativeMode` fast-path)

### Edge Cases

| Scenario | Expected |
|----------|----------|
| `/clear` during gather | Mine loop continues; blockMined events restart inventory. No permanent stale flag. |
| `/clear` while idle | Next goal's `SetGoal` sets stale flag → first blockMined clears it → normal flow. |
| Adapter restart mid-gather | Reconnect triggers fresh `SetGoal` with stale flag → stable restart. |
| Zero-area scan (ocean/void) | After 2 consecutive zero-area scans, fallback to bot position (unstaged change). |
| Player types "status" during gather | `QueryStatus` response reflects current projected inventory (may differ from server if items are mid-air). This is acceptable — the projected state is the "best known" state. |

---

## Implementation Order

```
1. index.js        — ground check, logging, chat try/catch  (no dependencies)
2. HtnTaskLibrary.cs — remove GetStatus from gather          (no dependencies)
3. AgentBackgroundService.cs — BlockMinedEvent handler       (depends on 2)
4. dotnet build && dotnet test
5. Deploy & verify
```

Steps 1-3 are independent and can be edited in any order. Step 4 validates all C# changes. Step 5 requires server + adapter restart.

---

## Deployment

```powershell
# 1. Kill any running node processes
taskkill /F /IM node.exe

# 2. Build and test C#
cd d:\@Repos\MemorySmith.Agent
dotnet build
dotnet test --no-build

# 3. Restart MemorySmith server
cd d:\@Repos\MemorySmith\MemorySmith.App
dotnet run --launch-profile http

# 4. Start Mineflayer adapter
cd d:\@Repos\MemorySmith.Agent\MineflayerAdapter
node index.js
```

---

## Key Differences from v1 Plan

| Aspect | v1 (Previous) | v2 (Refined) |
|--------|---------------|--------------|
| Issue A fix | 3 layers: additive ApplyStatus + skip guard + warning | **1 layer:** remove GetStatus from gather plan + clear stale flag on blockMined |
| Issue B fix | Separate B1/B2 options | **Converges with A:** `CompleteCorrelatedActionByTool` added to blockMined handler |
| Issue C fix | 3 changes across JS + C# | **All in adapter:** ground check, logging, chunk radius |
| Issue D fix | try/catch | Same (unchanged) |
| Correlation model | Assumed it existed | **Verified it exists** (Sprint 25 P0-D) |
| Total files changed | 4-5 files | **3 files** |
| New test burden | ~5 new test scenarios | **~4 specific unit tests** + runtime verification |
