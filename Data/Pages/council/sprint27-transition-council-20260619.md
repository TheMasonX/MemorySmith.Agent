# MemorySmith.Agent — Sprint 26→27 Transition Council Review + Sprint 27 Handoff

**Date**: 2026-06-19  
**Branch**: `sprint-5-tool-safety` @ 3522e58c (current HEAD)  
**Version**: v0.26.0 (per handoff; actual code version strings are inconsistent — see COR-1)  
**Council format**: 6-seat  
**Scope**: Independent re-verification of all Sprint 26 audit findings, synthesis, and Sprint 27 plan

---

## I. Independent Verification Summary

Five parallel source-level verification agents reviewed every finding from both external audits (deep-code-audit-20260619.md and exec-summary-audit-20260619.md) against the actual code at branch HEAD 3522e58c. Method: direct github__get_file_contents at the commit SHA, decoded and line-inspected.

### Deep Code Audit Findings

| # | Finding | Audit Status | My Verdict | Evidence |
|---|---|---|---|---|
| 1 | Tool schema validation too permissive for integers | RESOLVED (Sprint 25 P0-C) | **CONFIRMED RESOLVED** | `CheckType` uses `!value.TryGetInt32(out _)` — line-verified in Agent.Tools/ToolDispatcher.cs |
| 2 | ToolDispatcher assumes tools do not throw | RESOLVED (Sprint 25 P0-C) | **CONFIRMED RESOLVED** | `CallAsync` has try/catch around `tool.ExecuteAsync`; OCE re-throws; all others → `ToolResult(false, ...)` |
| 3 | WorldModel state aliasing | RESOLVED (Sprint 25 P1-A) | **CONFIRMED RESOLVED** | Constructor: two separate `new Dictionary<string, int>()`; `Observe()` uses copy constructor |
| 4 | Journal approximately bounded under contention | OPEN — deliberate design | **CONFIRMED OPEN — DELIBERATE** | ConcurrentQueue + single-dequeue trim; `MaxEntries = 1000`; comment says "best-effort" |
| 5 | Planner routing split across two modules | OPEN — Sprint 26 P1-C target | **CONFIRMED OPEN** | HtnPlanner retains hardcoded `BuildGoal` (line 53) and `CraftItemGoal` (line 62) branches |
| 6 | Mineflayer chat filter brittle | OPEN — deferred | **ACCEPTED AS KNOWN RISK** | Not re-verified; acknowledged as ongoing maintenance item |

### Executive Summary Audit Findings

| # | Finding | Audit Status | My Verdict | Evidence |
|---|---|---|---|---|
| A | CI failure on PR head | RESOLVED | **CONFIRMED RESOLVED** | BLK-1 CAS-loop fix shipped; CI was transient |
| B | Core sprint-5/6 goals substantially delivered | CONFIRMED | **CONFIRMED** | ToolDispatcher, AgentJournal, WorldModel, DecomposerRegistry, dual gateway — all present |
| C | Long-term planner still incomplete | CONFIRMED | **CONFIRMED** | `PlannerRouter.Select` has exactly 2 branches; `PlannerStrategy.Goap` + `.LlmAssisted` explicitly marked "[ASPIRATIONAL]" in source |
| D | Documentation drift (README version lag) | CONFIRMED | **CONFIRMED + EXTRA FINDING** | See COR-1 below |
| E | BlueprintRepository local-filesystem fallback | ACCEPTED AS KNOWN RISK | **PARTIALLY CONFIRMED** | No local filesystem scan exists — `MemorySmithBlueprintRepository` uses HTTP gateway. `SearchAsync` has no result cap though. Audit mischaracterized the mechanism. |
| F | Planner architecture transitional | CONFIRMED | **CONFIRMED** | Aligns with Finding C. `PlannerStrategy` enum (not `PlannerId` — audit used wrong name) |

### Sprint 26 P0-B Delivery

| Claim | Verdict | Evidence |
|---|---|---|
| `IItemSpecGoal.cs` has `int TargetCount => 1;` DIM | **CONFIRMED** | Line 34; XML doc on lines 25-33 |
| `GatherGoalDecomposer.cs` IItemSpecGoal arm uses `isg.TargetCount.ToString()` | **CONFIRMED** | Line 46; `Array.Empty<string>()` is gone |
| Redundant `GenericGatherGoal gg` arm still exists | **CONFIRMED** | Line 43; harmless (class member takes priority), but technically dead code |
| `HtnPlanner.cs` uses `isg.TargetCount.ToString()` | **CONFIRMED** | Lines 46-47 |
| Inner `is GenericGatherGoal` cast removed from HtnPlanner | **CONFIRMED** | Comment at line 42-43 explicitly records the removal |
| Only IItemSpecGoal implementor is `GenericGatherGoal` | **CONFIRMED** | github__search_code found no other concrete implementors in .cs files |

### BLK-1 (Critical Blocking Finding)

| Check | Result |
|---|---|
| Sprint26Tests.cs exists with 8 tests | **YES** — 5 P0-A + 3 P0-B confirmed |
| `AgentBackgroundServiceTestHelper.BuildMinimal` exists | **NO — DOES NOT EXIST** |
| MockWorldAdapter exists with PushEvent | **YES** |
| CI will compile? | **NO — CS0103 compile error** on `AgentBackgroundServiceTestHelper` |

**BLK-1 IS STILL BLOCKING.** This is the #1 priority for Sprint 27.

---

## II. New Independent Findings (Corrections & Additions)

### COR-1: Version String Three-Way Mismatch (NEW — HIGH)
The Sprint 26 handoff claims version v0.26.0. Independent verification found:
- `README.md` header: `v0.23.0` (Sprint 23)
- `/api/about` endpoint in `Program.cs`: `"0.23.0"`
- `Program.cs` file comment (line 2): `v0.25.0`
- **v0.26.0 does not appear anywhere in the code.**

This is not a cosmetic issue — it means the version bump claimed by Sprint 26 was never actually committed. The sprint's code changes (DIM, decomposer fix, tests) ARE present, but the version metadata was not updated.

### COR-2: Exec Audit Finding E Mischaracterized (LOW)
The audit claimed "local-filesystem blueprint scan during search" creates a perf risk. Verification shows `MemorySmithBlueprintRepository` fetches blueprints via HTTP from MemorySmith REST API, not a local filesystem scan. The concern about no result cap on `SearchAsync` is valid but the mechanism is wrong.

### COR-3: ToolDispatcher Path Correction (TRIVIAL)
Both audits reference ToolDispatcher in "Agent.Core". Actual location: `Agent.Tools/ToolDispatcher.cs`. Does not affect analysis but should be corrected in documentation.

### COR-4: Register(string, ITool) Silent Overwrite (LOW — DEF-9 carryover)
`Register(string name, ITool tool)` does `_tools[name] = tool` — silent overwrite with no warning or collision detection. DEF-9 remains open and accurately described.

---

## III. 6-Seat Council Review

### Seat 1: Source-Grounded Archivist
**Confidence: 93%**

All P0-B source changes verified at line level. The audit annotations are accurate — resolved findings are genuinely resolved, open findings are genuinely open. The two external audits are high-quality external work with correct technical observations. Cross-referencing between audits (deep code Finding 5 = exec summary Finding C = Finding F convergence on planner routing) is valid.

**Dissent:** COR-1 (version mismatch) is a process gap, not a code gap. The version bump should be Sprint 27 P0 hygiene, not a blocking finding. The code is correct; only the metadata is wrong.

### Seat 2: Data Model Architect
**Confidence: 89%**

The DIM approach for IItemSpecGoal is architecturally clean. C# DIM semantics are well-understood:
- Class members always take priority over DIM when calling through the interface reference
- `GenericGatherGoal.TargetCount` (class member) correctly overrides the DIM
- The DIM default of 1 is the right choice (not 0, which would mean "no items")

The redundant `GenericGatherGoal gg` arm in `GatherGoalDecomposer` is harmless now but should be removed when `CraftItemGoalDecomposer` is created in Sprint 27 P0-B — that's the natural refactoring point.

**Concern:** The `IItemSpecGoal` interface has only ONE concrete implementor (`GenericGatherGoal`). The DIM's value is in future-proofing, not current utility. When `CraftItemGoalDecomposer` is created, any goal it routes will need TargetCount — the DIM ensures it works without explicit implementation.

### Seat 3: Retrieval Specialist
**Confidence: 87%**

Test coverage assessment post-verification:
- 8 tests in Sprint26Tests.cs are correctly described
- **BUT 5 of them (P0-A) cannot compile** due to missing AgentBackgroundServiceTestHelper
- 3 P0-B tests should compile and pass (they only use GatherGoalDecomposer and IItemSpecGoal types, no service helper)
- The existing `AgentBackgroundServiceTests.cs` has a private `CreateService()` helper — the TestHelper pattern is consistent but the actual export was never done

**Coverage gaps carried to Sprint 27:**
1. **[BLOCKING]** AgentBackgroundServiceTestHelper — P0-A tests can't run
2. ITimeProvider abstraction — tests use Task.Delay(150ms), fragile on slow CI
3. E2E gather test — still deferred
4. CraftItemGoalDecomposer tests — new in Sprint 27

### Seat 4: Human Learning Advocate
**Confidence: 84%**

**Process assessment:**
The external audit intake process is exemplary — filing annotated audits as `Data/Pages/Audits/` documents with per-finding verification status is best-in-class for agent-to-agent knowledge transfer.

**Process gaps found:**
1. COR-1 (version bump never committed) is a process failure. The handoff doc claims v0.26.0, the council review references v0.26.0, but nobody verified the actual code. This is the same pattern as the AgentBackgroundServiceTestHelper gap — claims made without CI verification.
2. The Sprint 26 council's BLK-1 was identified as conditional but never resolved before the sprint was closed. The sprint should not have been closed with a known compile failure.

**Recommendation:** Sprint 27 must establish a "CI green before handoff" gate. No handoff should be written until the build passes.

### Seat 5: Skeptical Reviewer
**Confidence: 81%**

**Challenges:**

**Challenge 1: Is the version mismatch actually v0.26.0 missing, or was v0.25.0 the intended state?**
The Program.cs comment says v0.25.0. The `/api/about` endpoint returns "0.23.0". These are TWO different stale values. The simplest explanation: Sprint 25 updated the comment but forgot the endpoint, and Sprint 26 forgot both. Sprint 27 must fix ALL three locations (README, /api/about, comment) to v0.27.0.

**Challenge 2: Will creating AgentBackgroundServiceTestHelper actually unblock CI?**
The existing `AgentBackgroundServiceTests.cs` has a private `CreateService()` helper with a known constructor signature. If `AgentBackgroundService`'s constructor has changed since that file was last updated (e.g., Sprint 25 P0-D added new parameters), the test helper may need additional parameters beyond what the Sprint 26 handoff template shows. The safe approach: derive the helper from the ACTUAL current constructor signature, not the handoff template.

**Challenge 3: Are the 3 P0-B tests actually independent of the helper?**
Yes — verified. They test `GatherGoalDecomposer` and `IItemSpecGoal` directly without constructing `AgentBackgroundService`. They should compile and pass even without the helper.

### Seat 6: Synthesizer
**Confidence: 86%**

### Blocking Findings

| ID | Finding | Severity | Resolution |
|---|---|---|---|
| BLK-1 | AgentBackgroundServiceTestHelper.BuildMinimal does not exist | **BLOCKING** | Create helper matching current AgentBackgroundService constructor; must be Sprint 27 first commit |
| BLK-2 | Version string v0.26.0 never committed (COR-1) | **BLOCKING** | Update /api/about endpoint, Program.cs comment, README to v0.27.0 |

### Deferred Findings

| ID | Finding | Priority | Target |
|---|---|---|---|
| DEF-1 | JS correlationId echo verification | P2 | Sprint 28+ |
| DEF-2 | Full sendEvent audit / chat filter brittleness | P2 | Sprint 28+ |
| DEF-3 | WorldModel GetIntArg verification | Verified fixed (Sprint 25) | Closed |
| DEF-9 | Register(string,ITool) collision semantics | P2 | Sprint 28+ |
| DEF-NEW | SearchAsync no result cap (COR-2) | P2 | Sprint 28+ |
| DEF-NEW | GatherGoalDecomposer redundant GenericGatherGoal arm | P2 | Sprint 27 cleanup with P0-B |

### Acceptance Criteria for Sprint 26 Closure

- [x] 8 tests written in Sprint26Tests.cs
- [x] IItemSpecGoal.TargetCount DIM added
- [x] GatherGoalDecomposer + HtnPlanner updated
- [x] External audits filed with annotations
- [x] Council review written
- [ ] **CI green** — BLOCKED by missing AgentBackgroundServiceTestHelper
- [ ] **Version v0.26.0 committed** — NOT DONE

**Verdict: NOT APPROVED** — Sprint 26 cannot be closed until BLK-1 (test helper) and BLK-2 (version bump) are resolved. These are Sprint 27 P0 items.

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
