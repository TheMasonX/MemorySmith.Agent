# MemorySmith.Agent — Deep-Dive Audit: Delta Report #4
**Scope:** Continuation of the audit series. This pass covered `Agent.Memory/*` (full), `Agent.Construction/*` (full), the `NextSteps` multi-step goal-chaining branch of `HandleChatEventAsync`, `ToolDispatcher.cs` (dispatch path, full), and the remaining unreviewed `MineflayerAdapter` modules (`movements.js`, `gameModeState.js`, `stopState.js`) — cross-checked against `Data/Tasks/*.json` and the three prior delta reports.
**Format:** Deltas only.

---

## Summary

| # | Type | Item | Severity | Confidence |
|---|------|------|----------|------------|
| 1 | New | `movements.js`'s cached `Movements` singleton survives bot reconnects, silently binding to a stale/disconnected `bot` object | **High** | 88% |
| 2 | New | `NextSteps` multi-step goal-chaining loop has no per-call error handling — any exception silently discards the entire sequence with poor diagnostics | Medium-High | 90% |
| 3 | New, cosmetic/consistency | `creativeProvider.js` is the only `MineflayerAdapter` module using CommonJS (`require`/`module.exports`) while all 6 sibling extracted modules use ESM | Low | 95% |

Also explicitly re-verified and ruled out: `RestMemoryGateway.GetPageAsync` (and `GetPageTool`, which calls it) have no local try/catch, but this is safe — `ToolDispatcher.CallAsync`'s Sprint-25-P0-C catch-all converts any tool exception into a graceful `ToolResult(false, ...)`, so this is not the "crashes the tool loop" pattern TSK-0088/TSK-0193 fixed elsewhere (that pattern was specifically about internal repository classes bypassing the ToolDispatcher safety net entirely, which is correctly fixed per TSK-0088 — verified the fix is in place in `MemorySmithBlueprintRepository.GetAsync`). `BlueprintParser.Parse` was checked for un-caught exceptions and found to be defensively written (`TryParse`-based throughout, no `throw` statements) — it does not contribute additional risk to Finding 2.

---

## 1 — Stale `Movements` singleton survives bot reconnection (High, 88%)

`MineflayerAdapter/movements.js` implements a module-level lazy singleton, explicitly justified in its own docstring as safe because "the pathfinder calls `updateCollisionIndex()` internally before each path computation":

```js
let _instance = null;

export function createMovements(bot) {
  if (!_instance) {
    const m = new Movements(bot);
    m.canOpenDoors = true;
    _instance = m;
  }
  return _instance;   // bot argument silently ignored on every call after the first
}
```

Separately, `index.js` implements **in-process bot reconnection** (Sprint 56, TSK-0263 — "Reconnect-capable bot creation... re-create the bot... without restarting the process"):

```js
let bot;
function connectBot() {
  _reconnectAttempts = 0;
  bot = mineflayer.createBot(botOpts);   // ← a brand-new bot object
  bot.loadPlugin(pathfinder);
  registerBotEventHandlers();
  return bot;
}
...
bot.on('end', (reason) => {
  ...
  connectBot();   // called again on disconnect, line 425
});
```

**The bug:** `Movements` (from `mineflayer-pathfinder`) is constructed with a reference to a specific `bot` instance and uses that reference internally for entity/world lookups during path computation. After any reconnect — network blip, kick, crash-recovery, anything that fires `bot.on('end', ...)` — `connectBot()` creates an entirely new `bot` object, but the *next* call to `createMovements(newBot)` from any action handler (`move`, `mine`, `place`, `wander`, `findFlatArea`, etc. — all 7 call sites identified in delta report #2, finding #6) will find `_instance` already set and return the **old** `Movements` instance, silently discarding the new `bot` argument. The pathfinder's per-path `updateCollisionIndex()` refresh (the safety argument in the docstring) addresses *world-state* staleness (blocks changing between calls) — it does not address *bot-object-identity* staleness, which is a different failure mode entirely and isn't mitigated by anything in the current design.

**Likely symptoms post-reconnect:** silent pathfinding misbehavior, exceptions from the stale `Movements` instance touching a torn-down/disconnected bot's internals (e.g., accessing `bot.entity` or `bot.world` on a bot that's no longer connected), or subtly incorrect collision/entity data — all of which would be very difficult to diagnose from symptoms alone, since nothing logs or errors at the point of staleness; it would just quietly use the wrong object.

**Why untracked:** `TSK-0269` (Backlog, Low) touches `movements.js` but only for an unrelated cosmetic import-style inconsistency (named vs. default import of `Movements`). No task references reconnection interaction with the singleton.

**Recommendation:** Either (a) reset `_instance = null` inside `connectBot()` right after assigning the new `bot`, forcing the next `createMovements()` call to rebuild against the fresh bot, or (b) key the cache by bot identity (e.g., `if (!_instance || _instance.bot !== bot) { ... }`, if `Movements` exposes its bound bot — needs a quick check against the `mineflayer-pathfinder` version in use) so a stale instance is detected rather than assumed valid. Option (a) is simpler and lower-risk given `connectBot()` is the sole reconnection entry point. This should be sequenced together with (or immediately after) whatever comes out of TSK-0337/TSK-0339 (WebSocketBridge/Node reconnect backoff), since fixing the connection-level reconnect without fixing this downstream staleness would leave reconnection only partially functional in practice.

---

## 2 — `NextSteps` multi-step goal chaining has no per-call error isolation (Medium-High, 90%)

Inside `HandleChatEventAsync`'s `"gather"/"build"/"craft"/"smelt"/"place"` case (`AgentBackgroundService.cs`, Sprint 54 TSK-0205 multi-step chaining block):

```csharp
if (intent.NextSteps is { Count: > 0 } && goalFactory is not null)
{
    var allSteps = new List<IGoal>();
    var firstGoal = await goalFactory.CreateAsync(goalRequest.GoalName, goalRequest.Parameters, ct);
    if (firstGoal is not null) allSteps.Add(firstGoal);

    foreach (var stepCmd in intent.NextSteps.Take(TaskSequenceGoal.MaxSteps - 1))
    {
        var stepRequest = IntentManager.ParseCommandString(stepCmd);
        if (stepRequest is not null)
        {
            var stepGoal = await goalFactory.CreateAsync(stepRequest.GoalName, stepRequest.Parameters, ct);
            if (stepGoal is not null) allSteps.Add(stepGoal);
        }
    }
    if (allSteps.Count > 1) { SetGoal(new TaskSequenceGoal(allSteps)); }
    else if (allSteps.Count == 1) { SetGoal(allSteps[0]); }
}
else
{
    await TryCreateGoalFromChatAsync(goalRequest, ct);
}
```

Neither `goalFactory.CreateAsync` call inside this block has a local `try`/`catch`. Compare to the `else` branch's `TryCreateGoalFromChatAsync`, which wraps its single `CreateAsync` call in its own `try`/`catch` and logs `"Error creating goal from chat: {Name}"` with the specific goal name.

**Consequence of an exception at any point in the `NextSteps` block** (e.g., `firstGoal`'s creation, or the 3rd of 5 chained steps): the exception propagates out of `HandleChatEventAsync` entirely — past all the already-successfully-created steps in `allSteps` — up to `ChatConsumerAsync`'s outer catch:

```csharp
catch (Exception ex) { logger.LogError(ex, "Error processing chat event in consumer."); }
```

which is generic (doesn't process doesn't crash) but loses **all** context: which step failed, which goal name, how many steps had already succeeded, and the entire `allSteps` list built so far is discarded — even if 4 of 5 steps constructed cleanly. The player also receives no feedback at all (not even the "Sorry, I don't know how to do that yet." message the null-goal path provides) — from the player's perspective, the compound command is just silently ignored.

**Realistic trigger surface today (moderate, not high-frequency):** the narrow HTTP-related exceptions inside `_blueprintRepository.GetAsync`/`_itemRegistry.GetAsync` are already caught at the repository layer (TSK-0088, verified in place), so this doesn't fire on ordinary transient network blips. It would fire on any *other* exception type surfacing from a `CreateAsync` branch — e.g., an unexpected `ArgumentException`/`NullReferenceException` from a goal constructor, a `JsonException` from a malformed cached blueprint file, or any future code added to `GoalFactory.CreateAsync` that doesn't anticipate being called in a loop like this.

**Recommendation:** Wrap each `CreateAsync` call in the `NextSteps` loop in its own try/catch (mirroring `TryCreateGoalFromChatAsync`'s pattern), logging the specific step/goal name on failure and — the more important behavioral choice — deciding whether a mid-sequence failure should (a) skip just that step and continue with the rest, or (b) abort the whole sequence but still surface a specific player-facing message and preserve the steps that *did* succeed up to that point rather than discarding them silently. Either is better than the current all-or-nothing silent failure. No existing task covers this (`TSK-0205`, `TSK-0236`, `TSK-0274`, `TSK-0307` all touch `TaskSequenceGoal`/`NextSteps` machinery but none address error isolation within the chaining loop itself).

---

## 3 — `creativeProvider.js` is the only CommonJS module among ESM siblings (Low, 95%)

`MineflayerAdapter/package.json` declares `"type": "module"`, and `index.js` plus five of its six extracted sibling modules (`gameModeState.js`, `vec3.js`, `config.js`, `logger.js`, `movements.js`, `stopState.js`) consistently use ES Module syntax (`import`/`export`). `creativeProvider.js` alone uses CommonJS:

```js
const { logStructured } = require('./logger');
...
module.exports = { ensureCreativeItem, ensureCreativeItems };
```

This works correctly today only because `index.js` explicitly shims `require` via `createRequire(import.meta.url)` (line 42-43) specifically to support this one call site (plus one npm-package version lookup at line 329) — Node's native CJS/ESM interop handles the mixed load correctly, so this is **not a functional bug**, just a consistency gap in an otherwise clean modularization pattern established by `TSK-0166`.

**Recommendation:** Convert `creativeProvider.js` to ESM (`import { logStructured } from './logger.js'; ... export { ensureCreativeItem, ensureCreativeItems };`) for consistency with every other module extracted under the same modularization effort. Trivial, zero behavior change, removes one of the two remaining reasons the `createRequire` shim exists in `index.js` (the other being the `mineflayer/package.json` version lookup, which is a legitimate use of `require` for reading an npm package's own metadata and doesn't need to change). Low priority — bundle with other `TSK-0166`-adjacent cleanup (per delta report #2, finding #6) rather than filing separately.

---

## Assumptions & Open Questions (this pass)

1. Finding 1's exact failure mode (exception vs. silent misbehavior) depends on `mineflayer-pathfinder`'s internal implementation of `Movements`/pathfinding against a torn-down bot object — not verified against the actual library source in this pass (would require pulling the `mineflayer-pathfinder` package source, out of scope for this repo-focused audit). The core claim — that the cached instance is never invalidated and the `bot` argument is discarded after the first call — is verified directly from `movements.js`'s source and is not in question; only the downstream blast radius is an inference rather than an observed crash/log.
2. Have not yet reviewed `ChatInterpreter.cs` (the non-LLM regex-based interpreter) or the bulk of `LlmChatInterpreter.cs` (709 lines) — still the largest unreviewed surface in `Agent.Planning`, flagged again as the top candidate for the next pass.
3. Have not reviewed `Agent.World.Minecraft`'s remaining files beyond `WebSocketBridge.cs` (already covered in delta report #2), `WebUI.Blazor/Dtos.cs`, `AgentHub.cs`, or `Options/SafetyOptions.cs` this pass.
