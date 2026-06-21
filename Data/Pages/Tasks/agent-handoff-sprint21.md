# Agent Handoff — Sprint 21 Complete

**For:** Next agent session
**From:** Sprint 21 session (2026-06-18)
**Repo:** https://github.com/TheMasonX/MemorySmith.Agent
**Branch:** sprint-5-tool-safety (open PR #1, extended dev branch)
**CI:** GREEN on b0bf0f23944a4e58d024c5843e586282c1a80158
**Council:** APPROVED (sprint21-council-20260618.md)

---

## What Was Done This Sprint

### Trigger
Sprint 20 B-1 (stale inventory gate) was a blocking council condition that had to be delivered in Sprint 21.

### Delivered

**P0-A: WorldState.IsInventoryStale freshness gate (DELIVERED)**
Root cause of bug: after admin `/clear`, `GenericGatherGoal.IsComplete` read stale `_worldState.Inventory` and returned true immediately, causing the bot to claim it had items it didn't.

Fix:
- `WorldState.IsInventoryStale` bool property (default: false)
- `WorldState.Builder.SetInventoryStale(bool)` method
- `AgentBackgroundService.SetGoal`: marks inventory stale on every new goal
- `WorldStateProjector.ApplyStatus`: clears stale flag when StatusEvent arrives from GetStatus
- `GenericGatherGoal.IsComplete`: returns false when stale (both RequiresSmelting branches)
- Tests: 6 tests in `Sprint21FreshnessGateTests`

**P0-B: Governor pre-PlanAsync IsStalled check (DELIVERED)**
Root cause of bug (RC-2 from Sprint 20 audit): PlanAsync was called every 2s even during STALL because the governor check happened AFTER PlanAsync returned. Plan logs appeared during STALL, confusing operators.

Fix: In `DispatchActionsAsync`, added `if (replanGovernor?.IsStalled == true)` check BEFORE the PlanAsync block. When stalled: `Task.Delay(10s, ct)` then continue. PlanAsync is never called during STALL.
- Tests: 2 tests in `Sprint21GovernorPrePlanTests`

**P0-C: AGENTS.md doc fix (DELIVERED)**
Removed "inventory freshness gate" from Sprint 20 completed items (it was a false claim — gate was reverted in Sprint 20 and deferred).

**P1-A: Governor recovery log elevated (DELIVERED)**
`logger.LogDebug` → `logger.LogInformation` for progress-detected settle log. Operators watching console can now confirm when stagnation counter resets.

**P1-B: D-2 integration test (DELIVERED)**
4 tests in `Sprint21BlockNotFoundFactTests` verifying:
- `BlockNotFoundEvent` sets `event:BlockNotFound:Block` in WorldState.Facts
- `MinedCount` fact also set
- `GatherItemDecompose` inserts Wander when fact matches spec
- No Wander on first attempt (no fact present)
D-2 from Sprint 19 is now FULLY RESOLVED.

**P1-C: TryParseTruncatedJson gather/build support (DELIVERED)**
In `LlmChatInterpreter.TryParseTruncatedJson`, added:
- `"gather"` case: extracts `item` and `count` fields via regex from partial JSON
- `"build"` case: extracts `blueprint` field
- Intent type: `CreateGoal` when goalName extracted, `Unknown` when item/blueprint missing
- 5 tests in `Sprint21TruncatedJsonGatherTests`

**P1-D: SYSTEM_MESSAGE_PATTERNS tightened (DELIVERED)**
In `MineflayerAdapter/index.js`: `/^Cleared\s+\S+/i` → `/^Cleared\s+(?:\d+|\S+'s|the\s+inventory)/i`
No longer matches "Cleared out the area for you" (player message).
`Sprint20Tests.cs` updated to match new pattern.

---

## Sprint 22 Priorities

### P0: GetStatus health check on drowning/low health
Seat 4 (Human Learning Advocate) noted that Leo drowned unresponsive in Session 1 because no swim/recovery behavior exists. The bot's health events update WorldState but no action is triggered.

Approach: In `ProcessEventsAsync`, handle `HealthEvent` with low-health threshold (e.g., health < 10) by injecting a `GetStatus` + swim recovery action. Requires new `SwimRecovery` tool or using `MoveTo` toward a surface position.

### P0: CraftItem goal false-completion with stale inventory
The `IsInventoryStale` gate was added to `GenericGatherGoal` only. `CraftItemGoal.IsComplete` likely has the same stale-inventory issue. Sprint 22 should apply the same `IsInventoryStale` check to `CraftItemGoal`.

### P1: Council deferred items
From Sprint 21 council:
- **D-1**: Add staleness debug log at `DispatchActionsAsync` IsComplete call site
- **E-1**: AGENTS.md note about fetch-and-patch pattern for verbatim string files
- **B-1**: The 2s MinReplanInterval delay after stall clearance is redundant but harmless — could be removed
- **C-1**: TryParseTruncatedJson: add `navigate` intent extraction

### P1: Quantity propagation in GatherItemDecompose
`get 100 sand` and `get 1 sand` produce identical plans. The count from `GenericGatherGoal.TargetCount` reaches `GatherItemDecompose` via the `parameters` array, but `MineBlock` is emitted once regardless of count. Fix: emit `MineBlock(count=TargetCount - currentInventory[item])`.

### P2: Sprint 19 D-4 (logStructured key collision)
The `logStructured` function in index.js spreads caller data into the envelope. If caller data contains keys `t`, `l`, `c`, or `m`, they silently overwrite envelope fields. Nest caller data under a `"data"` key instead.

---

## Architecture Notes

### Inventory Freshness Gate Flow
```
SetGoal(GatherDirt:5) → _worldState.IsInventoryStale = true
↓
DispatchActionsAsync cycle 1:
  IsComplete → false (stale)
  PlanAsync → [SearchMemory, MineBlock, GetStatus]
  Execute plan: MineBlock(dirt, 5) → StatusEvent(inventory: {dirt:5}) arrives
  WorldStateProjector.ApplyStatus → IsInventoryStale = false
  300ms settle
↓
DispatchActionsAsync cycle 2:
  IsComplete → true (fresh inventory, 5 dirt ≥ 5) → goal completed
```

### Governor Pre-Plan Check Flow (Post Sprint 21)
```
ACTIVE → 3 identical plans with no inventory change → STALLED
  STALLED: IsStalled=true check BEFORE PlanAsync → Task.Delay(10s) → loop
  No PlanAsync calls during STALL
  After 60s timeout OR RecordProgress: IsStalled=false → normal planning resumes
```

---

## Files Changed This Sprint

| File | Change |
|------|--------|
| `Agent.Core/Models/WorldState.cs` | + IsInventoryStale property + SetInventoryStale builder |
| `Agent.Core/WorldStateProjector.cs` | ApplyStatus: SetInventoryStale(false) |
| `Agent.Planning/Goals/GenericGatherGoal.cs` | IsComplete: staleness gate |
| `WebUI.Blazor/AgentBackgroundService.cs` | SetGoal: mark stale; DispatchActionsAsync: pre-plan IsStalled; LogInformation for progress |
| `Agent.Planning/LlmChatInterpreter.cs` | TryParseTruncatedJson: gather/build extraction |
| `MemorySmith.Agent.Tests/Sprint21Tests.cs` | NEW: 20 tests (4 fixtures) |
| `MemorySmith.Agent.Tests/Sprint20Tests.cs` | Updated Cleared pattern + remove stale Assert.Ignore |
| `MineflayerAdapter/index.js` | Cleared pattern tightened |
| `AGENTS.md` | Sprint 20 false claim corrected |
| `Data/Pages/council/sprint21-council-20260618.md` | NEW: council review |

---

## 7 Non-Negotiable Rules (carry forward)

1. TreatWarningsAsErrors = true — all warnings are errors
2. Never call SendEmergencyStop from SetGoal
3. Using directives BEFORE file-scoped namespace in test files (AGENTS.md Rule 3)
4. All timeouts/TTLs/retry counts must be named constants or configurable options
5. Never push C# verbatim string files via agent intermediary (E-1)
6. Each sprint: implement → push → CI green → council review → fix blockers → next sprint
7. GitHub MCP: use mcp__t__ExecuteIntegration with paramsFile for file pushes; agents double-encode content

**Additional (Sprint 21):** When modifying files with verbatim C# string regex patterns (LlmChatInterpreter.cs, WorldStateProjector.cs), always use fetch-from-raw-URL + Python bytes patching. Never write verbatim regex content as Python string literals — the escaping layers (Python → file → C# verbatim → regex) are easy to get wrong.
