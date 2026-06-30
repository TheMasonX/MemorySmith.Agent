# Sprint 56 Wave A — Handoff

**Date:** 2026-06-29 | **Branch:** `dev/round-3` | **Baseline:** `aa4af10`

## Summary

Sprint 56 Wave A delivered 6 adapter bug fixes from the external Mineflayer audit (TSK-0260 through TSK-0266), plus one critical scope regression fix. The Wave A1 safe fixes (TSK-0260, TSK-0261) and vec3 fix (TSK-0262) are verified correct. However, live testing revealed a scope regression from the reconnect refactoring (TSK-0263) and 4 additional issues that need resolution before building works.

## Completed

| Task | Description | Status |
|---|---|---|
| TSK-0260 | `bot.bestHarvestTool` → `bot.pathfinder.bestHarvestTool` + remove `.item` | ✅ Done |
| TSK-0261 | `recipesFor(..., null)` → `recipesFor(..., true)` | ✅ Done |
| TSK-0262 | vec3.js: stop flooring in ctor, `.floored()` returns NEW object | ✅ Done (verified) |
| TSK-0263 | JS-side reconnect with exponential backoff | ✅ Done |
| TSK-0264 | Gate `agentSocket` behind auth handshake | ✅ Done |
| TSK-0265 | Ground check before scaffold fallback `goto(x+2)` | ✅ Done |
| TSK-0266 | Pre-dig hazard check (gravity/liquid above target) | ✅ Done |

## Regression Fixed

**`botPos`/`sendBotStatus` scope regression** (commit `aa4af10`): The `registerBotEventHandlers()` refactoring for TSK-0263 accidentally encapsulated `botPos`, `sendBotStatus`, `findGroundY`, `scanNearbyEntities`, `scanBlockBelow`, and `HOSTILE_MOB_NAMES` inside the function scope. This made them invisible to `dispatch()`, causing `botPos is not defined` / `sendBotStatus is not defined` on every action. Fixed by closing `registerBotEventHandlers()` after the event handlers and moving helper functions to module scope.

## New Issues Found (Live Testing)

### P0: PlaceBlock actions time out — no completion event from adapter (TSK-0270)

Place actions are dispatched to the adapter but never complete. Agent log shows `place TIMED OUT after 8.3s — no result event received`. Same pattern for GetStatus. Hypothesis: the vec3.js fix or WS auth gate may have changed behavior — needs investigation.

### P1: Build dispatches 218 individual place actions simultaneously (TSK-0271)

`BuildGoalDecomposer.DecomposeBuild` returns ALL 218 blueprint blocks as individual PlaceBlock actions. All are dispatched at once, flooding the adapter. Need batch size limiting or sequential dispatch.

### P1: Creative mode inventory provisioning broken (TSK-0272)

`place:cobblestone not in inventory` despite bot being in creative mode. `creativeProvider.js`'s `ensureCreativeItem` isn't providing items when needed for building.

### P2: GetStatus times out (TSK-0273)

`GetStatus TIMED OUT after 10.4s — no result event received`. Inventory never syncs, goals can never complete.

## Known Issues (Pre-existing)

### P2: Emergency stop kills in-flight place actions (Sprint 55)

`"The goal was changed before it could be completed!"` — From logs at 21:41:15 and 21:41:25. When the goal completes early (before the adapter finishes the action), `DispatchActionsAsync` dispatches `StopNow` which kills the in-flight place. The C# side gets `ErrorEvent` and `ActionFailedEvent`. This is the same issue documented in Sprint 55 Wave B handoff.

### P2: Build auto-skips blocks after 3 consecutive timeouts

Logs at 22:24:22 show `auto-skipping block Small Survival House #N after 3 consecutive timeouts`. This is a safety mechanism, but combined with P0 (all actions timing out) it cascades through the entire blueprint.

## Observed Behaviors (Working)

- ✅ Entity observation: all mobs detected with hostility flags
- ✅ Lightning command: `/summon minecraft:lightning_bolt <x> <y> <z>` works via chat
- ✅ Entity-targeted navigation: "go to nearest slime" navigates to entity coordinates
- ✅ Dashboard: entity list, block below, log ordering
- ✅ LLM prompt: combat guidance, navigate rules
- ✅ Module loads OK after all fixes

## Files Changed This Sprint

| File | Changes |
|---|---|
| `MineflayerAdapter/index.js` | TSK-0260, 0261, 0263, 0264, 0265, 0266 + scope fix |
| `MineflayerAdapter/vec3.js` | TSK-0262 — fractional offset fix |
| `Data/Tasks/*.json` | TSK-0260 through TSK-0273 task records |

## Next Steps (Sprint 56 Wave A Continuation)

1. **Fix TSK-0270 (P0):** Investigate why place actions aren't sending completion events. Check adapter console for errors. May need to roll back specific changes to isolate.
2. **Fix TSK-0271 (P1):** Add batch size limit (10 blocks) to BuildGoalDecomposer.
3. **Fix TSK-0272 (P1):** Debug creative provisioning in the place action flow.
4. **Fix TSK-0273 (P2):** Debug GetStatus — likely same root cause as TSK-0270.
5. **E2E validation:** Run a full build in creative mode with all fixes applied.
6. **Do NOT remove Sprint 41/52 placement workarounds** until vec3 fix is E2E validated (per council decision).

## Council Decisions Referenced

- `Data/Pages/council/mineflayer-external-audit-council-2026-06-29.md` — Bug triage and Wave A planning
- Vec3 Option B adopted: fix shim, don't swap to npm package
- Workarounds preserved until E2E validated
