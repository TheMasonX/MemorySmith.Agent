# Sprint 50 Wave A — Dashboard Usability + Build Placement Fixes

**Date:** 2026-06-25
**Branch:** `sprint-35-llm-first`
**Author:** SteveBot
**Tests:** 731 passing, 0 failing

---

## Summary

Three work streams in this wave:

1. **Dashboard Frontend** — made the Overview view a usable debugging console with persistent live logs, error badges, and real-time action tracking
2. **Build Placement Fixes** — TSK-0121 (walk-to-origin), TSK-0122 (vegetation clearance), TSK-0123 (self-position placement)
3. **Status API** — added `CurrentAction` and `GetCurrentAction()` for live action tracking in the dashboard

---

## Changes Implemented

### Dashboard Improvements — `WebUI.Blazor/wwwroot/index.html`

| Feature | Description |
|---------|-------------|
| **Persistent Live Log Strip** | Always-visible mini-log at bottom-right of Overview view. Shows last 50 entries with timestamps, levels, and truncated messages. Polls `/api/dashboard/logs` every 2s. |
| **Error/Warning Badges** | Header shows live error (✗) and warning (⚠) counts. Clicking jumps to Logs view with the level filter pre-applied. Empty counts use a dimmed "zero" style. |
| **Quick Filter Buttons** | All / Warnings / Errors toggle buttons on the live log strip. Active filter is highlighted with color. |
| **Auto-scroll Toggle** | Checkbox at the bottom-right of live log. When checked, log auto-scrolls to newest entry. |
| **Position History Trail** | Dots showing last 30 positions. Current position is bright green, recent positions are semi-transparent, older positions are nearly invisible. Coordinate text shown inline. |
| **Current Action Display** | Shows the next action from the queue (tool name + args + elapsed time) in the Status panel. |
| **Error Count in Overview** | Shows live error count next to Queue/Failures in the status panel. |

### TSK-0121: Remove Redundant MoveTo(origin) — `HtnTaskLibrary.cs`

**Problem:** The proximity-gated `MoveTo(origin)` before every block placement caused the bot to walk back to the build origin after each block, wasting time and looking unnatural.

**Fix:** Removed the `MoveTo(origin)` entirely. `PlaceBlock` actions already handle their own navigation via the adapter — each placement navigates from wherever the bot currently is. The separate MoveTo was redundant and actively harmful.

### TSK-0122: Terrain Clearance Before Block Placement — `HtnTaskLibrary.cs`

**Problem:** The agent skipped `PlaceBlock` when the target position was occupied by grass, tall grass, flowers, or other replaceable vegetation, causing infinite stall loops.

**Fix:** Added a terrain clearance phase before block placement that:
1. Defines a comprehensive set of replaceable blocks (~60 entries covering grass, flowers, mushrooms, fungi, vines, kelp, snow, torches, buttons, pressure plates, tripwire, redstone components)
2. Before emitting each `PlaceBlock`, checks if the position has a known replaceable block via `GetBlockAtPosition()` lookup in `WorldState.Facts`
3. If a replaceable block is found, emits `MineBlock` for that position before the `PlaceBlock`

Also added `GetBlockAtPosition()` helper that checks position-encoded fact keys (`block_at_{x}_{y}_{z}` and `block_at_{x}_{z}`).

### TSK-0123: Skip PlaceBlock at Bot's Current Position — `HtnTaskLibrary.cs`

**Problem:** The agent tried to place blocks at its own standing position where no solid reference block exists (`PlaceBlock` requires an adjacent solid face). This caused repeated failures with `"no solid reference block"` and replans couldn't fix it.

**Fix:** Before emitting each `PlaceBlock`, check if the target position matches `state.Position` (bot's current location). If so, skip it — the block will be placed on the next replan cycle once the bot has moved to place adjacent blocks. Logged at stderr for diagnostics.

### Status API Enhancement — `Program.cs`, `AgentBackgroundService.cs`

Added `GetCurrentAction()` method to `AgentBackgroundService` (returns `_queue.Peek()`). Added `CurrentAction` field to `/api/agent/status` endpoint with `{ tool, args }` shape. This enables the dashboard to show the next action in real-time.

---

## New Task Records

| Task | Title | Priority | Source |
|:---|---|:---:|---|
| TSK-0121 | Rehome to origin after every block | Critical | User report |
| TSK-0122 | Agent won't break blocks to build | Critical | User report + logs |
| TSK-0123 | Skip PlaceBlock at bot's current position | Critical | Live production log |

---

## Build & Tests

```
Build succeeded. 0 Error(s)
Passed!  - Failed: 0, Passed: 731, Skipped: 0, Total: 731
```
