# Agent Handoff — Sprint 24

**Date**: 2026-06-19  
**Branch**: `sprint-5-tool-safety` (PR #1)  
**Base commit**: `2fe07082a5` (Sprint 23 complete — damage interrupt, World KB routing, v0.23.0)  
**Theme**: Action Lifecycle Fidelity + Constant Unification  
**Council source**: `Data/Pages/council/reliability-audit-council-20260619.md` (2 blocking, 5 deferred)

---

## Context

Sprint 23 delivered real-time damage interrupt with context preservation, World KB routing for search/create tools, and health-check rate-limit guards. The council review is APPROVED (86% avg, 0 blockers post-council).

An external reliability audit was submitted and reviewed by a 5-chair council (2026-06-19). The council confirmed two source-grounded blocking findings (B-1: FindFlatAreaTool stale defaults; B-2: StatusTool/GetStatusTool duplicate classes) and one primary deferred architectural item elevated to Sprint 24 P0 (D-1: action correlation IDs).

**Sprint 24 theme**: stop treating dispatch success as the end of the story. Add an explicit action lifecycle that connects tool dispatch to world event arrival. Fix constant mismatches that actively mislead LLM calls and developer tooling.

---

## Delivered in Sprint 23 (for reference)

- `DamageTakenEvent` record synthesized from consecutive HealthEvent deltas
- `IGoal.DamageInterruptThresholdHp` per-goal override (default null = 6HP system default; 0 = never interrupt)
- `ActionQueue.ClearAndEnqueue` lock-protected atomic clear+enqueue
- `AgentBackgroundService`: `_previousHealth`, `_lastDamageInterruptAt`, `_lastHealthStatusEnqueuedAt` fields; `DamageInterruptCooldownSeconds=3`, `HealthCheckCooldownSeconds=2`; `TryInterruptOnDamage` → `SendEmergencyStop + ClearAndEnqueue`
- World KB routing: `SearchMemoryTool` and `CreatePageTool` receive world-keyed `IMemoryGateway`; `GetPageTool` retains agent KB
- Tool descriptions updated with routing semantics for LLM clarity
- `RestMemoryGatewayOptions.WorldKbUrl` default changed to `null`; startup `LogWarning` when null/empty
- `_lastHealthStatusEnqueuedAt` gates passive GetStatus to `HealthCheckCooldownSeconds=2`
- v0.23.0 — 15 new tests in 4 fixtures

Sprint 23 deferred (carried to Sprint 24):
- D-6: Integration test for `TryInterruptOnDamage`
- Sprint 23 priority: `GatherGoalDecomposer` TargetCount fix
- D-8: TimeProvider abstraction (`ITimeProvider`)
- D-5: `IWorldObservationGateway` note in architecture

---

## Sprint 24 Priorities

### P0 — Blocking fixes (from council B-1, B-2)

#### P0-A: FindFlatAreaTool default sync (B-1)

**Problem**: `FindFlatAreaTool.cs` defaults to `radius=20, minFlatArea=9` in both C# fallback code and InputSchema description. The JS adapter uses `FLAT_AREA_DEFAULT_RADIUS=32` and `FLAT_AREA_MIN_SIZE=25` (AGENTS.md, Sprint 19). LLM-driven invocations without explicit args use the C# defaults and get suboptimal first-attempt search coverage.

**Fix**:
1. `Agent.Tools/Tools/FindFlatAreaTool.cs`:
   - Change fallback `radius` default: `20` → `32`
   - Change fallback `minFlatArea` default: `9` → `25`
   - Update description string: "default 32" and "default 25 (i.e. 5×5 area)"
   - Update InputSchema description fields to match
2. Add test `FindFlatAreaDefaults_MatchJsAdapter`: `ExecuteAsync({})` dispatches `radius=32, minFlatArea=25`

**Note**: `FindFlatAreaTool.cs` contains raw string literals (`"""`). Use Rule E-1 (paramsFile) when committing.

---

#### P0-B: StatusTool/GetStatusTool deduplication (B-2)

**Problem**: `StatusTool.cs` (Name="Status") and `GetStatusTool.cs` (Name="GetStatus") are two separate classes with identical bodies. Any change to one must be manually mirrored. `GetStatusTool` documents itself as a "compatibility alias" but implements the full class.

**Fix**:
1. Delete `Agent.Tools/Tools/StatusTool.cs`
2. Keep `GetStatusTool.cs` unchanged (`Name = "GetStatus"`)
3. In `Program.cs` DI registration: if any plan still dispatches "Status", register `GetStatusTool` under both names via a `ToolDispatcher` alias map, not a second class
4. Update all tests that reference `"Status"` tool name to use `"GetStatus"`
5. Add test `ToolDispatcher_StatusDispatchedByOneClass`: exactly one class instance sends `ActionProtocol.Status`

---

### P0 — Architectural work (from council D-1, elevated)

#### P0-C: Action Correlation IDs

**Problem**: `ToolDispatcher.CallAsync` returns `ToolResult(bool Success, string Message)` upon dispatch. There is no mechanism connecting this return to a subsequent world event. `blockMined`, `craftComplete`, `smeltComplete`, `flatAreaFound` arrive via a separate event stream with no shared token. The system treats dispatch success as world success.

**Design** (minimal — no interface changes required):

**New file `Agent.Core/PendingAction.cs`**:
```csharp
public sealed record PendingAction(
    Guid CorrelationId,
    string ToolName,
    string? GoalId,
    DateTimeOffset DispatchedAt)
{
    public ActionLifecycle State { get; set; } = ActionLifecycle.Dispatched;
}

public enum ActionLifecycle
{
    Dispatched,
    Acknowledged,
    Completed,
    Failed,
    TimedOut
}
```

**ToolDispatcher.cs changes**:
- Generate `Guid corrId = Guid.NewGuid()` in `CallAsync` (or pass via ActionData)
- Inject into `action.Context["correlationId"] = corrId.ToString()`
- Pass corrId back via `ToolResult` metadata or a new `PendingActionId` property

**AgentBackgroundService.cs changes**:
- `private readonly ConcurrentDictionary<Guid, PendingAction> _pending = new()`
- On dispatch: `_pending[corrId] = new PendingAction(corrId, toolName, goalId, DateTimeOffset.UtcNow)`
- On world event: if `event.CorrelationId` parses to a Guid and exists in `_pending`, update State → Completed or Failed, remove from dictionary
- On 30s timeout: State = TimedOut, `LogWarning("Action {CorrelationId} ({ToolName}) timed out after {Elapsed}ms")`, remove
- In `ShutdownAsync`: log all remaining `_pending` entries as TimedOut, `_pending.Clear()`
- LogDebug on every state transition for traceability

**index.js changes**:
- In each result event emit (`bot.on('blockMined')`, `craftComplete`, `smeltComplete`, `flatAreaFound`, `moveDone`, `blockPlaced`, etc.): include `correlationId: args?.correlationId ?? null` in the emitted JSON payload
- This is a ~1 line addition per event handler

**Tests** (minimum 4 new):
- `PendingAction_CompletedOnMatchingEvent`
- `PendingAction_TimedOut_LogWarning`
- `PendingAction_UnmatchedEvent_NoSideEffects`
- `PendingAction_ShutdownClearsAll`

---

#### P0-D: Integration test for TryInterruptOnDamage (Sprint 23 D-6 carry-forward)

Write a test that exercises `TryInterruptOnDamage` end-to-end through `ProcessEventsAsync`:
1. Set up `AgentBackgroundService` with a fake world adapter
2. Set an active goal with threshold HP > 0
3. Inject two consecutive `HealthEvent` values that compute a delta below threshold
4. Assert `SendEmergencyStop` was called on the adapter
5. Assert `ActionQueue` was cleared and re-enqueued with GetStatus

---

### P1 — Should-ship

#### P1-A: GatherGoalDecomposer TargetCount fix (Sprint 23 carry-forward)

`GatherGoalDecomposer` does not pass `TargetCount` from the `IItemSpecGoal` to the decomposed plan's parameters. When a user asks for "get 100 sand", the plan is constructed with `count=10` (default). Mirror the Sprint 22 fix applied to `HtnPlanner` for `IItemSpecGoal`.

**Fix**: In `GatherGoalDecomposer.Decompose`, cast goal to `IItemSpecGoal` and pass `parameters[0] = goal.TargetCount.ToString()` to the `MineBlock` action.

**Test**: `GatherDecomposer_PassesTargetCount_ToMineAction` — goal with `TargetCount=100` produces `MineBlock` with `count=100`.

---

#### P1-B: End-to-end gather integration test (council D-4, start)

One deterministic integration test that exercises the full chain:
1. Create `AgentBackgroundService` with fake `IWorldAdapter` and fake `IAgentJournal`
2. Inject `chatMessage` event: "gather 5 iron_ore"
3. Assert `AgentBackgroundService` creates `GenericGatherGoal` with `ItemId=iron_ore, TargetCount=5`
4. Assert `MineBlockTool` is dispatched with `blockType=iron_ore`
5. Inject 5× `blockMined` world events (with matching correlationId if D-1 is complete)
6. Assert `GenericGatherGoal.IsComplete()` returns true

This test catches regressions across the entire chat→goal→plan→dispatch→event→projection pipeline.

---

#### P1-C: Startup constant log

In `AgentBackgroundService.StartAsync` (or `Program.cs` after DI build), emit `LogInformation` listing the top 6 tunable constants so future mismatches surface in logs automatically:

```
[AgentBackgroundService] Config: LlmTimeout={LlmTimeoutSeconds}s, PerActionTimeout={PerActionTimeoutSeconds}s, FlatAreaRadius={FlatAreaDefaultRadius}, FlatAreaMinSize={FlatAreaMinSize}, DamageInterruptThreshold={DamageInterruptThresholdHp}HP, ReplanGovernorThreshold={GovernorStallThreshold}
```

This requires passing `FindFlatAreaTool` constants (currently hardcoded) through an options class or `AgentConstants`. Sprint 24 P0-A introduces the correct default values; this P1 surfaces them at runtime.

---

### P2 — Nice-to-have

#### P2-A: TimeProvider abstraction (Sprint 23 D-8)

Replace all `DateTimeOffset.UtcNow` and `DateTime.Now` in production code paths with `ITimeProvider.UtcNow`. Provide `SystemTimeProvider` and `FakeTimeProvider` for tests. This is prerequisite for deterministic time-sensitive tests (governor timeouts, health cooldown, correlation ID timeout).

```csharp
public interface ITimeProvider { DateTimeOffset UtcNow { get; } }
public sealed class SystemTimeProvider : ITimeProvider { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
public sealed class FakeTimeProvider : ITimeProvider { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow; }
```

Register `SystemTimeProvider` as singleton in `Program.cs`. Inject where needed.

---

#### P2-B: Move event throttling

In `MineflayerAdapter/index.js`, add:
```js
const MOVE_EMIT_THROTTLE_MS = 250; // emit position update at most every 250ms
let _lastMoveEmit = 0;
bot.on('move', () => {
    const now = Date.now();
    if (now - _lastMoveEmit < MOVE_EMIT_THROTTLE_MS) return;
    _lastMoveEmit = now;
    // ... existing position emit
});
```

Add `MOVE_EMIT_THROTTLE_MS` to the tunable constants block at top of file.

---

#### P2-C: IWorldObservationGateway note (Sprint 23 D-5)

Add a design note in `Data/Pages/architecture.md` on the future `IWorldObservationGateway` interface. Not an implementation; an ADR note capturing the intent: `IWorldAdapter` currently handles both command dispatch and event reception. A future split would separate observation (read-only event stream) from command dispatch (write), enabling multiple world adapters to share one observation stream.

---

## Files to change

| File | Change | Note |
|------|--------|------|
| `Agent.Tools/Tools/FindFlatAreaTool.cs` | P0-A: fix defaults + description | Rule E-1: paramsFile |
| `Agent.Tools/Tools/StatusTool.cs` | P0-B: delete | Verify no direct typeof() references before deleting |
| `Agent.Core/PendingAction.cs` | P0-C: new file | Plain C# record, no verbatim strings |
| `Agent.Tools/ToolDispatcher.cs` | P0-C: inject correlationId | Check for verbatim strings before patching |
| `Agent.Core/AgentBackgroundService.cs` | P0-C, P0-D: pending dict, shutdown cleanup | Large file — check for verbatim strings |
| `MineflayerAdapter/index.js` | P0-C: echo correlationId; P2-B: throttle | JS — no verbatim string issues |
| `Program.cs` | P0-B: alias if needed, P2-A: register ITimeProvider | |
| `Agent.Planning/Decomposers/GatherGoalDecomposer.cs` | P1-A: TargetCount pass-through | |
| `MemorySmith.Agent.Tests/…` | 4+ new tests for P0-C, P0-D, P1-A, P1-B | |

---

## Known constraints

- `TreatWarningsAsErrors = true` (Directory.Build.props) — fix all warnings immediately.
- `AgentBackgroundService.cs` is large and likely contains verbatim strings — use Rule E-1 (paramsFile pattern) when patching.
- `FindFlatAreaTool.cs` uses raw string literals (`"""`) — use Rule E-1.
- `ToolDispatcher.cs` may contain verbatim strings (JSON schema strings) — verify before patching.
- JS adapter does not honor HTTPS_PROXY — curl-based node_modules install required in sandbox.
- After any C# change: `dotnet build` → `dotnet test` → push → CI green before council review.

---

## Sprint 25 preview (from deferred D-2, D-3)

- **D-2**: LLM decoupling — `Channel<WorldEvent>` + dedicated consumer Task in `AgentBackgroundService`. Removes LLM latency from world-event hot path.
- **D-3**: Adapter reconnection — reconnect loop in `MinecraftAdapter` on WebSocket close, `botReconnecting`/`botReconnected` synthetic events, `MaxReconnectAttempts` configurable option.

---

## Council workflow for Sprint 24

```
implement → dotnet build (all green) → dotnet test (all green) → push → CI green →
6-seat council review → fix blockers → CI green → next sprint
```

Write council doc to `Data/Pages/council/sprint24-council-<date>.md`.  
Write pre-council doc to `Data/Pages/council/sprint24-pre-council-<date>.md`.
