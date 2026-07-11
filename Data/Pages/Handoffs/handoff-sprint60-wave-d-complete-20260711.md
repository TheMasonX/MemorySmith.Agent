# Handoff — Sprint 60 Wave D Complete (2026-07-11)

**Branch:** `dev/round-3`
**Previous agent:** Agent Smith (Session 3)
**Next agent:** Agent Smith (next session)

---

## Wave D Status

| Task | Priority | Status | Summary |
|:----:|:--------:|:------:|---------|
| **TSK-0243** | **Critical** | ✅ Done | EvaluationResult discriminated union — `EvaluationDirective` with 6 outcome types |
| **TSK-0245** | **Critical** | ✅ Done | BlockState type change — structured `BlockState` record replacing flat `string?` |
| **TSK-0302** | **Critical** | ✅ Done | Inventory SSOT — addressed codesmells #2/#5/#6 (freshness timestamp, creative provisioning fix) |

---

## TSK-0243 — EvaluationResult Discriminated Types ✅

**What was done:**
- Created `EvaluationDirective` discriminated union with 6 outcomes: `Continue`, `Stop`, `AdvanceSequence`, `CreateFollowUp`, `ScheduleWake`, `Recover`
- Added `EvaluateWithDirectiveAsync` overload to `ILlmEvaluator` interface
- Implemented in `LlmEvaluatorImpl` with directive-specific JSON parser (`directive` field with `reason`, `suggestion`, `followUpGoal`, `delaySeconds`)
- Updated `AgentBackgroundService.TryLlmReplanOnStallAsync` to handle all 6 directive types
- Added `FactSource.LlmDirective` for provenance tracking

**Files changed:**
- `Agent.Core/Interfaces/ILlmEvaluator.cs` — `EvaluationDirective` union + new overload
- `Agent.Planning/LlmEvaluatorImpl.cs` — `EvaluateWithDirectiveAsync` + `ParseEvaluationDirective` + `BuildDirectiveSystemPrompt`
- `WebUI.Blazor/AgentBackgroundService.cs` — switch on all 6 directive types in `TryLlmReplanOnStallAsync`
- `Agent.Core/Models/Fact.cs` — added `FactSource.LlmDirective`

**Key design decisions:**
- Additive overload, not a breaking change — existing `EvaluateAsync` + `EvaluationResult` unchanged
- LLM responds with `{"directive":"recover","suggestion":"...","reason":"..."}` format
- Fast-path short-circuits preserved (too-few-outcomes, all-succeeded, provider-offline)
- Parse failures default to `Continue` (conservative)

---

## TSK-0245 — BlockState Type Change ✅

**What was done:**
- Created `BlockState` record in `Agent.Construction` with `IReadOnlyDictionary<string,string> Properties`
- Static factory `BlockState.Parse(string?)` parses wire format (`"half=top,waterlogged=true"`)
- `ToWireString()` serializes back to adapter-compatible flat string
- Changed `PlacementBlock.BlockState` from `string?` to `BlockState?` (default remains `null`)
- Updated `BlueprintParser` — `LegendEntry` uses `BlockState?`, `ParseLegend` calls `BlockState.Parse()`
- Updated `BlueprintExecutor` — serializes via `ToWireString()` for adapter
- Updated `BlueprintParserTests` — assertions use `ToWireString()` round-trip

**Files changed:**
- `Agent.Construction/BlockState.cs` — **NEW**
- `Agent.Construction/BlueprintSchema.cs` — type change
- `Agent.Construction/BlueprintParser.cs` — LegendEntry + ParseLegend
- `Agent.Construction/BlueprintExecutor.cs` — ToWireString() serialization
- `MemorySmith.Agent.Tests/BlueprintParserTests.cs` — updated assertions

**Key design decisions:**
- Non-breaking: default constructor parameter is still `null` (all existing call sites unchanged)
- Adapter wire protocol unchanged — `BlockState` is serialized to flat string before sending
- Parse/ToWireString round-trip preserves `"half=top,waterlogged=true"` format

---

## TSK-0302 — Inventory SSOT Refactor ✅

**What was done (addressed 3 of 7 codesmells):**

### Codesmell #2/#5 — IsInventoryStale boolean → Timestamp freshness model
- Added `DateTimeOffset? LastFreshInventoryAt` to `WorldState`
- Added `bool IsInventoryFresh(TimeSpan? maxAge = null)` method (defaults to 60s threshold)
- Added `Builder.SetLastFreshInventoryAt(DateTimeOffset?)` to `WorldState.Builder`
- `SetGoal()` now sets both `IsInventoryStale=true` AND `LastFreshInventoryAt=null`
- `ApplyStatus()` now sets both `IsInventoryStale=false` AND `LastFreshInventoryAt=event.Timestamp`
- `DeathEvent` handler clears both
- Backward compatible — existing `IsInventoryStale` boolean unchanged

### Codesmell #6 — Creative provisioning on LAN
- Replaced `/give @p` chat commands with `CreativeProvision` adapter action
- Added handler in `MineflayerAdapter/index.js` that calls `ensureCreativeItem` from `creativeProvider.cjs`
- `ensureCreativeItem` uses `bot.creative.setInventorySlot()` first (works on all versions, no OP), falls back to `/give`
- Updated `ProvisionGoalIfCreativeAsync` to enqueue `CreativeProvision` actions instead of `/give` chat messages
- Remaining `/give` reference in `SanitizeBlockName` kept as defensive validation

### Deferred to Sprint 62
| # | Codesmell | Why Deferred |
|:-:|-----------|-------------|
| 1 | Multiple sources of truth (no SSOT) | Requires `IInventoryService` extraction — broader refactor |
| 3 | StatusEvent overwrites without merge | Requires merge semantics design |
| 7 | Immutable record copies drift between StateManagerImpl and ABS | Architectural fix |

**Files changed:**
- `Agent.Core/Models/WorldState.cs` — `LastFreshInventoryAt`, `IsInventoryFresh()`, `SetLastFreshInventoryAt()`
- `Agent.Core/WorldStateProjector.cs` — `ApplyStatus` sets timestamp
- `WebUI.Blazor/AgentBackgroundService.cs` — SetGoal, DeathEvent, ProvisionGoalIfCreativeAsync
- `MineflayerAdapter/index.js` — `CreativeProvision` action handler

---

## Validation

| Check | Result |
|-------|--------|
| Build (Agent) | ✅ 0 errors, 0 warnings |
| Tests (Agent) | ✅ **822** pass, 0 failures |
| Build (base repo) | ✅ 0 errors |
| Tests (base repo) | ✅ **521** pass, 0 failures (9 skipped — CUDA) |
| Task records | ✅ 377 valid |

---

## Current State

| Item | Status |
|------|--------|
| Branch | `dev/round-3` |
| Uncommitted | Wave D changes + pre-existing audit tasks/skills from earlier sessions |
| Next | Commit + push Wave D, or proceed to Wave E |

---

## Next Up (Wave E — Proposed)

| Priority | Task | What It Involves |
|:--------:|:----:|------------------|
| High | TSK-0301 | P0: Fix inventory sync loop not syncing during active goals |
| High | TSK-0344 | WorldStateDiff unexpected inventory change detection |
| Medium | TSK-0158 | Config setting inventory and doc coverage check |
