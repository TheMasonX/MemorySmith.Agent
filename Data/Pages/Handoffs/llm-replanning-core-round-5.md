# Handoff: LLM Replanning Core — Round 5

**Date:** 2026-06-28
**Branch:** `dev/round-3` (commits `ff9628c` → `41054d6` → `3245211`)
**Build:** 742 tests passing, 0 warnings
**Handoff author:** SteveBot (MemorySmith.Agent)

---

## 🎯 Thesis

**The LLM evaluator was silently skipping during build stalls because `_cycleOutcomes` always contained all-success outcomes for fire-and-forget tools (PlaceBlock).** With `forceEvaluate`, enriched context, auto-skip, and accurate diagnostics in place, the LLM can now see WHY builds stall and recommend specific remediation.

---

## ✅ What's New (Waves A + B)

### Wave A — LLM Replanning Core (5 tasks)

| Task | File(s) | What changed |
|---|---|---|
| **TSK-0217** | `Agent.Planning/LlmEvaluatorImpl.cs` | `BuildUserMessage` includes build progress, skip reasons, facing-sensitive block indices |
| **TSK-0218** | `Agent.Core/BuildFactKeys.cs`, `AgentBackgroundService.cs` | `SkipReason()` fact key; `MarkSkippedBlock()` stores `botPosition`/`occupiedBy_X`/`noReference` as world facts |
| **TSK-0219** | `AgentBackgroundService.cs` | `BuildStallDetail()` reports timeout block indices, skipped count, unique skip reasons |
| **TSK-0220** | `ILlmEvaluator.cs`, `LlmEvaluatorImpl.cs`, `AgentBackgroundService.cs` | `EvaluationResult` with `ShouldReplan`/`Reason`/`Suggestion`; `TryLlmReplanOnStallAsync` queues Chat + clears queue |
| **TSK-0221** | `AgentBackgroundService.cs` | GetStatus timeout 30s→10s; `_consecutiveGetStatusTimeouts` clears `IsInventoryStale` after 2 failures |

### TSK-0223 — forceEvaluate (critical bug fix)

**Root cause:** `_cycleOutcomes` always contains all-SUCCESS outcomes for build goals because `PlaceBlock`/`MineBlock` are fire-and-forget — `CallWithOutcomeAsync` returns success immediately on dispatch. Real failures appear in `_correlatedActions`, never in `_cycleOutcomes`. The evaluator's `failureCount == 0` fast-path always triggered → LLM never called.

**Fix:** Added `forceEvaluate` parameter to `ILlmEvaluator.EvaluateAsync`. `TryLlmReplanOnStallAsync` passes `forceEvaluate: true` — bypasses fast-paths. Governor stall declaration IS the failure signal.

### Wave B — Stall Diagnostics & Auto-Skip (3 tasks)

| Task | What | Fix |
|---|---|---|
| **TSK-0224** | `"217/217 placed, 101 timed out"` contradictory | Exclude blocks marked "placed" in world facts from timeout count (sweep races with BlockPlacedEvent) |
| **TSK-0225** | `"Skip reasons: occupiedBy_?"` | WebSocketBridge maps missing `existingBlock` to `"?"` — treat as empty → `"noReference"` |
| **TSK-0226** | Blocks 1,7,8,11,23 timing out forever | `_blockTimeoutCounts` per-block tracking; auto-skip after 3 consecutive timeouts (`autoSkip_timeout`) |

---

## 📊 What's Working Well

| Component | Status |
|---|---|
| LLM chat interpretation (DeepSeek) | ✅ Parses compound commands, place/build/gather intents |
| Multi-step chaining | ✅ `TaskSequenceGoal` with `TryAdvanceSequence` |
| Auto-tool crafting | ✅ `GatherGoalDecomposer` pre-crafts tools |
| Cross-session memory | ✅ `IMemoryGateway.LoadSessionFactsAsync` on startup |
| Build progress tracking | ✅ Per-block status facts (placed/skipped/pending/in-progress) |
| Skip reason tracking | ✅ Stored as world facts for LLM and stall diagnostics |
| Stall messaging | ✅ Includes block indices, skip reasons, accurate timeout counts |
| LLM evaluator (chat path) | ✅ Now called with build-aware context |
| LLM evaluator (stall path) | ✅ `forceEvaluate` bypasses fire-and-forget gap |
| Auto-skip | ✅ Blocks timing out 3x are auto-skipped |
| GetStatus timeout | ✅ 10s timeout, 2-failure gate |

---

## 🐛 Remaining Issues

### Issue 1: FACING-DIRECTION BLOCKS (adapter-level)

Blocks with facing (beds, doors, furnaces, stairs, slabs) place in wrong orientation or fail entirely. The adapter tries all 6 reference faces blindly in `MineflayerAdapter/index.js`.

**Key code:** `index.js` lines 920-955 — the `refFaces` array iterates 6 faces in fixed order (ground, ceiling, west, east, north, south). No orientation data is passed from the blueprint.

**Fix needed:** Blueprint format needs facing data (`PlacementBlock` → add `Facing` field). Adapter needs to prefer specific face vectors for orientation-sensitive blocks.

### Issue 2: ROOF HOLES + FURNITURE (consequence of Issue 1)

When a block places with wrong facing (e.g., slab oriented wrong), `BlockPlacedEvent` fires and checkpoint advances. Build is visually broken. Fixing Issue 1 fixes this.

### Issue 3: BLUEPRINT FORMAT EXTENSION

`PlacementBlock` currently has `(X, Y, Z, BlockId)`. Needs `Facing` and possibly `BlockState` for stairs/slabs/doors/beds.

---

## 📁 Key Files Map

| File | What's There | What Needs Changing (Wave C) |
|---|---|---|
| `MineflayerAdapter/index.js` | `refFaces` loop (line 920), `placeBlock` call (line 952) | Add facing-aware placement; prefer specific face vectors for orientation-sensitive blocks |
| `Agent.Construction/BlueprintSchema.cs` | `PlacementBlock(X, Y, Z, BlockId)` | Add `Facing` and `BlockState` fields |
| `Agent.Construction/BlueprintParser.cs` | Parses markdown blueprints | Parse facing/state from extended format |
| `Agent.Planning/LlmEvaluatorImpl.cs` | Build-aware context with facing-sensitive block list | Already reports facing-sensitive blocks to LLM |
| `WebUI.Blazor/AgentBackgroundService.cs` | `BuildStallDetail`, `TryLlmReplanOnStallAsync`, `SweepTimedOutActions` | Auto-skip, forceEvaluate, accurate diagnostics all wired |

---

## 🔧 Implementation Notes for Wave C

### 1. Blueprint Format Extension

```csharp
// Before:
public record PlacementBlock(int X, int Y, int Z, string BlockId);

// After:
public record PlacementBlock(int X, int Y, int Z, string BlockId, 
    string? Facing = null, string? BlockState = null);
```

Where `Facing` is one of: `north`, `south`, `east`, `west`, `up`, `down`.

### 2. Adapter Facing-Aware Placement

The adapter's `refFaces` loop should prefer face vectors that produce the correct orientation:

```javascript
// For blocks with explicit facing, prefer specific face vectors
const FACING_VECTORS = {
  north: { fx: 0, fy: 0, fz: -1 },  // place against south face → faces north
  south: { fx: 0, fy: 0, fz: 1 },   // place against north face → faces south
  east:  { fx: 1, fy: 0, fz: 0 },   // place against west face → faces east
  west:  { fx: -1, fy: 0, fz: 0 },  // place against east face → faces west
};
```

### 3. Blueprint Parser Extension

Parse `Facing: north` and `BlockState: half=top` from the markdown blueprint format.

---

## 🧪 Validation Plan

- `dotnet test` → **742 tests pass** (regression gate)
- `pwsh Scripts/Test-TaskRecords.ps1` → pass (220 records)
- `dotnet build` → **0 warnings**
- Battle-test: restart app, verify LLM is called during stall (look for `[evaluator]` or `[llm-replan]` in logs)
- Battle-test: verify auto-skip kicks in after 3 consecutive place timeouts

---

## 🔗 References

- Previous handoff: `Data/Pages/Handoffs/llm-replanning-core-round-4.md`
- Architecture: `Data/Pages/architecture.md`
- AGENTS.md: root of repo
- Logs: `WebUI.Blazor/logs/memorysmith-agent-20260627.log`
- Task system: TSK-0217 through TSK-0229 (all Done) ✅

---

## 🚀 Wave C — COMPLETED — Facing-Direction Blocks & Blueprint Extension

**Status:** ✅ Complete (2026-06-28) | **Tasks:** TSK-0227, TSK-0228, TSK-0229 — all Done | **Build:** 746 tests, 0 warnings

### What Changed

| Task | File(s) | What changed |
|---|---|---|
| **TSK-0227** | `BlueprintSchema.cs`, `BlueprintParser.cs`, `BlueprintParserTests.cs` | `PlacementBlock` gains `Facing?` and `BlockState?` (both `null` default). Parser uses internal `LegendEntry` record; legend supports `\| facing: north` and `\| blockState: half=top` annotations. 4 new tests. |
| **TSK-0228** | `BlueprintExecutor.cs` | Passes `block.Facing` → `args["facing"]` and `block.BlockState` → `args["blockState"]` when non-null (2 lines). |
| **TSK-0229** | `MineflayerAdapter/index.js` | `FACING_VECTORS` map (6 directions). When `args.facing` provided, prefer that face vector FIRST. Falls through to full 6-face loop if preferred face has no solid reference. Structured debug log for facing-aware placement. |

### Blueprint Legend Format (new)

```
## Legend
- `D` = oak_door | facing: north
- `S` = oak_slab | facing: up | blockState: half=top
- `C` = cobblestone
```

Backward-compatible: existing blueprints without `\|` annotations work unchanged.

### Remaining Known Issues (after Wave C)

| Issue | Status | Notes |
|---|---|---|
| Roof holes + furniture | Mitigated | Facing awareness in place; blueprint authors need to annotate legends |
| Blueprint battle-test | Pending | Need live test with annotated blueprint to verify end-to-end |
| `orientationSensitiveBlocks` set | Deferred | Adapter currently applies facing preference to all blocks (harmless, just slightly slower for cobblestone). Could add a set later. |
