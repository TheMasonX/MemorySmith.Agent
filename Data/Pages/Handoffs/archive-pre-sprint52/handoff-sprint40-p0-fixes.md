# Handoff: Sprint 40 P0 Fix Package — Block Position, Reconnect, Chat Validation

## Summary

This handoff documents the current state of work-in-progress fixes for the MemorySmith.Agent
gather-dirt failure analysis and follow-up issues discovered during runtime testing.

## Completed Work

### 1. Inventory Sync (PR #1 — Merged)
- `WorldStateProjector.ApplyBlockMined` restored with block-to-item drop mapping
- `SelfDroppingBlocks` hash set + `BlockToItemDrop` dictionary for ore→raw-material mapping
- `GenericGatherGoal.IsComplete` creative-mode auto-complete removed
- `AgentBackgroundService.SetGoal` creative provisioning via `/give @p` + `GetStatus`

### 2. Creative Mode Gather (PR #1 — Merged)
- `/give` command enqueued when in creative mode with insufficient items
- Stale-inventory pre-plan guard enqueues `GetStatus` before planner runs

### 3. Mineflayer Block Reachability (PR #1 — Merged)
- `findReachableBlock` action added to Mineflayer adapter
- `ReachableBlockFoundEvent` + C# wiring
- Post-dig item pickup logic (wait, check entity, move to position)

### 4. Governor + Continue (PR #1 — Merged)
- `TryAutoRecover()` on `IReplanGovernor` for pre-plan stall check
- Continue/resume intent handler in `HandleChatEventAsync`

### 5. ActionOutcome Rich Status (PR #1 — Merged)
- `OutcomeType` enum replacing bare `bool Success`
- `Completed`, `NoProgress`, `Failed`, `Blocked`, `Unreachable`, `TimedOut`

### 6. Coordinate Logging (PR #2 — In Progress)
- Bot position `pos=(x,y,z)` added to all goal, action, plan, and inventory logs
- Block target position `block=(bx,by,bz)` added to BlockMinedEvent and log

### 7. Chat Startup Timing (PR #2 — In Progress)
- Mineflayer adapter chat case now checks `bot.entity` before calling `bot.chat()`
- Retries after 500ms if not spawned yet

### 8. Kick → Reconnect (PR #2 — In Progress)
- `KickedEvent` handling in `ProcessEventsAsync` with `LogCritical` + connection CTS cancel
- `_connectionCts` field added to `AgentBackgroundService`

### 9. Event Enrichment (PR #2 — In Progress)
- `BlockMinedEvent.BlockPosition` field added
- `MineCompleteEvent.BlockPosition` field added  
- `ReachableBlockFoundEvent` parsing in `WebSocketBridge`
- `GetDouble` helper added to `WebSocketBridge`

## Files Changed (PR #2 — Not Yet Built/Tested)

| File | Change |
|------|--------|
| `MineflayerAdapter/index.js` | Block position in blockMined/mineComplete; chat spawn guard |
| `Agent.Core/Events/WorldEvents.cs` | BlockPosition on BlockMinedEvent, MineCompleteEvent |
| `Agent.World.Minecraft/WebSocketBridge.cs` | Parse blockX/Y/Z, reachableBlockFound, GetDouble |
| `WebUI.Blazor/AgentBackgroundService.cs` | KickedEvent handler, _connectionCts, BlockPosition log |
| `Agent.Core/WorldStateProjector.cs` | (no change needed — StoreFacts already covers new fields) |

## Remaining Issues / What's Broken

### Issue A: Build Infrastructure — DLL Locking
The `WebUI.Blazor` app process and `Microsoft Visual Studio` hold locks on build output DLLs.
Running `dotnet build` fails with MSB3027. Workaround: kill PID 23016 (WebUI.Blazor) before build.

**Affected files** that need validation:
- `Agent.Core/Events/WorldEvents.cs` — `BlockMinedEvent` and `MineCompleteEvent` signatures changed
- All test files constructing these events — 7 files updated
- `WebSocketBridge.cs` — blockX/Y/Z parsing added

### Issue B: Chat Validation Kick (Server-Side)
The Minecraft server kicked the bot with `multiplayer.disconnect.chat_validation_failed` after
the `/give @p dirt 10` command was processed. This is likely a Paper/Spigot server chat validation
plugin, not a code bug. The `KickedEvent` handler (PR #2) should force reconnection when this
happens, but this hasn't been tested end-to-end.

**Next steps:**
1. Verify the kick event triggers `_connectionCts.Cancel()` and the reconnection loop
2. Check whether the Node.js adapter process needs to be restarted after a kick (the WebSocket
   stays alive but the Mineflayer bot is dead)
3. Consider adding exponential backoff to prevent rapid reconnect loops

### Issue C: Block Position Confusion (Off-By-One?)
User reported the bot digging at Y=62-63 instead of Y=64 (surface at Y=65 feet level).
The `BlockMinedEvent.BlockPosition` field (PR #2) adds the actual block coordinates to
the log. Once built and deployed, the log will show:

```
Inventory +1 dirt -> total 5 bot=(-245,65,161) block=(-245,64,161)
```

This will make it clear whether the target is at Y=64 (correct — one below feet) or
Y=63/62 (wrong — two below feet).

**Possible causes if Y is wrong:**
- `bot.findBlock()` returns the nearest block by Euclidean distance, not by reachability
  path. If blocks at Y=64 are already mined, the nearest remaining dirt might be at Y=63.
- The bot might be falling through mined blocks and `botPos()` reflects the lower Y.
- The `GoalNear` tolerance (2 blocks) might be too large.

### Issue D: Reachability Testing
The `findReachableBlock` action (PR #1) has been added but never tested in production.
It finds blocks that the bot can pathfind to, solving the "digging through a hole" problem.
Needs end-to-end validation.

### Issue E: `GatherItem` Goal Creation Failed
Log shows `Cannot create GatherItem goal for 'dirt:10': item not found in registry and not
a built-in direct-mine block`. This happened because the inventory still showed 10 dirt (stale)
from the previous session, and the GoalFactory rejected the request. The stale-inventory guard
(PR #1) should prevent this by refreshing inventory before planning, but the goal creation
happens in `HandleChatEventAsync` BEFORE the dispatch loop runs. Investigate whether the
stale check should also apply at goal-creation time.

## Build Instructions

```powershell
# Kill lock-holding processes first
Get-Process -Name WebUI.Blazor -ErrorAction SilentlyContinue | Stop-Process -Force
# Then build
cd D:\@Repos\MemorySmith.Agent
dotnet build
# Run tests
dotnet test --no-build
```

## Key Commands for Testing

```powershell
# Start Mineflayer adapter
cd D:\@Repos\MemorySmith.Agent\MineflayerAdapter
node index.js

# Start the app (separate terminal)
cd D:\@Repos\MemorySmith.Agent
dotnet run --project WebUI.Blazor --launch-profile http
```

## Sprint Plan — Council Review Integrated

The council review of the Mineflayer Adapter Research Paper (`Data/Pages/Audit/mineflayer-adapter-research.md`) identified additional defects beyond these handoff issues. A consolidated sprint plan is at:

**`Data/Pages/Tasks/sprint40-plan-adapter-council-response.md`**

### New Tasks Created from Council Review

| Task | Source | Title | Priority |
|------|--------|-------|----------|
| TSK-0061 | DEF-PAPER-2 | Wire mineAborted/stopComplete to C# | P0 Critical |
| TSK-0062 | DEF-PAPER-3 | Add goto() timeout with Promise.race() | P1 High |
| TSK-0063 | DEF-PAPER-4 | Wire bot.inventory.on('updateSlot') | P2 Medium |
| TSK-0064 | DEF-PAPER-7 | Add throttle/debounce for move events | P2 Medium |
| TSK-0065 | Handoff Issue C | Fix block position off-by-one | P0 Critical |
| TSK-0066 | Handoff Issue B | Verify kick→reconnect with backoff | P0 Critical |
| TSK-0067 | Handoff Issue E | Add stale-inventory guard at goal-creation | P1 High |
| TSK-0068 | DEF-PAPER-5 | Define IObservationSummarizer interface | P3 Low |
| TSK-0069 | DEF-PAPER-6 | Add collectblock/tool dependencies | P3 Low |
| TSK-0070 | DEF-PAPER-1 | Correct path_reset to path_update | P1 High |

### Dependency Map

```
TSK-0065 (Block position) ──depends-on── TSK-0061 (mineAborted/stopComplete wiring)
                                        ──depends-on── PR #2 (coordinate logging) [in-progress]

TSK-0066 (Kick→reconnect)  ──depends-on── PR #2 (KickedEvent handler) [in-progress]
                                        ──depends-on── TSK-0061 (event wiring patterns)

TSK-0062 (goto timeout)    ──depends-on── TSK-0070 (correct path_update)
                                        ──related-to── TSK-0037 (action progress telemetry)

TSK-0063 (updateSlot)     ──depends-on── TSK-0066 (solid reconnect first)
```

### Execution Order

1. **Build and test PR #2 changes** — Kill lock processes, build, run tests, fix any failures
2. **TSK-0061** — Wire mineAborted/stopComplete (enables TSK-0065, TSK-0066)
3. **TSK-0070** — Correct path_reset → path_update (prerequisite for TSK-0062)
4. **TSK-0065** — Diagnose block position off-by-one (needs PR #2 coordinate logging)
5. **TSK-0066** — Verify kick→reconnect with exponential backoff
6. **TSK-0062** — Add goto() timeout with Promise.race()
7. **TSK-0067** — Add stale-inventory guard at goal-creation
8. **TSK-0063, TSK-0064** — updateSlot wiring, move throttle
9. **TSK-0068, TSK-0069** — Architecture/planning (deferred)
