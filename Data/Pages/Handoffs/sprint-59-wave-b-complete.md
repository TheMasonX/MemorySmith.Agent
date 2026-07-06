# Sprint 59 — Wave B Complete (Inventory & Evaluation)

**Date:** 2026-07-06  
**Branch:** `dev/round-3`  
**HEAD:** `9801858` — Sprint 59 Wave B: evaluator circuit breaker pre-call guard (TSK-0325)  
**Agent:** SteveBot  

---

## What Was Delivered

### Wave B Tasks

| Task | Title | Status | Key Fix |
|------|-------|--------|---------|
| **TSK-0321** | Inventory sync during active goals | ✅ Verified (already done in Sprint 58) | Idle-only guard removed, stacked-delay fixed, `IsInventoryStale` prevents redundant syncs |
| **TSK-0325** | Circuit breaker cooldown (60s) + pre-call guard | ✅ Completed | Cooldown 5min→60s, added pre-call guards in DispatchActionsAsync + TryLlmReplanOnStallAsync |
| **TSK-0320** (remaining) | Step context in evaluator prompt | ✅ Verified (already done in Sprint 58) | TaskSequenceGoal branch delegates to current step's type-specific context |

### TSK-0325 Detail

The circuit breaker infrastructure (counter, `_llmEvalSuppressUntil` timestamp, logging) was added in Sprint 58 but was **post-call only** — it logged suppression but never actually prevented evaluator calls during cooldown. The infinite `fail → log → call → fail` loop continued unabated.

**Changes in `AgentBackgroundService.cs`:**
1. `LlmEvalCooldown`: `TimeSpan.FromMinutes(5)` → `TimeSpan.FromSeconds(60)` (matches handoff spec)
2. **Pre-call guard in `DispatchActionsAsync`**: When `_timeProvider.UtcNow < _llmEvalSuppressUntil`, skips the evaluator call entirely and logs debug
3. **Pre-call guard in `TryLlmReplanOnStallAsync`**: Returns early during cooldown, letting governor's deterministic backoff handle the stall
4. Log message unit: `{CooldownMin}m` → `{CooldownSec}s`

### Validation

- `dotnet build` — **0 warnings, 0 errors**
- `dotnet test` — **815/815 passed, 0 failed**

---

## Wave B Status Summary

| Scope | Total | Done | Verified (already) |
|-------|-------|------|--------------------|
| MSA Wave B | 3 | 1 (TSK-0325) | 2 (TSK-0321, TSK-0320 step context) |

---

## Files Changed

- `WebUI.Blazor/AgentBackgroundService.cs` — 3 changes:
  - `LlmEvalCooldown` constant (line ~234): 5min → 60s
  - Pre-call guard in DispatchActionsAsync (line ~2199)
  - Pre-call guard in TryLlmReplanOnStallAsync (line ~3365)
  - Log message unit fix

---

## Remaining Waves

### Wave C — Safety & Config Hardening (4 tasks)

| Task | Title | Status |
|------|-------|--------|
| **TSK-0340** | Stub evaluator diagnosis channel — add `EvaluationResult.Diagnosis` field | Backlog |
| **TSK-0341** | Fix PlaceBlockGoalDecomposer same-coordinate placements | Backlog |
| **TSK-0324** | Change DeniedCommands to additive-only merge | Ready (already done? — `DeniedCommands` property uses union merge) |
| **TSK-0318** | Complete denylist normalization (both sides) | Backlog |

### Wave D — Adapter Resilience (2 tasks)

| Task | Title | Status |
|------|-------|--------|
| **TSK-0337** | Fix WebSocketBridge reconnect retry loop | Backlog |
| **TSK-0339** | Fix Node adapter reconnect exponential backoff | Backlog |

---

## Handoff Notes

1. **TSK-0321 and TSK-0320 were already completed** in Sprint 58 Wave C. The handoff doc marked them as "Ready" but the code had both fixes in place.
2. **TSK-0324 may already be done** — the `DeniedCommands` property already uses union-merge (`new HashSet<string>(DefaultDeniedCommands)` + `foreach (var cmd in configured) merged.Add(cmd)`). Verify before marking complete.
3. **Wave C next**: TSK-0340 (Diagnosis field), TSK-0341 (PlaceBlock coords), TSK-0318 (denylist normalization), and verify TSK-0324.
