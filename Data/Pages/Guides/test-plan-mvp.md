# Leo MVP Test Plan — House Building
**Date:** 2026-06-17  
**Sprint:** 18 (Sprint 18 fixes applied)  
**Branch:** `sprint-5-tool-safety`  
**Goal:** Verify Leo can gather materials, find a flat area, and build a small house.

---

## Prerequisites

Before running any tests:

```powershell
# Terminal 1 — Start the Node.js adapter (adjust paths as needed)
cd D:\@Repos\MemorySmith.Agent\MineflayerAdapter
MC_HOST=localhost MC_PORT=25565 MC_USERNAME=Leo WS_PORT=3000 node index.js

# Terminal 2 — Start the C# host
cd D:\@Repos\MemorySmith.Agent
dotnet run --project WebUI.Blazor --launch-profile WebUI.Blazor
```

**Expected startup log (Sprint 18):**
```
[HH:mm:ss] World adapter connected.
[HH:mm:ss] Bot spawned at Position { X = ..., Y = ..., Z = ... }
[HH:mm:ss] === Agent config: bot=Leo mc=localhost:25565 | llmTimeout=10s rateCooldown=...s maxPerMin=... | memory=http://... actionTimeout=30s replanInterval=2s ===
```

✅ If you see `=== Agent config ===` — startup config logging is working.

---

## Phase 1 — Connectivity (2 minutes)

| # | Test | How to run | Expected |
|---|------|-----------|---------|
| 1.1 | Bot connects | Start adapter + C# | Log: "Bot spawned at Position ..." |
| 1.2 | Status command | Say in Minecraft: `leo status` | Leo responds with health/food/position |
| 1.3 | Help command | Say: `leo help` | Leo lists available commands |

---

## Phase 2 — Stop Command (2 minutes) ← NEW in Sprint 18

This verifies the emergency stop fix. Before Sprint 18, "leo stop" cleared the C# queue but Node.js kept mining.

| # | Test | How to run | Expected |
|---|------|-----------|---------|
| 2.1 | Start a gather goal | Say: `leo gather wood` | Log: "Goal set: Gather:oak_log" |
| 2.2 | Stop immediately | Say: `leo stop` | Log: "Goal cancelled: Gather:oak_log" + "[stop] emergency stop dispatched to adapter" |
| 2.3 | Verify bot actually stops | Watch Minecraft | Bot stops moving/mining within 1-2 seconds |

**Pass:** Bot stops within ~2 seconds of "leo stop".  
**Fail:** Bot keeps mining for 10+ seconds after "leo stop".

---

## Phase 3 — Gather with Correct Count (3 minutes) ← FIXED in Sprint 18

Before Sprint 18, "get 1 dirt" always mined 10 and kept going after goal completion.

| # | Test | How to run | Expected |
|---|------|-----------|---------|
| 3.1 | Gather exactly 1 item | Say: `get 1 dirt` | Log: "Chat created goal: Gather:dirt", then shortly "Goal 'Gather:dirt' completed." |
| 3.2 | Verify count | Check log | Inventory shows +1 dirt; bot stops after 1. NOT 10 or 55. |
| 3.3 | Gather a custom count | Say: `get 5 dirt` | Log shows 5 dirt mined, goal completes after 5 |
| 3.4 | Stop during gather | Start `get 67 wood`, then say `leo stop` | Bot stops mining; log shows "[stop] emergency stop dispatched" |

**Pass:** Gather collects exactly the requested count and stops.  
**Fail:** Gather mines 10 regardless of count; bot keeps mining after goal completion.

---

## Phase 4 — LLM Chat Handling (5 minutes)

| # | Test | How to run | Expected |
|---|------|-----------|---------|
| 4.1 | Greeting (LLM fast-path skipped) | Say: `leo gather wood` | Immediate response via pattern matcher. NO LLM call. Log: "[llm] fast-path CreateGoal for ..." |
| 4.2 | Unknown command (LLM called) | Say: `hey leo, what time is it?` | Log: "[llm] calling ollama (llama3.2:3b) for ...". Then within ~10-30s: response or timeout log |
| 4.3 | LLM timeout (if Ollama is slow) | Say an open-ended question | Within ~10-15s: "[llm] hard timeout after Xs" OR "[llm] timed out after Xs" in logs |
| 4.4 | Rate limit check | Send multiple LLM messages quickly | Log: "[llm] rate-limited for TheMasonX23 ..." |

**Startup config shows** `llmTimeout=10s` — LLM should never hang more than ~11s.  
**Check your startup log** for the actual timeout value if LLM hangs longer.

---

## Phase 5 — findFlatArea (3 minutes) ← FIXED in Sprint 18

Before Sprint 18: crash with "pos.floored is not a function" immediately.

| # | Test | How to run | Expected |
|---|------|-----------|---------|
| 5.1 | Find flat area via build command | Say: `leo build a house` | Log: "New plan for 'Build:small-house': N actions." then "[findFlatArea]..." |
| 5.2 | findFlatArea completes | Wait | Log: "[findFlatArea] best at (X,Y,Z) area=N..." OR "[findFlatArea] no qualifying flat area" (if terrain is too rough) |
| 5.3 | No "pos.floored" error | Check logs | NO log line containing "pos.floored is not a function" |

**Pass:** findFlatArea runs to completion (finds area OR reports no area found).  
**Fail:** Log shows "Game error [findFlatArea]: pos.floored is not a function".

If no flat area found: use `/teleport Leo X Y Z` to move the bot to a flatter area and retry.

---

## Phase 6 — Replanning frequency (1 minute) ← FIXED in Sprint 18

Before Sprint 18: "New plan for 'Gather:oak_log': 12 actions" appeared 3+ times per second.

| # | Test | How to run | Expected |
|---|------|-----------|---------|
| 6.1 | Start a gather | Say: `get 10 oak_log` | Log: "New plan for 'Gather:oak_log': 4 actions." |
| 6.2 | Check replan frequency | Watch the log for ~10 seconds | "New plan" appears at most once every ~2 seconds (not 3x per second) |

**Pass:** Replan messages appear ≤ once every 2 seconds.  
**Fail:** Replan messages flood continuously (1+ per second for >5 seconds).

---

## Phase 7 — Full House Build (10-20 minutes)

This is the MVP test. Prerequisites: findFlatArea works (Phase 5), gather works (Phase 3).

**Note:** A MemorySmith wiki deployment is NOT required if you manually set the build origin:

```
# Option A: Let Leo find a flat area automatically
say in Minecraft: leo build a house

# Option B: Set the origin manually (if Option A can't find a flat area)
# POST /api/agent/origin  {"blueprintId": "auto", "x": X, "y": Y, "z": Z}
# (use your current X,Y,Z position)
```

| # | Step | Expected log |
|---|------|-------------|
| 7.1 | Start build | `leo build a house` → "Chat created goal: Build:small-house" |
| 7.2 | findFlatArea | "[findFlatArea] auto-set build origin (X,Y,Z) area=N" |
| 7.3 | Material gather | "New plan for 'Build:small-house': N actions." + MineBlock actions for oak_log |
| 7.4 | Wood gathered | "Inventory +N oak_log → total M" |
| 7.5 | Block placement | "Tool PlaceBlock: PlaceBlock(...) dispatched." repeated |
| 7.6 | Build complete | "Goal 'Build:small-house' completed." |

**Pass:** Leo places blocks forming a recognizable structure.  
**Partial pass:** Leo gathers materials and finds the site but fails at placement.  
**Fail:** Leo fails at findFlatArea (Phase 5 blocker) or stops immediately with no actions.

---

## Observability Checks

| Check | How | Expected |
|-------|-----|---------|
| Dashboard | http://localhost:5000 | Shows Leo's status, goal, inventory, health |
| Journal | GET http://localhost:5000/api/agent/journal | Returns recent action log |
| Inventory | GET http://localhost:5000/api/agent/status | Shows current inventory |
| Knowledge resolve | GET http://localhost:5000/api/agent/resolve?q=oak_log | Returns DirectMineable candidate |

---

## Known Remaining Limitations

| Limitation | Status | Impact |
|-----------|--------|--------|
| No MemorySmith wiki deployed | Needs `tools/Start-MemorySmithWiki.ps1` (Sprint 19) | SearchMemory actions return empty results; bot still gathers and builds |
| Small-house blueprint not seeded | Needs wiki deployment | Bot uses hardcoded small-house if GoalFactory has it |
| LLM can't understand "walk into lava" | By design — dangerous commands are not implemented | Bot ignores or says "Didn't catch that" |
| Bot may get stuck in obstacles | Sprint 18 deferred (B3, B5) | Manually teleport bot to open area and retry |
| Mining wrong block type in gather | Only if SourceBlocks contains unexpected entries | Check GoalFactory.CreateAsync for item resolution |

---

## Quick Debug Commands

```powershell
# Check Leo's current status
curl -s http://localhost:5000/api/agent/status | python3 -m json.tool

# See recent journal entries
curl -s "http://localhost:5000/api/agent/journal?limit=20" | python3 -m json.tool

# Cancel current goal
curl -s -X DELETE http://localhost:5000/api/agent/goal

# Set build origin manually (replace X,Y,Z with coordinates)
curl -s -X POST http://localhost:5000/api/agent/origin -H "Content-Type: application/json" \
  -d '{"blueprintId":"auto","x":X,"y":Y,"z":Z}'

# Test knowledge resolver
curl -s "http://localhost:5000/api/agent/resolve?q=oak_log"
```
