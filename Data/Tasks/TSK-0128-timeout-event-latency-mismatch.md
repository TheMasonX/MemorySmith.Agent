# TSK-0128: Fix Action Timeout vs Event Latency Mismatch

**Status:** Backlog
**Priority:** High
**Sprint:** 53
**Discovered:** Runtime observation 2026-06-27 (08:48-08:50 run)
**Related:** TSK-0125 (per-block tracking)

## Summary

`BlockPlacedEvent` arrives 5-15 seconds after C# dispatch, but C# timeout fires at 6-8 seconds. The correlation context (`_placeBlockContexts`) is cleaned up before the event arrives, so `AdvanceBuildCheckpoint` can't find the block index and the checkpoint never advances.

**Result**: Blocks ARE placed in Minecraft (confirmed by late BlockPlacedEvents), but C# replans and re-emits them anyway → adapter says "already placed" → events arrive with no correlation → checkpoint stuck → infinite stall loop.

## Evidence

From the 08:48:55 – 08:49:23 timeframe:

| Dispatch Time | Block | Timeout (C#) | Event Arrival | Gap |
|---|---|---|---|---|
| 08:48:55 | oak_planks (142,6,271) | 08:49:08 (11s) | 08:49:23 (28s later) | Event 15s after dispatch, 15s after timeout |
| 08:49:04 | oak_planks (142,6,271) | 08:49:15 (11s) | 08:49:23 (19s later) | Event 8s after timeout |
| 08:49:35 | oak_planks (147,6,273) | 08:49:40 (6s) | 08:49:57 (22s later) | Event 16s after timeout |

The adapter takes 5-15 seconds to pathfind + place, but the C# timeout is 6-8 seconds (configured as `actionTimeout=30s` but PlaceBlock gets a shorter sweep timeout). Events arriving after cleanup are silently discarded — `BlockPlacedEvent` handler at line 681 has no `_placeBlockContexts` entry, so `AdvanceBuildCheckpoint` can't find the blueprint/blockIndex.

## Affected Blocks (confirmed from run)

Blueprint Y=2 (mid-wall), world Y=6, back wall area Z=274-277:
- Blocks 97-119: oak_planks at (139-147,6,277), glass_pane at (139,6,276) and (147,6,276), torch at (145,6,275), oak_planks at (139-147,6,274-275)

## Proposed Fix

### Option A: Extend sweep timeout for PlaceBlock
Increase `SweepTimedOutActions` timeout from 6-8s to 20s for PlaceBlock specifically. Simple but increases stall detection latency.

### Option B: Re-correlate late events
When a `BlockPlacedEvent` arrives with no matching `_placeBlockContexts` entry, don't discard it. Instead: (a) look up the position in the blueprint to find the block index, (b) write per-block status directly, (c) log clearly that a late event was recovered.

Depends on TSK-0125 (per-block tracking) to be feasible.

### Option C: Adapter-side correlation
Send the correlationId back with every `BlockPlacedEvent`. The adapter already receives it in the place action args — ensure it's echoed back in the event. This makes correlation reliable even if the C# context was cleaned up.

## Contributing Factor: Chest Interception

**Additional finding**: The bot stalls at position **(142,5,274)** — which is exactly where a **chest** is placed (block 79, confirmed placed at 08:48:56). Standing on a chest causes right-click interactions to open the chest GUI instead of placing blocks.

| What | Position | Blueprint |
|------|----------|-----------|
| Chest 1 | (142,5,274) | X at (3,1,3) |
| Chest 2 | (143,5,274) | X at (4,1,3) |
| Bot stall pos | (142,5,274) | standing ON Chest 1 |

**Effect**: When the bot tries to place blocks (right-click) while standing on a chest, Minecraft opens the chest GUI. This:
1. Intercepts the placement interaction
2. Opens the chest inventory UI (visible in screenshot)
3. May delay or prevent the adapter's `placeBlock` from completing
4. Could explain why events arrive late — the bot is fighting the chest GUI

**The step-aside feature** (`index.js:838`) checks for walkable ground but doesn't check if the ground block is **interactive** (chest, crafting_table, bed, furnace, etc.). Standing on interactive blocks is always problematic.

## Recommendation
Implement Option C + Option B together:
1. Ensure adapter echoes `correlationId` in BlockPlacedEvent (Option C — quick fix)
2. Add late-event recovery in C# handler (Option B — robust fix, depends on TSK-0125 for blueprint position lookup)

Option A is a band-aid that masks the real problem.

## References
- `WebUI.Blazor/AgentBackgroundService.cs:681` — BlockPlacedEvent handler
- `WebUI.Blazor/AgentBackgroundService.cs:1772` — AdvanceBuildCheckpoint
- `WebUI.Blazor/AgentBackgroundService.cs:1888` — SweepTimedOutActions
- `MineflayerAdapter/index.js` — place handler, sendEvent for blockPlaced
- Run log: 2026-06-27 08:48-08:50
