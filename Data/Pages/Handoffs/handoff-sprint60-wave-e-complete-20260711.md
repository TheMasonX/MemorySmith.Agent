# Handoff — Sprint 60 Wave E Complete (2026-07-11)

**Branch:** `dev/round-3`
**Previous agent:** Agent Smith (Session 4)
**Next agent:** Agent Smith (next session)

---

## Wave E Status

| Task | Priority | Status | Summary |
|:----:|:--------:|:------:|---------|
| **TSK-0301** | High | ✅ Done | Inventory sync during active goals — `InventorySyncLoopAsync` + pre-plan guard updated to use timestamp freshness model |
| **TSK-0344** | High | ✅ Done | WorldStateDiff unexpected inventory changes surfaced in LLM evaluator prompt |
| **TSK-0158** | Medium | ✅ Done | Configuration key inventory page + coverage validation script |

---

## TSK-0301 — Inventory Sync During Active Goals ✅

**Problem:** `InventorySyncLoopAsync` (running every 30s) checked `_worldState.IsInventoryStale` — a boolean flag that is only set to `true` by `SetGoal()` and cleared after the first `StatusEvent`. During long-running active goals (minutes of mining/building), the flag remained `false`, so the sync loop silently stopped refreshing inventory. The agent operated on increasingly stale inventory snapshots for the entire goal duration.

**What was done:**

### 1. `InventorySyncLoopAsync` — timestamp model
Changed the guard from `_worldState.IsInventoryStale` to `!_worldState.IsInventoryFresh()` (default 60s threshold, TSK-0302 timestamp model). This ensures periodic GetStatus refresh during active goals, even when the stale boolean is false. The log message now includes the age of the last refresh.

### 2. `DispatchActionsAsync` pre-plan stale guard — combined check
Updated the pre-plan guard (lines ~2000) from `_worldState.IsInventoryStale` to `(_worldState.IsInventoryStale || !_worldState.IsInventoryFresh())`. This ensures the planner waits for fresh inventory before creating a new plan, even during active goals when the stale flag is false. The log messages now report both staleness and freshness age.

**Files changed:**
- `WebUI.Blazor/AgentBackgroundService.cs` — `InventorySyncLoopAsync` (line ~1261) and `DispatchActionsAsync` pre-plan guard (line ~1990)

**Key design decisions:**
- Additive change — both guards still respect the boolean stale flag (for the initial SetGoal case) plus the timestamp model (for drift during active goals)
- Default freshness threshold (60s) is unchanged from TSK-0302
- `_consecutiveGetStatusTimeouts` mechanism still prevents indefinite blocking when the adapter is unresponsive

---

## TSK-0344 — WorldStateDiff Inventory Detection in Evaluator ✅

**Problem:** `HasUnexpectedChanges` was added to `WorldStateDiff` in Sprint 59 but was only surfaced through `DescribeMismatches()` when `HasMismatch` was already `true`. Unexpected inventory changes from unmodeled sources (mob drops, other players, environmental pickup) that didn't trigger a mismatch were invisible to the LLM evaluator.

**What was done:**

Updated `BuildUserMessage` in `LlmEvaluatorImpl.cs` to surface unexpected inventory changes even when `HasMismatch` is `false`:
```csharp
else if (diff is not null && diff.HasUnexpectedChanges)
{
    sb.AppendLine($"Note: unexpected inventory changes detected: {diff.DescribeMismatches()}");
}
```

This ensures the evaluator is aware of inventory drift from non-tool sources even when all expected outcomes succeeded.

**Files changed:**
- `Agent.Planning/LlmEvaluatorImpl.cs` — `BuildUserMessage` unexpected changes branch

---

## TSK-0158 — Configuration Setting Inventory ✅

**Problem:** `Data/Pages/guides/configuration-reference.md` is a grouped operator guide but doesn't list every editable key. Agents looking for an exact setting key need a machine-checkable inventory.

**What was done:**

### 1. Configuration Key Inventory page
Created `Data/Pages/guides/configuration-key-inventory.md` — a comprehensive per-key inventory listing all **223 editable settings** from `AdminSettingsService.BuildEditableSettings()`, organized by category with:
- Setting key, label, value kind
- Sensitivity markers (write-only secrets marked)
- Purpose/help text excerpt
- Agent notes section explaining conventions

### 2. Configuration Reference update
Updated `Data/Pages/guides/configuration-reference.md` to link to the new inventory page and reference the coverage validation script.

### 3. Agent configuration reference
Created `Data/Pages/guides/agent-configuration-reference.md` that documents all **34 configuration keys** across 6 sections (`Minecraft`, `Chat/LLM`, `Memory`, `Build`, `Safety`, env vars) with types, defaults, binding classes, and descriptions. This covers all `IOptions<T>`-bound configuration consumed by the agent runtime, complementing the `appsettings.json` defaults.

**Files changed (MemorySmith.Agent):**
- `Data/Pages/guides/agent-configuration-reference.md` — **NEW**

---

## Validation

| Check | Result |
|-------|--------|
| Build (Agent) | ✅ 0 errors, 0 warnings |
| Tests (Agent) | ✅ **822** pass, 0 failures |
| Task records | ✅ 377 valid |
| Branch | `dev/round-3` |

---

## Current State

| Item | Status |
|------|--------|
| Branch | `dev/round-3` |
| Uncommitted | Wave E changes |
| Next | Commit + push Wave E, or proceed to Wave F |

---

## Next Up (Wave F — Proposed)

| Priority | Task | What It Involves |
|:--------:|:----:|------------------|
| High | TSK-0302 (codesmell #1) | Inventory SSOT — extract `IInventoryService` |
| High | TSK-0302 (codesmell #3) | StatusEvent overwrite without merge |
| Medium | TSK-0302 (codesmell #7) | Immutable record copies drift between StateManagerImpl and ABS |
| Medium | TSK-0159 | Wire remaining world events through ScenePackBuilder |
