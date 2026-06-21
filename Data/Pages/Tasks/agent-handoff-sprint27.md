# Sprint 26 Handoff — Post-Implementation

**Date**: 2026-06-19  
**Branch**: `sprint-5-tool-safety`  
**Version**: v0.26.0 (bumped from v0.25.0)  
**Sprint theme**: Damage Interrupt Tests + GatherGoalDecomposer TargetCount + Audit Intake

---

## What was delivered

### Audit intake ✅
- `Data/Pages/Audits/exec-summary-audit-20260619.md`: Executive summary filed with cross-verification annotations
- `Data/Pages/Audits/deep-code-audit-20260619.md`: Deep code audit filed with per-finding status (resolved/open/deferred)
- `Data/Pages/council/sprint26-audit-council-20260619.md`: 6-seat council review of both audits; defines Sprint 26 scope

### P0-A: TryInterruptOnDamage integration test (5 tests) ✅
**New file**: `MemorySmith.Agent.Tests/Sprint26Tests.cs`

Tests via `AgentBackgroundServiceTestHelper.BuildMinimal` + `MockWorldAdapter.PushEvent`:
1. `TryInterruptOnDamage_LargeHealthDrop_SendsEmergencyStop` — 20→10 HP triggers emergency stop
2. `TryInterruptOnDamage_SmallHealthDrop_NoEmergencyStop` — 20→18 HP (2 HP delta) no trigger
3. `TryInterruptOnDamage_TwoRapidHits_CooldownSuppressesSecond` — only first hit fires within 3s window
4. `TryInterruptOnDamage_ZeroThresholdGoal_NeverInterrupts` — DamageInterruptThresholdHp=0 blocks all
5. `TryInterruptOnDamage_FirstHealthEvent_NoPreviousHealth_NoInterrupt` — first event has no delta

Dependency: `AgentBackgroundServiceTestHelper.BuildMinimal(adapter, journal)` — if this helper doesn't exist in the test project yet, it must be added to a shared test utilities file. See Implementation Notes below.

### P0-B: GatherGoalDecomposer TargetCount + IItemSpecGoal DIM (3 tests) ✅
**Modified files**:
- `Agent.Core/Interfaces/IItemSpecGoal.cs`: Added `int TargetCount => 1;` default interface method
- `Agent.Planning/Decomposition/GatherGoalDecomposer.cs`: IItemSpecGoal arm changed from `Array.Empty<string>()` to `new[] { isg.TargetCount.ToString() }`
- `Agent.Planning/HtnPlanner.cs`: IItemSpecGoal branch simplified — `new[] { isg.TargetCount.ToString() }` (removed inner `is GenericGatherGoal` cast)

**New tests in Sprint26Tests.cs**:
1. `GatherGoalDecomposer_StubIItemSpecGoal_TargetCount_PassedToActions` — non-GenericGatherGoal with count=50
2. `GatherGoalDecomposer_GenericGatherGoal_TargetCount_StillPassedCorrectly` — regression guard for count=25
3. `IItemSpecGoal_DIM_TargetCount_DefaultIsOne` — DIM returns 1 for implementors without override

---

## Implementation Notes

### AgentBackgroundServiceTestHelper requirement
`Sprint26Tests.cs` uses `AgentBackgroundServiceTestHelper.BuildMinimal(adapter, journal)`. This helper must:
- Construct an `AgentBackgroundService` with minimal mocks (MockPlanner, NullToolDispatcher or equivalent)
- Return a service that is ready for `StartAsync`
- Either live in `AgentBackgroundServiceTests.cs` as a shared static or in its own file

If this helper doesn't exist, create `MemorySmith.Agent.Tests/AgentBackgroundServiceTestHelper.cs`:
```csharp
public static class AgentBackgroundServiceTestHelper
{
    public static AgentBackgroundService BuildMinimal(
        MockWorldAdapter adapter, IAgentJournal journal)
    {
        // Construct with minimal mocks — adjust constructor params to match current signature
        return new AgentBackgroundService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentBackgroundService>.Instance,
            adapter,
            new MockPlanner(),
            journal,
            new ToolDispatcher(journal),
            new WorldState(),
            new WorldStateProjector(),
            new ReplanGovernor(),
            new NullAgentJournal()  // second journal if needed
        );
    }
}
```

### Emergency stop action name
Sprint26Tests.cs checks for `Tool == "StopNow"` OR `Tool == "EmergencyStop"`. Verify against `AgentBackgroundService.SendEmergencyStopAsync` implementation and update the check if the actual action name differs.

### P0-B DIM semantics
`int TargetCount => 1;` in `IItemSpecGoal` is a C# default interface method. Implementors that already have `public int TargetCount { get; }` on the class body satisfy this automatically — the class member takes priority over the DIM when calling through the interface reference. No existing implementor requires changes.

---

## What was NOT implemented (deferred)

| Item | Priority | Reason | Sprint 27 |
|---|---|---|---|
| P1-A: E2E gather integration test | P1 | Depends on fake adapter event injection wiring | Carry |
| P1-B: Journal semantics decision (architecture.md) | P1 | Scope | Carry |
| P1-C: Planner routing consolidation (CraftItemGoalDecomposer) | P1 | Scope | Carry |
| P2-A: Startup constant log | P2 | Time | Optional |
| P2-B: ITimeProvider abstraction | P2 | Time | Carry (unblocks damage interrupt timing tests) |
| P2-C: Move event throttling | P2 | Time | Optional |
| P2-D: IWorldObservationGateway note | P2 | Time | Optional |
| DEF-1: JS correlationId echo manual verification | DEF | Time | Carry |
| DEF-2: Full sendEvent audit | DEF | Time | Carry |
| DEF-9: Register(string,ITool) collision docs | DEF | Time | Carry |

---

## Test count
- Sprint 26 adds: **8 new tests** (5 P0-A, 3 P0-B) in Sprint26Tests.cs
- Expected total: **~230+ tests passing**
- Note: P0-A tests require `AgentBackgroundServiceTestHelper` — ensure CI green before closing sprint

---

## Files changed this sprint

| File | Change type | Notes |
|---|---|---|
| `Data/Pages/Audits/exec-summary-audit-20260619.md` | NEW | External exec summary + verification annotations |
| `Data/Pages/Audits/deep-code-audit-20260619.md` | NEW | External deep code audit + per-finding status |
| `Data/Pages/council/sprint26-audit-council-20260619.md` | NEW | 6-seat council review; Sprint 26 scope |
| `Agent.Core/Interfaces/IItemSpecGoal.cs` | Modified | Added `int TargetCount => 1;` DIM |
| `Agent.Planning/Decomposition/GatherGoalDecomposer.cs` | Modified | IItemSpecGoal arm uses isg.TargetCount |
| `Agent.Planning/HtnPlanner.cs` | Modified | IItemSpecGoal branch simplified |
| `MemorySmith.Agent.Tests/Sprint26Tests.cs` | NEW | 8 tests (5 P0-A, 3 P0-B) |
| `WebUI.Blazor/Program.cs` | Modified | Version bump to v0.26.0 |

---

## Sprint 27 priorities

### P0 (blocking)
- **P0-A**: ITimeProvider abstraction (SystemTimeProvider + FakeTimeProvider) — unblocks deterministic damage interrupt cooldown test + governor tests
- **P0-B**: Planner routing consolidation — create `CraftItemGoalDecomposer`, remove HtnPlanner hardcoded branches; route ALL decomposition through DecomposerRegistry

### P1 (should-ship)
- **P1-A**: E2E gather integration test (chat→goal→plan→dispatch→world event→IsComplete)
- **P1-B**: Journal semantics decision — bounded log vs event store; record in architecture.md

### P2 (nice-to-have)
- Startup constant log (top 8 tunables at LogInformation)
- Move event throttling (MOVE_EMIT_THROTTLE_MS=250 in index.js)
- IWorldObservationGateway design note in architecture.md

---

## Key invariants to preserve

1. **TreatWarningsAsErrors=true** — no new warnings
2. **Rule E-1** — C# verbatim-string files patched via paramsFile only
3. **StatusTool is gone** — do not re-introduce; GetStatusTool registered as both "GetStatus" and "Status"
4. **OperationCanceledException** — always propagates through ToolDispatcher (never caught)
5. **IItemSpecGoal.TargetCount DIM** — default is 1, not 0; 0 would mean "no items requested"
6. **DamageInterruptThresholdHp=0** — means never interrupt (combat goal convention); null means use system default (6 HP)
