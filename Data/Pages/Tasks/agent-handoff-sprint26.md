# Sprint 25 Handoff — Post-Implementation

**Date**: 2026-06-19
**Branch**: `sprint-5-tool-safety`
**Version**: v0.25.0 (bumped from v0.23.0)
**Sprint theme**: Tool Boundary Hardening + Action Lifecycle

---

## What was delivered

### P0-A: FindFlatAreaTool constant unification ✅
- `Agent.Tools/Tools/FindFlatAreaTool.cs`: defaults changed from `radius=20, minFlatArea=9` → `radius=32, minFlatArea=25`
- Matches JS adapter constants `FLAT_AREA_SCAN_RADIUS=32`, `FLAT_AREA_MIN_SIZE=25`
- Safe parsing: `r.GetInt32()` → `r.TryGetInt32(out var rv) ? rv : 32` (graceful scientific notation fallback)
- Description string updated to show new defaults
- 2 new tests in Sprint25Tests: `FindFlatAreaDefaults_MatchJsAdapter`, `FindFlatArea_ScientificNotation_FallsBackToDefault`

### P0-B: StatusTool deduplication ✅
- `Agent.Tools/Tools/StatusTool.cs`: **deleted** from branch
- `Agent.Tools/ToolDispatcher.cs`: new overload `Register(string name, ITool tool)` for alias registration
- `WebUI.Blazor/Program.cs`: StatusTool removed; `d.Register("Status", new GetStatusTool(world))` added as backward-compat alias
- `MemorySmith.Agent.Tests/ToolDispatchTests.cs`: `StatusTool_SendsStatusAction` → `StatusAlias_SendsStatusAction`
- 1 new test: `ToolDispatcher_StatusAlias_DispatchesSameClass` (Sprint25Tests) + updated AllRegisteredTools

### P0-C: ToolDispatcher exception wrapping ✅
- `Agent.Tools/ToolDispatcher.cs`: `tool.ExecuteAsync` wrapped in try/catch; OperationCanceledException re-thrown; all other exceptions become `ToolResult(false, "Tool '{name}' threw: {ex.Message}")`
- `ValidateAgainstSchema`/`CheckType`: integer check changed from `value.GetRawText().Contains('.')` → `!value.TryGetInt32(out _)` — correctly rejects scientific notation (e.g. `1e20`) and floating-point
- 5 new tests: `CallAsync_ToolThrows_ReturnsFailureResult`, `ValidateSchema_ScientificNotation_RejectedAsNonInteger`, `ValidateSchema_DecimalInInteger_Rejected`, `ValidateSchema_ValidInteger_Accepted`, `CallAsync_ToolThrowsOperationCanceled_Propagates`

### P0-D: Action correlation ID infrastructure ✅
**New files:**
- `Agent.Core/Models/PendingAction.cs`: record with CorrelationId (Guid), ToolName, DispatchedAt, State (ActionLifecycle)
- `Agent.Core/Models/ActionLifecycle.cs`: enum {Dispatched, Acknowledged, Completed, Failed, TimedOut}

**AgentBackgroundService changes:**
- `_correlatedActions: ConcurrentDictionary<Guid, PendingAction>` — tracks dispatched actions
- `correlationId` generated per dispatch, injected into `ActionData.Context["correlationId"]`
- Lifecycle transitions on result events: moveComplete→MoveTo:Completed, wanderComplete→Wander:Completed, craftComplete→CraftItem:Completed, smeltComplete→SmeltItem:Completed, flatAreaFound→FindFlatArea:Completed, statusEvent→GetStatus/Status:Completed, wanderFailed→Wander:Failed, blockNotFound→MineBlock:Failed, error→tool:Failed
- Fire-and-forget tools (MoveTo, MineBlock, Wander, CraftItem, SmeltItem, FindFlatArea, GetStatus, Status) stay in Dispatched until result event; sync tools (Chat, SearchMemory, GetPage, CreatePage) transition immediately on CallAsync return
- `SweepTimedOutActions()`: called from idle branch; any Dispatched PendingAction older than 30s → TimedOut + LogWarning
- `StopAsync`: logs all remaining Dispatched PendingActions as abandoned before calling base.StopAsync
- `_correlatedActions.Clear()` on SetGoal, CancelGoal, and TryCompleteCurrentGoalFromWorldUpdate

**MineflayerAdapter/index.js changes:**
- `dispatch({ action, arguments: args = {}, correlationId })` — extracts correlationId from incoming message
- All result `sendEvent` calls include `correlationId` in the payload: moveComplete, blockMined, blockNotFound, mineAborted, wanderComplete, wanderFailed, flatAreaFound (both paths), blockPlaced, craftComplete, smeltComplete

7 new tests: `PendingAction_LifecycleTransition_DispatchedToCompleted`, `PendingAction_Timeout_MarkedTimedOut`, `PendingAction_ConcurrentDispatch_IndependentTracking`, `PendingAction_StaleDispatch_IdentifiedByTimestamp`, `PendingAction_DuplicateTransition_HandledGracefully`, `ActionLifecycle_AllStatesExist`, `Dispatcher_WithJournal_LogsSuccessAndFailure`

### P1-A: WorldModel defensive copy ✅
- `Agent.Core/Models/WorldModel.cs`: constructor creates **separate** `new Dictionary<string, int>()` for `_observed.Inventory` and `_belief.Inventory` (eliminates shared mutable state)
- `Observe()`: `new Dictionary<string, int>(observation.Inventory)` deep-copies instead of aliasing
- **Also fixed**: `GetIntArg` JsonElement branch changed from `je.GetInt32()` → `je.TryGetInt32(out var ji) ? ji : fallback` for consistency with P0-A/C safe parsing
- 2 new tests: `WorldModel_Constructor_SeparateInstances`, `WorldModel_Observe_DoesNotAliasInventory`

---

## What was NOT implemented (deferred)

| Item | Priority | Reason | Sprint 26 |
|---|---|---|---|
| P1-B: TryInterruptOnDamage integration test | P1 | Scope — test infrastructure complexity | ✅ Carry |
| P1-C: GatherGoalDecomposer TargetCount | P1 | File not found on branch, create task | ✅ Carry |
| P1-D: E2E gather integration test | P1 | Depends on P1-C + fake adapter wiring | ✅ Carry |
| P2-A: Startup constant log | P2 | Time | Optional |
| P2-B: ITimeProvider abstraction | P2 | Time | Optional |
| P2-C: Move event throttling (index.js) | P2 | Time | Optional |
| P2-D: IWorldObservationGateway note | P2 | Time | Optional |

---

## Test count
- Baseline (Sprint 23): ~200 tests
- Sprint 25 adds: **17 new tests** in `Sprint25Tests.cs`
- Updated: `ToolDispatchTests.cs` (StatusTool → alias; AllRegisteredTools updated)
- Expected total: **220+ tests passing**

---

## Files changed this sprint

| File | Change type | Commits |
|---|---|---|
| `Agent.Core/Models/PendingAction.cs` | NEW | edd2a38 |
| `Agent.Core/Models/ActionLifecycle.cs` | NEW | cd50669 |
| `Agent.Core/Models/WorldModel.cs` | Modified (P1-A) | 0f98a87 |
| `Agent.Tools/ToolDispatcher.cs` | Modified (P0-C + P0-B alias overload) | 7298d10 |
| `Agent.Tools/Tools/FindFlatAreaTool.cs` | Modified (P0-A) | dfa27b1 |
| `Agent.Tools/Tools/StatusTool.cs` | **DELETED** (P0-B) | f782af4 |
| `WebUI.Blazor/Program.cs` | Modified (P0-B alias + v0.25.0) | pending |
| `WebUI.Blazor/AgentBackgroundService.cs` | Modified (P0-D correlation) | pending |
| `MineflayerAdapter/index.js` | Modified (P0-D correlationId echo) | pending |
| `MemorySmith.Agent.Tests/ToolDispatchTests.cs` | Modified (P0-B alias test) | pending |
| `MemorySmith.Agent.Tests/Sprint25Tests.cs` | NEW (17 tests) | pending |

---

## Sprint 26 priorities

### P0 (blocking — address before further feature work)
- **P0-A**: TryInterruptOnDamage integration test — was Sprint 24 P0-D, Sprint 25 P1-B. Integration test for health-delta → DamageTakenEvent → ActionQueue.ClearAndEnqueue → emergency stop chain. Verify cooldown suppression.
- **P0-B**: GatherGoalDecomposer TargetCount pass-through — file not found on branch. Determine if it exists under a different name or needs creation. Ensure `IItemSpecGoal.TargetCount` flows through to MineBlock parameters.

### P1 (should-ship)
- **P1-A**: End-to-end gather integration test (chat→goal→plan→dispatch→world event→IsComplete). Requires fake IWorldAdapter with event injection.
- **P1-B**: Journal semantics decision — bounded log (current) vs event store. Council flagged in Sprint 6 (D-3), carry-forward from Sprint 24/25.
- **P1-C**: Planner routing consolidation — delete HtnPlanner hardcoded decomposer branches; route all through DecomposerRegistry. Requires creating a CraftItemGoalDecomposer first.

### P2 (nice-to-have)
- World model full immutability (copy-on-write at projector boundary) — P1-A fixes the aliasing seam; full immutability is the Sprint 26 follow-through
- Startup constant log (top 6 tunable constants at LogInformation)
- ITimeProvider abstraction (SystemTimeProvider + FakeTimeProvider for governor/projector tests)
- Move event throttling (MOVE_EMIT_THROTTLE_MS=250 in index.js)
- IWorldObservationGateway note in architecture.md

### Deferred from Sprint 25 externals
- Adapter reconnection strategy (MinecraftAdapter auto-reconnect on WebSocket drop)
- Structured message classification (replace regex SYSTEM_MESSAGE_PATTERNS with typed classifier)
- SearchMemoryTool/GetPageTool return ToolResult failures (not throw) — P0-C wrapper catches them now, but tools should return failures directly

---

## Key invariants to preserve

1. **TreatWarningsAsErrors=true** — no new warnings introduced
2. **Rule E-1** — C# verbatim-string files patched via paramsFile, never agent-intermediary
3. **_correlatedActions** — always cleared on SetGoal/CancelGoal/goal-complete to prevent cross-goal correlation leakage
4. **OperationCanceledException** — must always propagate through ToolDispatcher (never caught)
5. **StatusTool is gone** — do not re-introduce; GetStatusTool is the single implementation; "Status" is an alias in DI only
