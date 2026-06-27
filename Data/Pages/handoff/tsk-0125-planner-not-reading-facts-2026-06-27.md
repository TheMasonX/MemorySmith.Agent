# Handoff: TSK-0125 Per-Block Tracking — Planner Not Seeing Placed Facts

**Date:** 2026-06-27
**Branch:** `main` (TSK-0125 implemented, 742 tests pass)
**Build timestamp:** `2026-06-27T14:27:43Z`

## Symptom

`DecomposeBuild` returns **216 actions every replan cycle** even after blocks are confirmed placed. The governor reports STALLED because the plan fingerprint never changes. The bot builds the cobblestone floor successfully but gets stuck in a stall loop at the mid-wall layer.

## What's Working

- `SetBlockStatus` writes per-block facts: `[build] block Small Survival House #7 → placed`
- Facts are logged correctly (block index and blueprint name visible)
- `BlockPlacedEvent` handlers call `AdvanceBuildCheckpoint` → `SetBlockStatus`
- `ClearFactsByPrefix` added to `WorldState.Builder` ✅
- `BuildProgressReport` model class exists ✅
- TSK-0128 late-event correlation works (no "cleaned up stale context" messages) ✅

## What's Broken

`EmitBuildPlacementLoop` in `HtnTaskLibrary.cs` reads per-block status facts but the planner still emits all 215 PlaceBlock actions on every replan.

## Suspected Root Causes (in order of likelihood)

### 1. Blueprint name mismatch in fact keys (MOST LIKELY)
- **Writer** (`SetBlockStatus`): uses `blueprintId` from `_placeBlockContexts` = `blueprint.Name` = `"Small Survival House"`
- **Reader** (`DecomposeBuild` → `EmitBuildPlacementLoop`): uses `blueprint.Name` = `"Small Survival House"`
- **Cleanup** (`ClearBuildFacts` in SetGoal): uses `goal.Name["Build:".Length..]` = `"small-house"` ← **wrong prefix**

The writer and reader should match (both `"Small Survival House"`), but the cleanup doesn't.
**Verify**: Add `logger.LogDebug` in `EmitBuildPlacementLoop` to dump the first 5 fact key lookups and their results.

### 2. Timing — replan fires before SetBlockStatus completes
- Replan fires every 2s from the settle loop
- `SetBlockStatus` is called from `ProcessEventsAsync` which runs concurrently
- `_worldState.With(...)` creates a new record — but the planner captures a snapshot at the start of `PlanAsync`
- If `SetBlockStatus` fires between `PlanAsync` start and the actual `DecomposeBuild` call, the fact should be visible
- **Verify**: Confirm that `_worldState` is NOT being overwritten by `_projector.Apply()` between `SetBlockStatus` and the next `PlanAsync`

### 3. Fact eviction from MaxFacts=1000
- `SetFact` trims `StructuredFacts` when > 1000 entries (oldest-first)
- If many facts are accumulating from event traffic, build facts could be evicted
- **Verify**: Log `_worldState.Facts.Count` and `_worldState.StructuredFacts.Count` at plan time

### 4. StringBuilder/string comparison issue
- `statusVal?.ToString() == BuildFactKeys.BlockStatusPlaced` — should work for string values
- But if the fact was stored as something else (e.g., `JsonElement`), `.ToString()` might return a different format
- **Verify**: Add explicit type check: `statusVal is string s && s == "placed"`

## Key Files

| File | What Changed |
|---|---|
| `Agent.Core/BuildFactKeys.cs` | `BlockStatus()`, `BlockStatusPrefix()`, status constants |
| `Agent.Core/Models/WorldState.cs` | `ClearFactsByPrefix()` on Builder |
| `Agent.Core/Models/BuildProgressReport.cs` | New model class |
| `WebUI.Blazor/AgentBackgroundService.cs` | `SetBlockStatus()`, `MarkSkippedBlock()`, `LogBuildProgress()`, `ClearBuildFacts()` |
| `Agent.Planning/HtnTaskLibrary.cs` | `EmitBuildPlacementLoop` — per-block status filtering |
| `Agent.Planning/Decomposition/BuildGoalDecomposer.cs` | (unchanged, but calls `DecomposeBuild` with `bg.Blueprint`) |

## Quick Diagnostic

Add this at the top of `EmitBuildPlacementLoop` (in the placement loop):

```csharp
if (i < 5) // only log first 5 blocks for diagnostics
{
    var statusKey = BuildFactKeys.BlockStatus(blueprint.Name, i);
    var found = state.Facts.TryGetValue(statusKey, out var sv);
    System.Console.Error.WriteLine(
        $"[TSK-0125-DIAG] block {i}: key={statusKey}, found={found}, value={sv?.ToString() ?? "null"}");
}
```

Expected output after blocks are placed:
```
[TSK-0125-DIAG] block 0: key=build:Small Survival House:block:0:status, found=True, value=placed
[TSK-0125-DIAG] block 1: key=build:Small Survival House:block:1:status, found=True, value=placed
```

If `found=False`, the fact isn't being written or the key doesn't match.

## Running the Agent

```powershell
D:\@repos\memorysmith.agent\scripts\Start-Agent.ps1
```

Connect to Minecraft LAN world, type `leo build a house`.

## Next Steps

1. Add diagnostic logging (above) to confirm fact keys and values
2. If keys match but facts aren't found → investigate timing/threading
3. If keys don't match → fix the name mismatch (likely `Blueprint.Name` vs `Blueprint.Id`)
4. Fix `ClearBuildFacts` to use `blueprint.Name` instead of extracted goal name substring
5. After fix, verify plan count decreases as blocks are placed (should see `place×N` where N < 215)
