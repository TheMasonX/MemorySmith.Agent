# Task: Agent Loop Regression — Infinite Replan & "Digging Aborted" in GatherWood

Status: **INVESTIGATING**
Confidence: 0.85 (root cause identified, fix paths clear)
Tags: `agent-loop`, `minecraft`, `phase-3`, `regression`, `bug`, `handoff`

## Summary

The GatherWood agent loop replans infinitely — the same 4-action plan (SearchMemory → GetStatus → MineBlock → GetStatus) is generated, executed, and regenerated in a tight ~200ms cycle. The bot in Minecraft was visible but **"Digging aborted"** errors from mineflayer indicate the mining action was interrupted before completion. The `MoveTo` action is absent from the plan despite being documented in the architecture.

## Evidence (from 2026-06-15 live test)

### C# (WebUI) Logs — Infinite Replan Cycle
```
Tool SearchMemory: Found 11 result(s): agent-planner-task-library(0.03), ...
Tool GetStatus: Status requested — await WorldEvent type 'status'.
Tool MineBlock: MineBlock(minecraft:oak_log, 10) dispatched.
Tool GetStatus: Status requested — await WorldEvent type 'status'.
New plan for 'GatherWood': 4 actions.
```

Pattern repeats every ~200ms until Ctrl+C.

### Node.js (Mineflayer) Logs
```
[mc] bot spawned at { x: -204, y: 64, z: 177 }
[ws] C# agent connected
[dispatch] mine failed: Digging aborted
...
Error: write ECONNRESET
```

The bot was **visible in-game** at spawn (-204, 64, 177) but mining kept failing.

## Configuration at Time of Test

### appsettings.json
- ServerHost: 192.168.1.187, ServerPort: 4242, BotUsername: AgentBot
- WebSocketPort: 3000, NodeScriptPath: ../MineflayerAdapter/index.js
- AutoStartNode: true, NodeStartTimeoutMs: 10000

### Environment
- Minecraft: LAN server on 192.168.1.187:4242 (1.21+)
- Node.js: v22.11.0 (switched from v18 via nvm4w)
- npm: mineflayer@4.37.1, mineflayer-pathfinder@2.4.5, ws@8.21.0

## Changes Made This Session

### MineBlockTool — Created & Registered
- **Created** `Agent.Tools/Tools/MineBlockTool.cs` — dispatches `{action:"mine", block, count}` to world adapter
- **Registered** in `WebUI.Blazor/Program.cs` alongside MoveToTool, StatusTool, etc.

### mineflayer-pathfinder Import Fixed
- **Modified** `MineflayerAdapter/index.js` line 24 — CommonJS module needs default import, not named ESM import
- Changed `import { pathfinder, Movements, goals } from 'mineflayer-pathfinder'` to default import + destructure

### .gitignore — Wiki Section Added
- Patterns for `Data/memorysmith.db*`, `Data/Keys/`, `Data/Models/`, `*.onnx`, `Data/Graph/`, `Data/Events/`, `.service/`, local overrides, BenchmarkDotNet.Artifacts/

### Task Pages Created
- `Data/Pages/Tasks/wiki-deployment-tooling.md` — Wiki deployment tooling
- `Data/Pages/Tasks/agent-loop-regression-repair.md` — This task

## Root Cause Analysis

### Why the infinite loop happens

1. Queue empty → Planner generates 4 actions → enqueued
2. Each action dispatched (SearchMemory → GetStatus → MineBlock → GetStatus)
3. MineBlock "succeeds" immediately (just sends WebSocket message, doesn't wait for dig to finish)
4. Queue empty → `Goal.IsComplete(WorldState)` → **FALSE** (Inventory has no `*_log` entries)
5. `HasFailed` → FALSE (consecutive failures = 0 because dispatch "succeeds")
6. GOTO 1 — replan same 4 actions forever

### Why MoveTo is missing

`HtnTaskLibrary.GatherWoodDecompose` creates:
```
SearchMemory → GetStatus → MineBlock(block,count) → GetStatus
```

Architecture doc says:
```
FindTree → MoveTo(tree) → MineBlock(log) × N → CollectInventory
```

**MoveTo was intentionally omitted** because no adaptive execution layer exists (Phase 4) to pipe SearchMemory results into coordinates, and MoveToTool would send hardcoded (0,64,0) anyway.

### Why "Digging aborted"

C# dispatch loop sends next `mine` command immediately after the previous `SendActionAsync` returns. Node adapter receives overlapping `dig()` commands — mineflayer can only dig one block at a time, so each new dig aborts the previous.

## Files Modified This Session

| File | Change |
|---|---|
| `Agent.Tools/Tools/MineBlockTool.cs` | **Created** |
| `WebUI.Blazor/Program.cs` | **Modified** — Registered MineBlockTool |
| `MineflayerAdapter/index.js` | **Modified** — Fixed CommonJS import for mineflayer-pathfinder |
| `.gitignore` | **Modified** — Added wiki artifact section |
| `Data/Pages/Tasks/wiki-deployment-tooling.md` | **Created** |
| `Data/Pages/Tasks/agent-loop-regression-repair.md` | **Created** (this file) |

## Recommended Fixes (Priority Order)

### Fix 1: Add MoveTo to GatherWoodDecompose (HIGH)
**File**: `Agent.Planning/HtnTaskLibrary.cs`

Add `MakeAction("MoveTo", ("x", 0), ("y", 64), ("z", 0))` before MineBlock. Hardcoded coords until Phase 4.

### Fix 2: Rate-limit or serialize mine actions (HIGH)
Two approaches:
- **A) Node-side busy flag**: Add a `busy` bool to index.js dispatch — reject/queue incoming commands while digging
- **B) C# side completion signal**: MineBlockTool should await a `blockMined` world event before returning success

### Fix 3: Update WorldState.Inventory from blockMined events (MEDIUM)
**File**: `WebUI.Blazor/AgentBackgroundService.cs` — `ProcessEventsAsync`

Currently blockMined events don't update `WorldState.Inventory`. `GatherWoodGoal.IsComplete` checks `Inventory["*_log"]` which stays empty forever.

### Fix 4: Node adapter busy queue (MEDIUM)
**File**: `MineflayerAdapter/index.js` — prevent overlapping dig commands

### Fix 5: Consecutive failure detection for dispatch-only tools (LOW)
**File**: `WebUI.Blazor/AgentBackgroundService.cs` — `MineBlockTool` returns success=true immediately, never incrementing `_consecutiveFailures`, so `HasFailed` never triggers.

## Related Files

- `Agent.Planning/HtnTaskLibrary.cs` — decomposition methods
- `Agent.Tools/Tools/MineBlockTool.cs` — created this session
- `WebUI.Blazor/Program.cs` — tool registration
- `WebUI.Blazor/AgentBackgroundService.cs` — dispatch loop + event processing
- `MineflayerAdapter/index.js` — Node adapter bridge
- `Agent.Planning/Goals/GatherWoodGoal.cs` — IsComplete checks Inventory
- `Agent.Core/Models/WorldState.cs` — Inventory dictionary
- `Data/Tasks/tsk-0003-first-end-to-end-game-test.json` — original E2E test task
- `Data/Pages/planner.md` — architecture doc
