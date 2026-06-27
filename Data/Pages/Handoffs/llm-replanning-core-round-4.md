# Handoff: LLM Replanning Core — Round 4

**Date:** 2026-06-27
**Branch:** `dev/round-3` → next: `dev/round-4`
**Build:** 742 tests passing, 0 warnings
**Handoff author:** SteveBot (MemorySmith.Agent)

---

## 🎯 Thesis

**The LLM should detect build stalls, analyze WHY blocks failed to place, and recommend remediation.** The current loop is blind: when the governor stalls, it logs `"no progress"` without understanding that the bot is standing where a block should go, a door has the wrong facing, or a bed can't place because of collision. The LLM can see these patterns — we just need to feed it the right data.

---

## 📊 What's Working Well

| Component | Status |
|---|---|
| LLM chat interpretation | DeepSeek correctly parses compound commands, place/build/gather intents, `nextSteps[]` |
| Multi-step chaining | `TaskSequenceGoal` executes steps in order via `TryAdvanceSequence` |
| Auto-tool crafting | `GatherGoalDecomposer` pre-crafts tools before mining |
| Cross-session memory | `IMemoryGateway.LoadSessionFactsAsync` on startup |
| PlaceBlock | Separate from build; works for single blocks |
| /command filtering | Server commands logged but not parsed |

## 🐛 Known Issues (from live logs 2026-06-27)

### Issue 1: SILENT PLACE FAILURES → STALL
Multiple `place` actions dispatch but never receive `BlockPlacedEvent` or `BlockPlaceSkippedEvent`. After 5s timeout they go to `TimedOut`. The governor only sees inventory didn't change → STALL.

**Evidence:**
```
[14:14:41] [correlation] place f14ba404 TIMED OUT after 8.2s - no result event received
[14:14:41] [correlation] place ff28861c TIMED OUT after 8.1s - no result event received
... 8 more timeouts in the same cycle ...
```

**Root causes (probable):**
1. Bot standing at target position — step-aside works but pathfinder may take >5s, then timeout fires before placement
2. Facing-dependent blocks (beds, doors) fail because the adapter tries all 6 reference faces but the block itself has collision rules
3. Scaffold fallback takes too long — digging down to ground, placing scaffold, then placing target block

### Issue 2: FACING-DIRECTION BLOCKS
Blocks with facing (beds, doors, furnaces, stairs) place in wrong orientation or fail entirely when all reference faces are tried. The adapter tries all 6 faces blindly; some face directions are invalid for certain blocks.

**Example:** `red_bed` needs two adjacent blocks facing the same direction. The adapter places one half (from a valid reference face) but the second half collides with the bot's position.

### Issue 3: ROOF HOLES + FURNITURE ISSUES
The build checkpoint (`BuildFactKeys.BlockStatus`) advances on `BlockPlacedEvent` confirmation. When a placed block has the wrong facing (e.g., a slab oriented wrong), the checkpoint still advances but the build is visually broken. There's no post-placement validation.

### Issue 4: GetStatus TIMEOUT (36 seconds)
```
[14:14:51] [correlation] GetStatus 62f0badd TIMED OUT after 36.7s - no result event received
```
This is the inventory-stale guard's GetStatus. It dispatched but never completed, meaning no fresh StatusEvent arrived for 36s. During this time the agent may operate on stale inventory.

### Issue 5: STALL MESSAGE MISSING BLOCK-LEVEL DETAIL
The enhanced stall message says `"185/217 blocks placed"` but doesn't say WHICH blocks are failing or WHY. `BuildStallDetail()` needs to list the specific block indices that timed out.

---

## 🔧 Immediate Implementation Plan (P0 for Round 4)

### 1. ENHANCE `LlmEvaluatorImpl` WITH BUILD-AWARE CONTEXT

**File:** `Agent.Planning/LlmEvaluatorImpl.cs`

Currently sends: goal name + world snapshot + last 10 ActionOutcomes. Missing:
- Build progress: which block indices are placed/skipped/timed-out
- Recent `BlockPlaceSkippedEvent` reasons (occupied, terrainOccupied, botPosition)
- Per-block facing/direction data from the blueprint

**Implementation sketch:**
```csharp
private static string BuildUserMessage(IGoal goal, IReadOnlyList<ActionOutcome> outcomes, WorldState worldState)
{
    var sb = new StringBuilder();
    sb.AppendLine($"Goal: {goal.Name}");
    
    // ADD: build-specific diagnostic detail
    if (goal is IBuildGoal bg)
    {
        var (placed, skipped, remaining) = CountBlockStatuses(bg.Blueprint.Name, worldState);
        sb.AppendLine($"Build progress: {placed} placed, {skipped} skipped, {remaining} remaining");
        
        // ADD: skipped block reasons
        var skipReasons = GetRecentSkipReasons(worldState, bg.Blueprint.Name, max: 5);
        if (skipReasons.Count > 0)
            sb.AppendLine($"Recent skip reasons: {string.Join("; ", skipReasons)}");
    }
    
    // EXISTING: world snapshot + outcomes
    sb.AppendLine($"World: HP={worldState.Health}/20, Food={worldState.Food}/20, Pos=...");
    // ...
}
```

### 2. ADD `PlaceBlockSkippedEvent` FACT TRACKING

**File:** `WebUI.Blazor/AgentBackgroundService.cs` — `ProcessEventsAsync`

Currently `BlockPlaceSkippedEvent` just logs and advances the checkpoint. It should also store the skip reason as a world fact so `BuildStallDetail()` can report it.

```csharp
// In BlockPlaceSkippedEvent handler:
var skipKey = $"build:{blueprintId}:block:{blockIndex}:skipReason";
_worldState = _worldState.With(b => b.SetFact(skipKey, reason, FactSource.Observed));
```

### 3. ENRICH `BuildStallDetail()` WITH SKIP REASONS

**File:** `WebUI.Blazor/AgentBackgroundService.cs`

Currently: `"185/217 blocks placed. Recent place timeouts: f14ba404."`

Should be: `"185/217 blocks placed. 5 timed out, 3 skipped (occupiedBy_?, terrainOccupied). Recent timeouts: blocks #8, #9, #182."`

### 4. LLM REPLANNING: ACT ON THE LLM'S RECOMMENDATION

**File:** `WebUI.Blazor/AgentBackgroundService.cs` — `TryLlmReplanOnStallAsync`

Currently: calls `ILlmEvaluator.EvaluateAsync` and logs the result. Does NOT act on it.

**Change:** When `shouldReplan == true`, also ask the LLM for a specific remediation step (e.g., "step back 3 blocks and retry block #187", "skip block #9 and continue"). Parse the JSON response and either:
- Clear+replan (if fundamental issue)
- Inject a remediation action (MoveTo + retry)
- Skip the problematic block and advance checkpoint

### 5. FIX GetStatus TIMEOUT / STALE INVENTORY

The 36-second GetStatus timeout suggests the adapter is overwhelmed or the StatusEvent isn't emitted. Add a timeout gate: if GetStatus doesn't complete within 10s, treat inventory as "not stale" and proceed.

---

## 📁 Key Files Map

| File | What's There | What Needs Changing |
|---|---|---|
| `Agent.Planning/LlmEvaluatorImpl.cs` | Existing LLM evaluator with `{replan:bool}` response | Add build-aware context to `BuildUserMessage` |
| `WebUI.Blazor/AgentBackgroundService.cs` | `BuildStallDetail()`, `TryLlmReplanOnStallAsync()`, `ProcessEventsAsync` | Enrich stall detail with block-level skip reasons; act on LLM replan decision; store skip reasons as facts |
| `MineflayerAdapter/index.js` | Step-aside (line 847), reference face loop (line 925), scaffold fallback (line 974) | May need facing-direction awareness for beds/doors/stairs |
| `Agent.Core/Interfaces/ILlmEvaluator.cs` | `Task<bool> EvaluateAsync(goal, outcomes, worldState, ct)` | Consider extending to return `EvaluationResult` with suggestion string |
| `Agent.Planning/LlmChatInterpreter.cs` | `BuildSystemPrompt` — already injects tool names, memory, command status | Add entity/block context from adapter events (TSK-0215/0217) |

---

## 🧪 Validation Plan

- `dotnet test` → **742 tests pass** (regression gate)
- `pwsh Scripts/Test-TaskRecords.ps1` → pass
- `dotnet build` → **0 warnings**
- Battle-test: place `red_bed`, `oak_door`, `furnace` — verify facing is correct
- Battle-test: build with bot standing at origin — verify step-aside works
- Battle-test: stall → verify chat message includes block indices and skip reasons

---

## 🔗 References

- Previous handoff: `Data/Pages/Handoffs/chat-harness-llm-steering-wave.md`
- Architecture: `Data/Pages/architecture.md`
- AGENTS.md: root of repo
- Logs: `WebUI.Blazor/logs/memorysmith-agent-20260627.log`
