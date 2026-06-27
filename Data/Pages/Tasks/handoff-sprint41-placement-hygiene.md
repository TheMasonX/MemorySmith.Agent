# Sprint 41 — Placement & Build Hygiene Handoff (2026-06-23)

## Summary

The bot can now place blocks sequentially, but with significant gaps in quality,
reachability, and terrain awareness. The house was partially built but had missing
wall blocks, blocks placed inside a hill, and no roof/scaffolding capability.

---

## Issue TSK-0072: Placement Fails with `blockUpdate` Timeout

### Symptoms
```
Game error [place]: Cannot place oak_planks at (-232,66,139) — no solid reference block
(last face error: Event blockUpdate:(-232, 66, 139) did not fire within timeout of 5000ms)
```
- All failures are at Y=66 (wall blocks), NOT at Y=63 (floor blocks)
- The `blockUpdate` event for the placed position never fires within 5s
- The reference block WAS found (otherwise we'd see "ref face empty" not a placeBlock error)

### Root Cause Hypothesis
Mineflayer's `bot.placeBlock(ref, faceVec)` sends the place packet to the server.
The server places the block but the client doesn't receive the `blockUpdate` event
within 5 seconds. Possible causes:
1. **Terrain overlap**: The target position already has a block (inside a hill/terrain)
2. **Anti-cheat / gamemode interaction**: Creative mode + `/give` items might behave differently
3. **Reach distance**: Bot is too far from the placement position (goto tolerance is 3 blocks, but
   `placeBlock` requires ~4.5 block reach)
4. **Server lag**: Under load, the block update is delayed >5s

### Evidence from adapter logs
- Fast placements: 200-800ms (bot already close, position clear)
- Slow placements: 6,000-27,000ms (bot pathfinding far OR blockUpdate timing out)
- All slow placements are Y=66 wall blocks

### Suggested Fixes
1. Check if target position already has a block before attempting placement (skip instead of fail)
2. Increase goto precision: `GoalNear(x, y, z, 2)` instead of `GoalNear(x, y, z, 3)`
3. Log whether terrain exists at target position before attempting
4. Add block-occupancy check: `if (bot.blockAt(toVec3(x, y, z)).type !== 0) skip`
5. (P2) Increase the Mineflayer internal `blockUpdate` timeout via bot settings

### Diagnostic Logging to Add
- Before attempting placement: log target block type (air vs occupied)
- After failed blockUpdate: log bot position vs target position distance
- After each reference face attempt: log whether the ref block was found and its name
- Log the distance from bot to target position on each attempt

---

## Issue TSK-0073: No Scaffolding / Height Access

### Symptoms
The bot builds floor and lower walls (Y=63-66) but cannot reach Y=67+ (roof/upper wall).
The blueprint requires placing blocks at Y=68-71 (roof), but the bot never reaches them.

### Root Cause
The bot places blocks from ground level and has no mechanism to:
1. Build temporary scaffolding (dirt pillars to stand on)
2. Jump while placing
3. Place blocks above its reach distance (~5 blocks from feet)

### Evidence
- Build origin was at (-231, 65, 156), floor at Y=63 (gap of 2)
- Wall blocks at Y=66 succeeded when reachable
- No roof blocks at Y=68-71 were ever attempted (or all failed silently)

### Suggested Fixes
1. Add scaffolding phase to `BlueprintExecutor` or `DecomposeBuild`:
   - Before placing blocks above Y = botFeetY + 4, build a dirt pillar beneath the bot
   - Place dirt column, move bot to top, place wall/roof blocks, then remove dirt
2. OR skip unreachable blocks gracefully instead of stalling

### Diagnostic Logging to Add
- Log the maximum Y the blueprint requires vs the bot's current Y
- When a placement fails at a high Y, log "scaffolding needed" as a separate event
- Track how many blocks were skipped due to height

---

## Issue TSK-0074: Terrain Clearance / Hill Detection

### Symptoms
The bot tried to place wall blocks inside a hill. The user had to manually remove
dirt blocks to let the bot continue. The bot never detected or reported terrain
obstruction.

### Root Cause
The blueprint places blocks at absolute world coordinates relative to the build
origin. The flat area scanner found "flat" ground, but the area still had terrain
features (a hill at the edge). Wall blocks at Y=66 intersected with the hill at
that Y level.

### Evidence from logs
- Build origin: (-231, 65, 156) with area=1586 (found flat area)
- Failed placements at (-232,66,139) through (-227,66,142) — these are in a line
  along the north wall at Y=66
- The adapter elapsed times for these: 6-27 seconds each (blockUpdate timeout)

### Suggested Fixes
1. Before placing, check if target position already has a non-air block → skip (don't fail)
2. Log and count skipped positions so the build checkpoint advances past them
3. After the build completes, optionally emit a warning listing skipped positions
4. (P2) Add pre-build terrain clearance: emit MineBlock actions for any blocks at
   blueprint positions that already exist

### Diagnostic Logging to Add
- Log "position occupied" when skipping a block that already exists
- Log the number of terrain-overlapping positions found during build decomposition

---

## Issue TSK-0075: No Place-By-Place Retry After Failure

### Symptoms
When a PlaceBlock fails, the build checkpoint advances past the failed position
and never retries it. The house has missing blocks in walls and floor.

### Root Cause
The `BlueprintExecutor` emits PlaceBlock actions ordered by Y→Z→X. Each action
has a `PlaceBlockProgressBlockIndex` context key. When an action fails, the
dispatch loop still records the checkpoint as "placed" (line 1374 in
`AgentBackgroundService.cs`):

```csharp
if (action.Tool.Equals("PlaceBlock", StringComparison.OrdinalIgnoreCase)
    && action.Context.TryGetValue(
        BuildFactKeys.PlaceBlockProgressBlueprintId, out var bpId)
    && action.Context.TryGetValue(
        BuildFactKeys.PlaceBlockProgressBlockIndex, out var bpIdx))
{
    var progressFactKey = BuildFactKeys.BuildProgressIndex(...);
    _worldState = _worldState.With(b => b.SetFact(...));
}
```

This code runs when `result.Success == true` from the fire-and-forget PlaceBlock
tool (which returns success immediately after dispatching to Node.js). The actual
placement result arrives later via `BlockPlacedEvent` or `ErrorEvent`, but the
checkpoint is already saved.

### Suggested Fixes
1. Only advance the build checkpoint when `BlockPlacedEvent` is received, not on
   fire-and-forget dispatch success
2. OR track failed positions separately and retry them on the next plan cycle

### Diagnostic Logging to Add
- Log "checkpoint advanced" with the block index and position separately from
  "block placed" confirmation
- Log the gap between checkpoint position and actually confirmed positions

---

## Issue TSK-0076: MoveTo → PlaceBlock Gap Causes Wrong Position

### Symptoms
The PlaceBlock handler pathfinds to `GoalNear(x, y, z, 3)`. After arriving, the
bot may be up to 3 blocks from the target. The `placeBlock()` call then fails
because the reference block is out of reach (4.5 block reach in survival).

### Evidence from adapter logs
- Some placements take 200ms (bot already nearby, 0-1 block away)
- Others take 6-27 seconds (bot pathfinding from far away)

### Suggested Fixes
1. Reduce goto tolerance: `GoalNear(x, y, z, 2)` instead of 3
2. After goto, check actual distance to target before placing; if too far, send
   a more precise goto
3. Log the distance from bot to target at the start of each PlaceBlock attempt

---

## Issue TSK-0077: LLM Intent Confusion (Build vs Gather)

### Symptoms (observed but may be improved by prompt changes)
- `[chat] <TheMasonX23> build a house` → `-> gather`
- The LLM sometimes returns `intent: "gather"` with `blueprint: "house"` instead of
  `intent: "build"`
- Fixed by prompt coaching in `LlmChatInterpreter.BuildSystemPrompt()`

### Status
Prompt was strengthened with explicit INTENT RULES section. Monitor after deployment
to see if the issue recurs.

### Additional Logging
- Log the full LLM response whenever `IntentManager.BuildGoalRequest` returns null
  (was already added in Sprint 41, verify it's working)
- Add the raw LLM response snippet to the "insufficient fields" warning

---

## Issue TSK-0078: BlockPlacedEvent Correlation (Fixed, Monitor)

### Status
Fixed in Sprint 41 by adding `case BlockPlacedEvent:` handler. Monitor that
PlaceBlock correlation no longer shows "TIMED OUT after 30s" in logs.

### Additional Logging
- Already added: `[place] block placed: {Block} @ ({X},{Y},{Z})` at LogDebug
- Monitor that this appears consistently for every successful placement

---

## Config Changes Made This Session

| Change | File | Old Value | New Value |
|--------|------|-----------|-----------|
| PlaceBlock timeout | `AgentBackgroundService.cs` | 5s | 2s |
| Stall threshold | `Program.cs` | 3 | 5 |
| Queue clear on replan | `AgentBackgroundService.cs` | No | Yes |
| Reference block detection | `MineflayerAdapter/index.js` | Bot-relative | Target-relative |
| `/give` anti-spam delay | `AgentBackgroundService.cs` | None | 200ms |
| `default` event logging | `AgentBackgroundService.cs` | Silent | LogDebug |
| Error event position context | Multiple files | None | x,y,z,block,material,item |
| LLM prompt for build intent | `LlmChatInterpreter.cs` | Weak coaching | Explicit INTENT RULES |

## Suggested Additional Logging Improvements

1. **PlaceBlock**: Log target block occupancy (is position already filled?) before attempting
2. **PlaceBlock**: Log bot-to-target distance at start
3. **PlaceBlock**: Log which of the 6 reference faces were skipped vs attempted
4. **Build checkpoint**: Log "block placed" confirmation separately from "checkpoint advanced"
5. **Height tracking**: Log max blueprint Y vs bot's Y when scaffolding would be required
6. **Terrain collision**: Log count of blueprint positions that overlap existing blocks
7. **C# LLM logging**: Confirm `[ollama] response` Debug lines appear in the Serilog file
8. **Plan changes**: Log WHY the action count changed (which block index was checkpointed)

## Files Modified This Session

| File | Changes |
|------|---------|
| `AGENTS.md` | Added Rule E-3 (Never Swallow Exceptions) |
| `AgentBackgroundService.cs` | BlockPlacedEvent handler, queue clear, tool timeouts, creative provisioning, `/give` delay, default event logging, ErrorEvent position context |
| `MineflayerAdapter/index.js` | Tool selection before dig, target-relative place refs, position logging in place/error |
| `HtnTaskLibrary.cs` | EnsureToolsForBlocks, HasAnyTool, tool requirement maps |
| `Program.cs` | Stall threshold 3→5 |
| `OllamaProvider.cs` | Full request/response Debug logging |
| `LlmChatInterpreter.cs` | Raw response Debug logging, parsed intent logging, stronger prompt |
| `IntentManager.cs` | Refactored build case, no goto |
| `WorldEvents.cs` | ErrorEvent position fields |
| `WebSocketBridge.cs` | ErrorEvent parsing, GetIntOrNull helper |
| `ReplanGovernor.cs` | (Tests only) |

---

## Sprint 42 — Council Findings & Improved Directions (2026-06-23)

### What Sprint 42 Implemented (SteveBot)
- **TSK-0074**: Added terrain occupancy check in `MineflayerAdapter/index.js` `case 'place'` — skips occupied positions instead of 5-27s blockUpdate timeout
- **TSK-0075**: Moved build checkpoint advancement from dispatch-time to `BlockPlacedEvent`-confirmed via new `AdvanceBuildCheckpoint()` method and `_placeBlockContexts` dictionary
- **TSK-0076**: Reduced MoveTo tolerance from 3 to 2 blocks for tighter placement positioning
- **Task records**: TSK-0074 through TSK-0078 created with Done/Backlog status

### 6-Seat Council Review Findings

A full 6-seat LLM council review was conducted on 2026-06-23 (see `Data/Pages/council/sprint42-placement-hygiene-council-20260623.md`). Five seats were run via subagent with explicit permission, plus in-process synthesis.

**Overall confidence: 0.84** — Direction is sound; 4 P0 items must be fixed before next feature work.

#### P0 Items (Fix Immediately)

| # | Issue | Detail |
|---|---|---|
| **P0-1** | **TSK-0074/TSK-0075 interaction: silent wrong-build** | Terrain-occupied positions emit `blockPlaced` event → checkpoint advances → blueprint has holes. MUST emit a distinct event or not advance checkpoint for skips. |
| **P0-2** | **Smelt→CraftItem routing (7 sprints old)** | "Smelt iron ore" produces a craft plan, not furnace execution. Furnace handler exists in `index.js` but C# planner never routes to it. |
| **P0-3** | **SearchMemory dead weight** | ~15 HTTP calls per gather cycle, results NEVER consumed by any downstream action. Either wire TSK-0004 (SearchMemory → MoveTo) or remove calls from decompositions. |
| **P0-4** | **Zero tests for Sprint 42 changes** | `AdvanceBuildCheckpoint`, `BlockPlacedEvent` handler, `_placeBlockContexts` lifecycle, terrain skip — 0% test coverage. |

#### P1 Items (This Sprint)

| # | Issue | Detail |
|---|---|---|
| P1-1 | PlaceBlock timeout: code=2s, docs=5s | Race condition: C# cancels at 2s while Node.js adapter may still be placing. Reconcile. |
| P1-2 | `_placeBlockContexts` dictionary leak | Entries not cleaned up on duplicate events or goal failure paths. |
| P1-3 | User-facing stall/progress messages | Bot silently stalls for 6-27s. Add chat messages for long operations. |

#### Deferred Items (Sprint 43+)

| # | Item | Gate |
|---|---|---|
| D1 | Decompose `AgentBackgroundService` (13+ responsibilities) | Extract event routing + goal management first |
| D2 | Deprecate `WorldState.Facts`, unify with `StructuredFacts` | Migrate all goal IsComplete/HasFailed readers |
| D3 | Type `ActionData.Context` — extract `correlationId: Guid` | Reduce implicit string-keyed coupling |
| D4 | Remove `ChatInterpretation.GoalName` (zombie field) | Verify no unbilled consumers; update Sprint21Tests |
| D5 | Wire `IKnowledgeResolver` into planning | Has zero consumers — dead code risk |
| D6 | Add E2E tests (simulated or real Minecraft) | Unit-test ceiling reached |

### Updated Sprint Priority

```
Sprint 43: CORRECTNESS SPRINT — fix P0/P1 items, close test gaps
Sprint 44: ARCHITECTURE SPRINT — AgentRuntime decomposition, fact store unification
Sprint 45+: FEATURE SPRINT — scaffolding, terrain clearance, new goals
```

### Critical Risk: TSK-0074 Occupancy Skip + Checkpoint Advance

The TSK-0074 fix (Sprint 42) replaces a 5-27 second stall with a **silent skip** that advances the checkpoint. When the bot encounters a position occupied by terrain:

1. `index.js` checks `bot.blockAt(targetPos)` → occupied by different block
2. Logs "terrain collision — skipping occupied position"
3. **Emits `blockPlaced` event** with success status
4. C# `BlockPlacedEvent` handler calls `AdvanceBuildCheckpoint`
5. Checkpoint advances past this position
6. Blueprint has a hole permanently — the position is never retried

**Resolution path:**
- Option A: Emit new `BlockSkippedEvent` (not `blockPlaced`) that completes correlation but does NOT advance checkpoint
- Option B: Add `Skipped` state to checkpoint that the planner can retry on next cycle
- Option C: Keep skipping but add pre-build terrain clearance (TSK-0078, backlogged) to mine terrain blocks before placement phase begins

**Preferred: Option A** — cleanest separation of concerns. Requires new event type, C# handler, and test coverage.

