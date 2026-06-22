# Sprint 37 — Debug Tasks

## TSK-NNN1: GatherItem goal completes prematurely with 0 inventory

**Type:** Bug  
**Priority:** High  
**Status:** Ready  
**Labels:** `gather`, `goal-completion`, `debugging`

### Symptom
`[goal] completed: Gather:dirt | inventory: [dirt: 0/100]` — the gather goal for 100 dirt
completed in the same timestamp as goal creation, with 0 dirt in inventory. No mining
actions were dispatched.

### Steps to Reproduce
1. Start the agent (connects to Minecraft server)
2. Say "leo gather 100 dirt"
3. Observe: goal completes immediately with inventory still at 0

### Root Cause Investigation
Most likely: `GenericGatherGoal.IsComplete` returns `true` because
`WorldState.IsCreativeMode` is `true`. This would bypass the inventory check entirely.

Possible causes:
1. **Game mode detection failure**: If the Minecraft server sends game mode as a numeric
   value (0=survival, 1=creative), the JS `normalizeGameMode` in `gameModeState.js`
   checks `mode.includes('creative')` on the string "0" or "1", which never matches.
   Returns `null`, so no `gameMode` event is sent → `WorldState.GameMode` stays `null`.
2. **Race condition**: A StatusEvent from initial `sendBotStatus()` processed between
   `SetGoal` and the first plan cycle could clear the stale flag before inventory is
   populated.

### Diagnostics Added (Sprint 37)
- `AgentBackgroundService.DispatchActionsAsync` now logs game mode, stale flag, and
  inventory count when a gather goal completes.
- `AgentBackgroundService.TryCompleteCurrentGoalFromWorldUpdate` now logs the same
  diagnostics for event-path goal completion.
- `AgentBackgroundService.HandleChatEventAsync` now logs `[chat] bot: {Response}` for
  `CreateGoal` and `NavigateTo` intents (previously only logged for `QueryStatus`).

### Next Steps
1. Reproduce with the diagnostic logging in place
2. Check logs for `gameMode=` value — if `null` or `creative`, the root cause is
   game mode detection
3. If game mode is `survival` or `null`, check the stale-flag and inventory flow

---

## TSK-NNN2: findFlatArea builds on tower instead of nearby ground

**Type:** Bug  
**Priority:** High  
**Status:** Ready  
**Labels:** `build`, `findFlatArea`, `scanner`

### Symptom
When saying "build a house" without coordinates, the bot runs to a nearby tower
structure and builds on its roof instead of finding nearby flat ground.

### Root Cause
The `findFlatArea` scanner scored candidates solely by area size, compactness, and
flatness — with **no distance penalty**. A tower top with area=47 at distance ~40
scored higher than any smaller ground-level patches.

### Fix Applied (Sprint 37)
1. **Proximity weight (0.30)** added to `FLAT_SCORE_WEIGHTS` in `index.js`, with
   area/compactness/flatness reduced proportionally. Closer flat areas now score
   significantly higher.
2. **On retry (area=0)**, `minArea` is reduced to 10 (from 25) so small nearby patches
   are accepted instead of expanding radius to find far structures.
3. **`scanOriginX/Y/Z` parameter** added to findFlatArea — when the C# side passes
   explicit coordinates, the scan centers there instead of at the bot's position.

### Verification
- [ ] Restart Mineflayer adapter (process was running old code)
- [ ] Say "build a house" near uneven terrain — should find nearest flat patch
- [ ] Say "build a house at X Y Z" — should scan near those coords, not build directly

---

## TSK-NNN3: Chat responses not visible in console logs

**Type:** Bug  
**Priority:** Low  
**Status:** Ready  
**Labels:** `chat`, `logging`

### Symptom
When the bot responds to a user command (e.g. "Gathering 100x dirt."), the response
is sent in-game but not logged to the C# console.

### Root Cause
`HandleChatEventAsync` only logs `[chat] bot: {Response}` for `QueryStatus` intents.
`CreateGoal` and `NavigateTo` intents enqueue the response as a Chat action but
never log it to the console.

### Fix Applied (Sprint 37)
Added `logger.LogInformation("[chat] bot: {Response}", pendingResponse)` for
`CreateGoal` and `NavigateTo` cases.

---

## Test Plan

### Prerequisites
1. Restart both the Mineflayer adapter and the C# agent
2. Ensure the Minecraft world is set to **survival** mode
3. Player should be standing on flat ground near some uneven terrain

### Test 1: Gather goal (diagnostic)
1. Start agent, wait for connection
2. Say "leo gather 100 dirt"
3. Check console logs for:
   - `[chat] bot: Gathering 100x dirt.`
   - `[goal] set: Gather:dirt`
   - `[goal] completed:` with `gameMode=` and `stale=` values
4. **Expected**: Goal does NOT complete instantly. Bot mines dirt.

### Test 2: Build without coordinates (proximity scoring)
1. Stand on or near flat ground (not on a structure)
2. Say "build a house"
3. **Expected**: Bot finds nearest flat ground, not a distant tower top

### Test 3: Build with coordinates (scanOrigin)
1. Say "build a house at <coords>"
2. **Expected**: Bot scans for flat ground near those coords and builds there

### Test 4: Chat response logging
1. Say any command that triggers a bot response
2. Check console for `[chat] bot: ...`
3. **Expected**: Bot chat responses appear in console logs
