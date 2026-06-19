# Sprint 27 Handoff — Updated Post-Audit-Verification

**Date**: 2026-06-19  
**Branch**: `sprint-5-tool-safety`  
**Previous handoff**: agent-handoff-sprint27.md (Sprint 26 original)  
**This update**: Independent re-verification of all audit findings + council review  

---

## IV. Sprint 27 Plan

### P0 (Blocking — must ship)

**P0-A: AgentBackgroundServiceTestHelper + CI green**
- Create `MemorySmith.Agent.Tests/AgentBackgroundServiceTestHelper.cs`
- `BuildMinimal(MockWorldAdapter adapter, IAgentJournal journal)` → returns ready-to-start `AgentBackgroundService`
- Derive constructor params from ACTUAL current `AgentBackgroundService` constructor (inspect the file, don't use handoff template blindly)
- Verify all 8 Sprint26Tests pass locally
- Push and confirm CI green
- This CLOSES Sprint 26's BLK-1

**P0-B: Version string unification to v0.27.0**
- `WebUI.Blazor/Program.cs` — update `/api/about` endpoint return to `"0.27.0"`
- `WebUI.Blazor/Program.cs` — update file header comment to `v0.27.0`
- `README.md` — update version header to `v0.27.0`
- This CLOSES Sprint 26's BLK-2 (COR-1)

**P0-C: ITimeProvider abstraction**
- Create `Agent.Core/Interfaces/ITimeProvider.cs` with `DateTimeOffset UtcNow { get; }`
- Create `Agent.Core/SystemTimeProvider.cs` (production) and `MemorySmith.Agent.Tests/FakeTimeProvider.cs` (test)
- Inject into `AgentBackgroundService` (replaces `DateTimeOffset.UtcNow` calls for damage interrupt cooldown + governor timing)
- Update Sprint26Tests P0-A tests to use `FakeTimeProvider` instead of `Task.Delay(150ms)`
- Unblocks deterministic timing tests

**P0-D: Planner routing consolidation**
- Create `Agent.Planning/Decomposition/CraftItemGoalDecomposer.cs`
- Register in DecomposerRegistry via DI
- Remove HtnPlanner hardcoded `CraftItemGoal` branch (line 62-65)
- Remove HtnPlanner hardcoded `BuildGoal` branch ONLY IF `BuildGoalDecomposer` already exists and is registered — verify first
- Route ALL decomposition through `DecomposerRegistry` → HtnPlanner becomes pure HTN fallback with no type-switch
- Remove redundant `GenericGatherGoal gg` arm from GatherGoalDecomposer (cleanup, since IItemSpecGoal arm now covers it)
- Tests: at least 3 new tests for CraftItemGoalDecomposer

### P1 (Should-ship)

**P1-A: E2E gather integration test**
- `chat→goal→plan→dispatch→world event→IsComplete` chain with MockWorldAdapter
- Requires P0-A (helper) and P0-C (FakeTimeProvider) to be done first

**P1-B: Journal semantics decision**
- Record in `Data/Pages/Guides/architecture.md`: "AgentJournal is a bounded diagnostic buffer, not a durable event store"
- Formally close Deep Code Audit Finding 4

### P2 (Nice-to-have)

- Startup constant log (DamageInterruptCooldownSeconds, HealthCriticalThreshold, etc.)
- Move event throttling (MOVE_EMIT_THROTTLE_MS=250)
- IWorldObservationGateway design note in architecture.md
- DEF-9: Add XML doc comment on Register(string,ITool) collision semantics

---

## V. Key Invariants to Preserve

1. **TreatWarningsAsErrors=true** — no new warnings
2. **Rule E-1** — C# verbatim-string files patched via paramsFile only
3. **StatusTool is gone** — do not re-introduce; GetStatusTool registered as both "GetStatus" and "Status"
4. **OperationCanceledException** — always propagates through ToolDispatcher (never caught)
5. **IItemSpecGoal.TargetCount DIM** — default is 1, not 0; 0 would mean "no items requested"
6. **DamageInterruptThresholdHp=0** — means never interrupt (combat goal convention); null means use system default (6 HP)
7. **PlannerStrategy enum** — correct name is PlannerStrategy, not PlannerId (correct references in any new docs)
8. **BlueprintRepository** — uses HTTP gateway via MemorySmith REST API, NOT local filesystem

---

## VI. Files Expected to Change in Sprint 27

| File | Change | Priority |
|---|---|---|
| `MemorySmith.Agent.Tests/AgentBackgroundServiceTestHelper.cs` | NEW | P0-A |
| `WebUI.Blazor/Program.cs` | Version bump to v0.27.0 | P0-B |
| `README.md` | Version bump to v0.27.0 | P0-B |
| `Agent.Core/Interfaces/ITimeProvider.cs` | NEW | P0-C |
| `Agent.Core/SystemTimeProvider.cs` | NEW | P0-C |
| `MemorySmith.Agent.Tests/FakeTimeProvider.cs` | NEW | P0-C |
| `Agent.Core/AgentBackgroundService.cs` | ITimeProvider injection | P0-C |
| `Agent.Planning/Decomposition/CraftItemGoalDecomposer.cs` | NEW | P0-D |
| `Agent.Planning/HtnPlanner.cs` | Remove hardcoded branches | P0-D |
| `Agent.Planning/Decomposition/GatherGoalDecomposer.cs` | Remove redundant GenericGatherGoal arm | P0-D |
| `MemorySmith.Agent.Tests/Sprint26Tests.cs` | Update P0-A tests to use FakeTimeProvider | P0-C |
| `MemorySmith.Agent.Tests/Sprint27Tests.cs` | NEW — CraftItemGoalDecomposer + routing tests | P0-D |
| `Data/Pages/Guides/architecture.md` | Journal semantics decision | P1-B |
| `Data/Pages/council/sprint27-council-20260619.md` | NEW — this review | Doc |
| `Data/Pages/Tasks/agent-handoff-sprint27-updated.md` | NEW — updated handoff | Doc |

---

## VII. Council Confidence Summary

| Seat | Role | Confidence | Dissent |
|---|---|---|---|
| 1 | Source-Grounded Archivist | 93% | COR-1 is process gap, not code gap |
| 2 | Data Model Architect | 89% | None |
| 3 | Retrieval Specialist | 87% | None |
| 4 | Human Learning Advocate | 84% | Sprint 26 should not have been closed without CI green |
| 5 | Skeptical Reviewer | 81% | TestHelper must derive from actual constructor, not template |
| 6 | Synthesizer | 86% | None |

**Average confidence**: 87%  
**Verdict**: Sprint 26 NOT APPROVED (2 blocking findings). Sprint 27 plan APPROVED with P0 scope addressing both blockers.
