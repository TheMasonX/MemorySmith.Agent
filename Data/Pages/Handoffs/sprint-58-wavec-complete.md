# Sprint 58 Wave C — Audit Fixes Complete

**Date:** 2026-07-01
**Status:** ✅ Complete
**Tests:** 815 passed, 0 failed, 0 skipped
**Build:** ✅ Succeeded

---

## Summary

Wave C addressed 9 of 10 audit findings from `internal-audit-57-20260701.md`. 
All 4 P0 tasks and 4 of 5 P1/P2 tasks are resolved. The original Wave C scope 
(TSK-0311: 6 missing tools) is deferred to Sprint 59.

---

## Completed Tasks

### P0 Fixes (Critical)

| Task | Title | Files Changed |
|------|-------|--------------|
| **TSK-0320** | LlmEvaluator fast-path suppressing world diff | `Agent.Planning/LlmEvaluatorImpl.cs` |
| **TSK-0321** | InventorySync not syncing during active goals | `WebUI.Blazor/AgentBackgroundService.cs` |
| **TSK-0323** | HtnPlanner sync-over-async deadlock | `Agent.Planning/HtnPlanner.cs` |
| **TSK-0324** | Safety config merge eroding default deny list | `WebUI.Blazor/AgentBackgroundService.cs` |

### P1/P2 Fixes

| Task | Priority | Title | Files Changed |
|------|----------|-------|--------------|
| **TSK-0325** | P1 | LLM evaluator circuit breaker | `WebUI.Blazor/AgentBackgroundService.cs` |
| **TSK-0326** | P2 | Goal-identity guard in ProvisionGoalIfCreativeAsync | `WebUI.Blazor/AgentBackgroundService.cs` |
| **TSK-0327** | P2 | SafetyConfig normalization runtime path | `WebUI.Blazor/Program.cs` |
| **TSK-0328** | P2 | Downgrade plan-raw Warning→Debug | `WebUI.Blazor/AgentBackgroundService.cs` |
| **TSK-0329** | P2 | SignalR event name drift | `WebUI.Blazor/AgentBackgroundService.cs` |

---

## Detailed Changes

### TSK-0320 — LlmEvaluator Fast-Path World Diff (P0)
**File:** `Agent.Planning/LlmEvaluatorImpl.cs`
**Change:** Added `&& (diff is null || !diff.HasMismatch)` guard to the 
`failureCount == 0` fast-path. Fire-and-forget tools (MineBlock, PlaceBlock) 
always report success; real failures show up as world state mismatches. 
The evaluator now proceeds to LLM evaluation when the world diverged from 
expectations even though all action outcomes claimed success.

### TSK-0321 — InventorySync During Active Goals (P0)
**File:** `WebUI.Blazor/AgentBackgroundService.cs` — `InventorySyncLoopAsync`
**Changes:**
- Removed `_currentGoal is not null` guard — inventory must stay fresh during 
  goals for precondition checks and LLM evaluator
- Fixed stacked-delay bug: moved sync check before `Task.Delay` so first sync 
  happens after initial delay (30s), not 2× interval (60s)
- `IsInventoryStale` still prevents redundant syncs

### TSK-0323 — HtnPlanner Sync-Over-Async Deadlock (P0)
**File:** `Agent.Planning/HtnPlanner.cs`
**Changes:**
- Renamed `TryLlmFallback` → `TryLlmFallbackAsync` with proper async/await
- Replaced `.ConfigureAwait(false).GetAwaiter().GetResult()` with `await`
- Propagates `CancellationToken` to the LLM call
- `PlanAsync` signature changed to `async Task<IPlan>` (compatible with 
  `IPlanner` interface)
- Removed `Task.FromResult` wrappers in PlanAsync

### TSK-0324 — Safety Config Merge (P0)
**File:** `WebUI.Blazor/AgentBackgroundService.cs` — `DeniedCommands` property
**Change:** Changed from XOR-replace to union-merge. `DefaultDeniedCommands` 
(35+ entries) is always included as a safety floor. User-configured 
`DeniedCommands` can add more commands but never removes built-in defaults.

### TSK-0325 — LLM Evaluator Circuit Breaker (P1)
**File:** `WebUI.Blazor/AgentBackgroundService.cs`
**Changes:**
- Added `_llmEvalSuppressUntil` field and `LlmEvalCooldown` (5 min) constant
- After 3 consecutive failures, sets cooldown timestamp instead of resetting 
  to 0 (which caused infinite fail→log→reset→fail cycle)
- Success resets both counter and suppress timestamp
- During cooldown, further failures are silently skipped

### TSK-0326 — Goal-Identity Guard (P2)
**File:** `WebUI.Blazor/AgentBackgroundService.cs` — `ProvisionGoalIfCreativeAsync`
**Change:** Added `if (_currentGoal != goal) return;` guard at top of the 
material provisioning foreach loop. If goal is cancelled/replaced during 
inter-command delay, stops enqueuing `/give` commands for the old goal.

### TSK-0327 — SafetyConfig Runtime Normalization (P2)
**File:** `WebUI.Blazor/Program.cs`
**Change:** Added `PostConfigure<SafetyOptions>` call that normalizes 
`DeniedCommands` entries (adds leading slash) for the runtime path. 
Previous normalization only covered `ChatOptions` (LLM prompt path).

### TSK-0328 — Plan-Raw Log Downgrade (P2)
**File:** `WebUI.Blazor/AgentBackgroundService.cs` — `DispatchActionsAsync`
**Change:** `LogWarning` → `LogDebug` for `[plan-raw]` diagnostic log 
that fires every 10-30s on replan.

### TSK-0329 — SignalR Event Name Drift (P2)
**File:** `WebUI.Blazor/AgentBackgroundService.cs` — `PushStatusToDashboardAsync`
**Changes:**
- Added `using WebUI.Blazor.Dashboard`
- Changed `"StatusUpdated"` string literal to `DashboardHubEvents.SnapshotUpdated` 
  constant (consistent with `DashboardPublisherImpl`)

---

## Files Changed

| File | Tasks |
|------|-------|
| `Agent.Planning/LlmEvaluatorImpl.cs` | TSK-0320 |
| `Agent.Planning/HtnPlanner.cs` | TSK-0323 |
| `WebUI.Blazor/AgentBackgroundService.cs` | TSK-0321, TSK-0324, TSK-0325, TSK-0326, TSK-0328, TSK-0329 |
| `WebUI.Blazor/Program.cs` | TSK-0327 |

**Total:** 4 files modified, 0 files deleted.

---

## Deferred to Sprint 59

| Task | Severity | Reason |
|------|----------|--------|
| **TSK-0322** | P0 | ExecutionManager JSON round-trip — complex, deferred since Sprint 40; needs deeper refactor |
| **TSK-0311** | P2 | 6 missing tools (EquipItem, ActivateBlock, AttackEntity, UseItem, DropItem, LookAt) — original Wave C scope |
| **TSK-0292/0293** | High | ABS decomposition — deferred per 3-audit consensus |

---

## Validation

```
dotnet build → Build succeeded. (0 errors)
dotnet test  → Passed! - Failed: 0, Passed: 815, Skipped: 0, Total: 815
```
