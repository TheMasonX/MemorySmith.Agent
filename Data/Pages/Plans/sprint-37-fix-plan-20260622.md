# Sprint 37 — Fix Plan

**Date:** 2026-06-22  
**Author:** Agent (Sprint 37 continuation)  
**Status:** Plan — ready for implementation  
**Based on:** `sprint-37-handoff-20260621.md`, code analysis of `main` branch

---

## Executive Summary

Four open issues remain from the Sprint 37 debug cycle, plus a set of unstaged changes that must be verified. Two issues are **Critical** (gather/inventory), one is **High** (build origin), and one is **Medium** (chat API). This plan covers the remaining code changes needed beyond what was already applied as unstaged work.

### Issue Inventory

| # | Title | Severity | Root Cause Identified | Needs Code Change |
|---|-------|----------|----------------------|-------------------|
| A | Inventory resets to empty mid-gather | Critical | ✅ `GetStatus` in plan + `ApplyStatus` replacement | Yes |
| B | MineBlock actions stop after first dispatch | Critical | ✅ No completion event for MineBlock correlation | Yes |
| C | FindFlatArea picks distant tower | High | ✅ First scan finds area=0 on flat ground | Yes |
| D | `bot._client.chat` error | Medium | ✅ Already fixed in code (`bot.chat()`) | No — verify only |

---

## Issue A: Inventory Resets to Empty Mid-Gather

### Root Cause (Confirmed)

The gather plan produced by `GatherItemDecompose` in `HtnTaskLibrary.cs` is:

```
[SearchMemory → MineBlock(dirt, 10) → GetStatus]
```

In `DispatchActionsAsync`, all three actions are processed sequentially in one cycle:

1. **SearchMemory** — dispatched synchronously, completes immediately
2. **MineBlock** — dispatched fire-and-forget (1ms), adds correlation `PendingAction`
3. **GetStatus** — dispatched fire-and-forget. The JS `sendBotStatus()` reads `bot.inventory.items()` which returns the adapter's current snapshot. If items are mid-pickup (not yet in bot's inventory slots), the snapshot is empty/partial.

In `WorldStateProjector.ApplyStatus` (line ~100):

```csharp
b.SetInventory(NormalizeInventory(e.Inventory));
```

This **replaces** the entire inventory dictionary. Any items accumulated via `ApplyBlockMined`'s additive `AddInventoryItem` are wiped out.

**Evidence from session log:**
```
[21:22:32] Inventory +1 dirt -> total 10       ← BlockMinedEvent additive
[21:22:33] [action] GetStatus OK (0ms)          ← StatusEvent replaces inventory
[21:22:33] [plan] ... inventory: [dirt: 0/10]   ← Inventory now empty!
```

### Fix: Two-Layer Defense

#### Layer 1 (Primary) — `Agent.Core/WorldStateProjector.cs`

Make `ApplyStatus` inventory behavior **additive** for keys that already exist in the current inventory. This prevents the StatusEvent from wiping out accumulated items while still allowing new keys to be populated.

**Change in `ApplyStatus`:**
```csharp
// Current (replacement):
b.SetInventory(NormalizeInventory(e.Inventory));

// New (additive for existing keys):
foreach (var (key, count) in NormalizeInventory(e.Inventory))
    b.AddInventoryItem(key, count);
```

**Risk:** If the server clears the bot's inventory (e.g. `/clear`), this additive approach would NOT detect the clearance. However:
- The stale flag mechanism (`IsInventoryStale`) already handles this scenario via `SetGoal` marking inventory stale.
- A `/clear` triggers other state changes (chat event, possibly game mode) that would cause a replan + fresh GetStatus.
- The stale flag is cleared only by `ApplyStatus`, so an explicit GetStatus after `/clear` would still produce a correct fresh snapshot.

**Verdict:** Acceptable risk with compensating stale-flag mechanism.

#### Layer 2 (Secondary) — `WebUI.Blazor/AgentBackgroundService.cs`

Add a guard in `DispatchActionsAsync` to skip `GetStatus` when any fire-and-forget action is pending in the correlation tracker. This prevents GetStatus from dispatching at all during active mining/crafting.

**Change in dispatch loop** (after dequeue, before dispatch):
```csharp
if (action.Tool.Equals("GetStatus", StringComparison.OrdinalIgnoreCase)
    && _correlatedActions.Values.Any(a =>
        a.State == ActionLifecycle.Dispatched && IsFireAndForgetTool(a.ToolName)))
{
    logger.LogDebug("[dispatch] GetStatus skipped — pending fire-and-forget actions in-flight");
    continue;
}
```

**Why both layers:** Layer 1 fixes the data corruption even if GetStatus somehow dispatches. Layer 2 prevents unnecessary GetStatus calls from reaching the adapter during active work. Together they are robust.

#### Layer 3 (Verification) — `WebUI.Blazor/AgentBackgroundService.cs`

Add a `LogWarning` when a StatusEvent arrives with inventory count > 0 that is LOWER than the current projected inventory count. This flags potential inventory conflicts for debugging.

```csharp
case StatusEvent e:
    var incomingTotal = e.Inventory.Values.Sum();
    var currentTotal = _worldState.Inventory.Values.Sum();
    if (incomingTotal < currentTotal && currentTotal > 0)
        logger.LogWarning(
            "[inventory] StatusEvent inventory ({Incoming}) lower than projected ({Current}) — " +
            "possible inventory desync or server clear",
            incomingTotal, currentTotal);
    // ... existing correlation completion
    break;
```

---

## Issue B: MineBlock Actions Stop After First Dispatch

### Root Cause (Confirmed)

The MineBlock action is dispatched fire-and-forget to the JS adapter. In `index.js`, the `case 'mine':` handler:

1. Mines blocks in a `while (mined < count)` loop
2. Sends `blockMined` event per dig (with correlationId) — this IS happening
3. After the loop completes: only `logStructured('info', 'mine', 'complete', ...)` — **NO completion event is sent**
4. `break;` exits the switch case, `dispatch` returns normally

On the C# side in `ProcessEventsAsync`:

```csharp
case BlockMinedEvent e:
    // ... log inventory
    // Sprint 25 P0-D: blockMined is a partial-progress event for MineBlock.
    // Don't transition to Completed yet — the mine loop may continue.
    break;  // ← NO CompleteCorrelatedActionByTool("MineBlock") call!
```

The MineBlock correlation stays in `Dispatched` state forever. The fire-and-forget skip guard (`IsFireAndForgetTool + HasPendingActionOfTool`) prevents dispatching subsequent MineBlocks. The action eventually times out at 30s via `SweepTimedOutActions`, which:
1. Transitions MineBlock to `TimedOut`
2. Does NOT increment `_consecutiveFailures` (the timeout is non-fatal for fire-and-forget)
3. On the next replan (every 2s), a new MineBlock dispatches
4. Which times out again after 30s

**Result:** Bot mines ~1 block per 30+ seconds. Gather goals stall.

### Fix: Complete MineBlock Correlation on BlockMined Events

The design intent is that `blockMined` is "partial progress" — the JS loop continues mining. But since C# has no way to know when the JS loop finishes (no `mineComplete` event), we have two options:

#### Option B1 (Recommended): Complete correlation on each `blockMined` event

In `ProcessEventsAsync`, `case BlockMinedEvent e:` — add:

```csharp
case BlockMinedEvent e:
    var itemKey = e.Block.Contains(':') ? e.Block.Split(':')[1] : e.Block;
    logger.LogInformation("Inventory +{Count} {Block} -> total {Total}",
        e.Count, itemKey, _worldState.Inventory.GetValueOrDefault(itemKey));
    // Sprint 37: complete MineBlock correlation so subsequent MineBlocks can dispatch.
    // Each blockMined event represents one dig completion. The JS mine loop may continue
    // mining more blocks, but C# tracks it as one-block-per-action for dispatch pacing.
    CompleteCorrelatedActionByTool("MineBlock");
    break;
```

**Why this works:** The JS mine handler mines one block, sends `blockMined`, then loops to mine the next. The C# dispatch loop sees the MineBlock correlation complete and can dispatch the next MineBlock (which gets queued in JS behind the in-progress loop). The JS `cmdQueue` handles sequential processing — the next MineBlock starts when the current one finishes.

**Effect on plan pacing:**
- Before fix: 1 block per 30s (timeout-driven)
- After fix: Blocks mined continuously — each blockMined triggers the next MineBlock dispatch within the next replan cycle (2s max)

**Risk:** Multiple MineBlocks could queue up in JS if C# replans faster than JS can mine. Mitigation: `MinReplanIntervalSeconds=2` limits this to one extra dispatch per 2s. The JS queue depth stays at 0-1 in practice.

#### Option B2 (Alternative): Add `mineComplete` event from JS + handle in C#

In `index.js`, after the `while` loop in `case 'mine':`:

```js
sendEvent('mineComplete', { block: shortName, mined, targetCount: count, correlationId });
```

In `ProcessEventsAsync`, add a new case for `mineComplete`. This requires adding a new event type.

**Verdict:** Option B2 is cleaner architecturally but requires more changes (new event types in both JS and C#). Option B1 is simpler and sufficient — go with B1.

### Additional: Diagnostic Logging for BlockMined Correlation

Add a log when a `blockMined` event arrives but NO MineBlock correlation is pending. This would indicate a race condition or orphaned event:

```csharp
case BlockMinedEvent e:
    // ... existing log
    // Sprint 37: diagnostic — check if MineBlock correlation exists
    var hasMineBlockPending = _correlatedActions.Values.Any(a =>
        a.State == ActionLifecycle.Dispatched &&
        a.ToolName.Equals("MineBlock", StringComparison.OrdinalIgnoreCase));
    if (!hasMineBlockPending)
        logger.LogWarning(
            "[correlation] BlockMinedEvent for {Block} arrived but no MineBlock pending — " +
            "possible orphaned event or completed/abandoned goal",
            itemKey);
    // ... rest of handler
```

---

## Issue C: FindFlatArea Picks Distant Tower

### Root Cause (Confirmed)

The unstaged changes include:
1. Proximity weighting (`FLAT_SCORE_WEIGHTS` with `proximity: 0.30`)
2. `scanOriginX/Y/Z` parameter support
3. Zero-area fallback (bot position after 2 zero-area scans)

**However**, the session log shows the first scan (radius=30) returns area=0 even though the bot is standing on flat grass at (-280,63,103). The proximity fix only helps when multiple candidates exist within a single scan — it does NOT help when the scan finds zero candidates.

The height map scan works by checking `yAbove=10` down to `yBelow=16` relative to `scanCenter.y`. If `scanCenter.y = 63` (bot's Y), the scan checks y=73 down to y=47. At the bot's position, the ground block is at y=62 (grass block). But the height map requires:
1. A non-air block with `boundingBox === 'block'`
2. An air block ABOVE it (or boundingBox === 'empty')

So at position (-280, 62, 103), block = grass. Above at y=63 is air. That should match. But wait — the scanning loop goes from `scanCenter.y + yAbove` (73) DOWN to `scanCenter.y - yBelow` (47). At y=63, the block might be `grass_block` with `boundingBox = 'block'`. Above at y=64 would be `air`. So this should work...

Unless the chunks at the bot's position aren't fully loaded yet. The chunk load wait happens before the scan, but it's only waiting for chunks within `chunkRadius = Math.ceil(r/16) + 1 = ceil(30/16) + 1 = 3`. A 30-block radius covers about 4x4 chunks. The chunk wait is centered on the bot's position at the time of wait. But between the chunk wait and the actual scan, the bot may have moved or chunks may have unloaded.

The most likely cause: **The bot is in an area where `bot.world.getColumn()` returns null for certain chunks even after the wait**, causing `blockAt()` to return null for ground-level blocks, and the height map ends up empty.

### Fix: Three Changes

#### 1. Add Diagnostic Logging for Winning Candidate — `index.js`

In the `findFlatArea` handler, after scoring, add winning candidate details to the console log:

```js
if (bestCandidate) {
    const distFromCenter = Math.sqrt(
        ((bestCandidate.x + bestCandidate.maxX) / 2 - scanCenter.x) ** 2 +
        ((bestCandidate.z + bestCandidate.maxZ) / 2 - scanCenter.z) ** 2
    );
    console.log(
        `[findFlatArea] best at (${bestCandidate.x},${bestCandidate.y},${bestCandidate.z})` +
        ` area=${bestCandidate.area} yRange=${bestCandidate.yRange}` +
        ` compact=${bestCandidate.compactness} score=${bestScore.toFixed(1)}` +
        ` distFromCenter=${distFromCenter.toFixed(1)} (${scanElapsed}ms)`
    );
}
```

#### 2. Add Direct Ground-Height Check Before Scan — `index.js`

Before the main scan loop, check if the block directly below the bot's feet is solid ground. If so, use it as the initial height map entry rather than scanning from scratch:

```js
// Sprint 37: direct ground check before full scan.
// If the bot is already standing on solid ground, seed the height map with
// the bot's position so ground-level candidates don't get missed.
const groundBlocks = new Map();
const feetPos = toVec3(botPosObj.x, botPosObj.y - 1, botPosObj.z);
const feetBlock = bot.blockAt(feetPos);
if (feetBlock && feetBlock.boundingBox === 'block' && !LIQUID_BLOCK_NAMES.has(feetBlock.name)) {
    const aboveFeet = bot.blockAt(toVec3(botPosObj.x, botPosObj.y, botPosObj.z));
    if (!aboveFeet || aboveFeet.name === 'air' || aboveFeet.boundingBox === 'empty') {
        groundBlocks.set(`${botPosObj.x},${botPosObj.z}`, {
            x: botPosObj.x, z: botPosObj.z, y: botPosObj.y  // surface = bot's feet Y
        });
        console.log(`[findFlatArea] direct ground hit at (${botPosObj.x},${botPosObj.y},${botPosObj.z}) block=${feetBlock.name}`);
    }
}

// Merge groundBlocks into heightMap after the scan populates
// ... existing height map scan code ...
// After scan:
for (const [key, col] of groundBlocks) {
    if (!heightMap.has(key)) heightMap.set(key, col);
}
```

This ensures the height map always contains at least one entry at the bot's position — the block the bot is standing on. If the chunk scan doesn't find that block (due to chunk load issues), this fallback provides it.

#### 3. Validate `chunkRadius` Calculation — Diagnostic

Add a log showing the scan radius and chunk radius to verify they're correct:

```js
const chunkRadius = Math.ceil(r / 16) + 1;
console.log(`[findFlatArea] scanning radius=${r} chunkRadius=${chunkRadius} center=(${scanCenter.x},${scanCenter.y},${scanCenter.z})`);
```

### Verification Criteria for Issue C

1. Bot on flat ground → first scan should find area >= 25 (ground-level)
2. Bot on flat ground → second scan (retry, radius=48) should prefer nearby ground over distant structures
3. After 2 zero-area scans → bot position fallback should trigger

---

## Issue D: `bot._client.chat is not a function`

### Investigation Result

**The code already uses `bot.chat()`** at line 846 of `index.js`. No reference to `bot._client.chat` exists in the current file. The `_client` property is not referenced anywhere in the Mineflayer adapter.

Possible explanations for the error in the log:
1. The error was from a previous version of `index.js` that used `bot._client.chat()` and has since been fixed
2. The error came from a different script or plugin loaded by Mineflayer (e.g., a third-party plugin)
3. A transient error during script loading

**Fix:** None needed for the main code path. Add a try-catch around the chat dispatch for defensive robustness:

```js
case 'chat':
    try {
        bot.chat(args.message ?? '');
    } catch (e) {
        console.error(`[chat] failed to send chat message: ${e.message}`);
        sendEvent('error', { action: 'chat', message: e.message, correlationId });
    }
    break;
```

### Verification
- [ ] Start adapter, send a chat command → bot responds in-game
- [ ] Check adapter logs for `[chat]` errors

---

## Unstaged Changes: Verification Required

The following changes are in the working tree but NOT deployed. They must be verified after restart:

| File | Change | Verification |
|------|--------|-------------|
| `gameModeState.js` | Fix falsy guard for numeric game mode (0=survival) | Start in survival → logs show `gamemode detected: survival` |
| `index.js` (game mode) | `normalizeGameMode` handles numeric values | Same as above |
| `index.js` (proximity) | `FLAT_SCORE_WEIGHTS` with `proximity: 0.30` | Build near tower → bot picks nearby ground |
| `index.js` (scanOrigin) | `findFlatArea` accepts `scanOriginX/Y/Z` | Build at coords → scan centers there |
| `BuildGoalDecomposer.cs` | `requireOrigin=true` always | All builds go through FindFlatArea first |
| `AgentBackgroundService.cs` | Zero-area fallback + diagnostic logging | 2 zero scans → bot position used as origin |
| `AgentBackgroundService.cs` | Chat response logging for CreateGoal/NavigateTo | Console shows `[chat] bot: ...` for all intents |

---

## Implementation Order

### Phase 1: JS Changes (MineflayerAdapter)

1. `index.js`:
   - Add `mineComplete` diagnostic logging (Issue B diagnostic)
   - Add direct ground-height check before findFlatArea scan (Issue C)
   - Add winning candidate distance in findFlatArea output (Issue C)
   - Add try-catch around chat dispatch (Issue D robustness)

### Phase 2: C# Changes (Core Logic)

2. `Agent.Core/WorldStateProjector.cs`:
   - Make `ApplyStatus` inventory additive for existing keys (Issue A Layer 1)

3. `WebUI.Blazor/AgentBackgroundService.cs`:
   - Skip GetStatus when pending fire-and-forget actions exist (Issue A Layer 2)
   - Complete MineBlock correlation on each blockMined event (Issue B)
   - Add inventory-desync warning log on StatusEvent (Issue A Layer 3)
   - Add orphaned blockMined diagnostic log (Issue B diagnostic)

### Phase 3: Validation

4. Build and run tests
5. Create tasks in MemorySmith
6. Deploy and verify

---

## Validation Plan

### Build Validation

```powershell
# Build and run ALL tests
cd d:\@Repos\MemorySmith.Agent
dotnet build
dotnet test --no-build
```

**Expected:** All 504+ tests pass. Zero compiler warnings.

### Unit Test Coverage

Add or verify tests for:

| Scenario | File | What to Test |
|----------|------|-------------|
| `ApplyStatus` additive inventory | `WorldStateProjectorTests.cs` | StatusEvent with existing keys does not reset them |
| GetStatus skip guard | `AgentBackgroundServiceTests.cs` | GetStatus skipped when MineBlock pending |
| MineBlock correlation on blockMined | `AgentBackgroundServiceTests.cs` | BlockMinedEvent completes MineBlock correlation |
| Gather goal completion with additive inventory | `GenericGatherGoalTests.cs` | Goal completes correctly when inventory populated by both paths |

### Runtime Verification

#### Test 1: Gather 10 Dirt (Survival)
1. Start agent (survival mode)
2. `leo gather 10 dirt`
3. **Expected:**
   - Bot starts mining dirt
   - `[action] MineBlock OK` appears
   - `Inventory +1 dirt -> total N` increments
   - No `[correlation] MineBlock TIMED OUT` appears
   - Goal completes with `inventory: [dirt: 10/10]`
   - No inventory resets during mining

#### Test 2: Build House Near Tower (Survival)
1. Stand on flat ground near a tower
2. `leo build a house`
3. **Expected:**
   - findFlatArea scan shows ground-level candidate (not tower top)
   - Bot builds near spawn, not on tower

#### Test 3: Inventory Stability During Gather
1. Start gather goal
2. While bot is mining, manually check console for:
   - No `[inventory] StatusEvent inventory lower than projected` warning
   - No `[correlation] MineBlock TIMED OUT` warning
   - Inventory count monotonically increases

#### Test 4: Chat API
1. Send any command to bot
2. **Expected:** Bot responds in-game chat
3. No `Game error [chat]:` in logs

### Edge Cases

| Scenario | Expected Behavior |
|----------|-------------------|
| `/clear` during gather | Stale flag mechanism detects, replan issues GetStatus |
| Adapter restart mid-gather | Reconnect triggers new goal evaluation |
| Bot in creative mode | Gather completes immediately (existing behavior) |
| Zero-area scan on ocean | After 2 scans, fallback to bot position |
| Two gather goals in sequence | Second goal starts with fresh inventory state |

---

## Deployment Checklist

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

# 5. Verify in console:
#    - "[mc] bot spawned" appears
#    - "gamemode detected: survival" or "creative"
#    - No "[mc] error:" messages
```

---

## Task Tracking

Create the following tasks in MemorySmith:

| Key | Title | Priority | Type |
|-----|-------|----------|------|
| TSK-NNN4 | GetStatus resets inventory during active gather | Critical | Bug |
| TSK-NNN5 | MineBlock correlation never completes | Critical | Bug |
| TSK-NNN6 | Mineflayer chat API compatibility | Medium | Bug |

Task JSON files are ready at `Data/Tasks/` (created by previous agent).

---

## Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Additive inventory masks `/clear` | Low | Low | Stale flag mechanism; warning log |
| Multiple MineBlocks queue in JS | Low | Low | MinReplanInterval=2s limits dispatch rate |
| Proximity weighting still chooses tower if ground scan returns area=0 | Medium | High | Direct ground check + zero-area fallback |
| Unstaged changes conflict with new fixes | Low | Medium | All changes in same sprint; build validates |
