# Sprint 25 Handoff — Tool Boundary Hardening + Action Lifecycle

**Date**: 2026-06-19
**Branch**: `sprint-5-tool-safety`
**Previous sprint**: Sprint 24 (planning only — no implementation commits landed)
**Version**: v0.23.0 (Sprint 23 was the last implementation sprint)

---

## Context

Two independent external audits were conducted against the `sprint-5-tool-safety` branch on 2026-06-19:

1. **Deep Code Audit** (`Data/Pages/council/deep-code-audit-20260619.md`) — focused on architectural seams, type safety, and module coherence.
2. **Reliability Report** (`Data/Pages/council/reliability-report-20260619.md`) — focused on runtime failure modes, success semantics, and operational resilience.

An independent source-level verification was performed against both audits using 3 parallel agents reading actual files on the branch. Results:

- **9 findings confirmed** with line-level evidence
- **1 finding refuted** (LLM blocking world events — Channel pattern is already in place)
- **Sprint 24 status: fully unstarted** — all 15 commits since the Sprint 24 handoff doc are documentation ports from main, zero implementation changes

### Human reviewer annotations

The deep code audit included open questions. Human reviewer responses:

- **Tool execution exceptions**: ToolResult is the only failure channel. All failures should be channeled into traceable results and explicit control flow (modern result pattern).
- **World model snapshots**: Should be immutable states in time.
- **PlannerRouter vs HtnPlanner**: Modernize and prefer flexible over hardcoded.
- **Journal semantics**: To be decided (operational log vs durable source of truth).

---

## Sprint 24 carry-forward (all items — nothing was implemented)

| ID | Item | Original priority | Sprint 25 status |
|---|---|---|---|
| B-1 | FindFlatAreaTool defaults (radius=32, minFlatArea=25) | P0 | **Absorbed into Sprint 25 P0-A** |
| B-2 | Delete StatusTool.cs / deduplicate with GetStatusTool.cs | P0 | **Absorbed into Sprint 25 P0-B** |
| P0-C | PendingAction + ActionLifecycle + correlationId | P0 | **Absorbed into Sprint 25 P0-D** |
| P0-D | TryInterruptOnDamage integration test | P0 | **Absorbed into Sprint 25 P1-B** |
| P1-A | GatherGoalDecomposer TargetCount fix | P1 | **Sprint 25 P1-C** |
| P1-B | End-to-end gather integration test | P1 | **Sprint 25 P1-D** |
| P1-C | Startup constant log | P1 | **Sprint 25 P2-A** |
| P2-A | ITimeProvider abstraction | P2 | **Sprint 25 P2-B** |
| P2-B | MOVE_EMIT_THROTTLE_MS in index.js | P2 | **Sprint 25 P2-C** |
| P2-C | IWorldObservationGateway note | P2 | **Sprint 25 P2-D** |

---

## Cross-audit convergence analysis

Both audits independently identified the same top-3 priorities, which strengthens confidence in this sprint plan:

| Theme | Deep Code Audit | Reliability Report | Independent verification |
|---|---|---|---|
| Tool validation/exception boundary | Finding 1 + Finding 2 (92%/88%) | Priority 2 (constants) + "one-sided completion" | **CONFIRMED** — Contains('.') integer check, no try/catch in CallAsync |
| Action success semantics | Finding 2 (dispatcher assumes no throw) | Finding 1 + Priority 1 ("dispatched != done") | **CONFIRMED** — all 4 core tools return success before world event |
| Constants/defaults mismatch | Mentioned in context | Finding 2 + Priority 2 (three flat-area regimes) | **CONFIRMED** — C# 20/9 vs JS 32/25 still live |
| World model immutability | Finding 3 (86%) | Not directly covered | **CONFIRMED** — shared mutable Dict in WorldModel constructor |
| Planner routing duplication | Finding 5 (81%) | Priority 6 (keep planner simple) | **CONFIRMED** — HtnPlanner has 4 hardcoded branches overlapping DecomposerRegistry |
| LLM blocking events | Not mentioned | Finding 4 + Priority 3 | **REFUTED** — Channel<WorldEvent> already in place |

---

## Sprint 25 priorities

### Theme: Tool Boundary Hardening + Action Lifecycle

This sprint addresses the convergent top findings from both external audits plus all carry-forward Sprint 24 work. The sprint is scoped to what can be delivered and tested in a single session.

---

### P0 — Blocking (must fix before council approves)

#### P0-A: FindFlatAreaTool constant unification + safe integer parsing
**Source**: Sprint 24 B-1 + Deep Code Audit Finding 1 + Reliability Report Finding 2
**Both audits agree**: Constants disagree across layers and GetInt32() can throw.

Changes:
- `FindFlatAreaTool.cs`: Change defaults to `radius = 32`, `minFlatArea = 25`
- `FindFlatAreaTool.cs`: Update `Description` string to match new defaults
- `FindFlatAreaTool.cs`: Replace `r.GetInt32()` with `r.TryGetInt32(out var rv) ? rv : 32` (same for minFlatArea)
- Test: `FindFlatAreaDefaults_MatchJsAdapter` — verify defaults match JS constants
- Test: `FindFlatArea_ScientificNotation_FallsBackToDefault` — verify 1e5 doesn't crash

#### P0-B: StatusTool deduplication
**Source**: Sprint 24 B-2 + Reliability Report Finding 2

Changes:
- Delete `Agent.Tools/Tools/StatusTool.cs`
- In DI registration (Program.cs), register GetStatusTool under both "GetStatus" and "Status" names
- Test: `ToolDispatcher_StatusAlias_DispatchesSameClass` — verify both names resolve to GetStatusTool

#### P0-C: ToolDispatcher exception wrapping
**Source**: Deep Code Audit Finding 2 (88% confidence) + Human reviewer annotation (ToolResult-only failure channel)
**New finding from external audit — not in Sprint 24 backlog.**

Changes:
- `ToolDispatcher.CallAsync`: Wrap `tool.ExecuteAsync(...)` in try/catch. On exception, return `ToolResult(false, $"Tool '{name}' threw: {ex.Message}")` and log JournalEntry with ActionFailed type.
- `ToolDispatcher.ValidateAgainstSchema`: Replace `value.GetRawText().Contains('.')` integer check with `value.TryGetInt32(out _)`. This is correct for System.Text.Json and handles scientific notation.
- Test: `CallAsync_ToolThrows_ReturnsFailureResult` — mock tool that throws, verify ToolResult.Success == false
- Test: `ValidateSchema_ScientificNotation_RejectedAsNonInteger` — verify `1e5` is rejected for integer fields
- Test: `ValidateSchema_DecimalInInteger_Rejected` — verify `1.5` still rejected

#### P0-D: Action correlation ID infrastructure
**Source**: Sprint 24 P0-C + Reliability Report Priority 1 + Finding 1
**Both audits' strongest recommendation**: Stop treating "dispatched" as "done."

Changes:
- New record: `Agent.Core/Models/PendingAction.cs` — `PendingAction(Guid CorrelationId, string ToolName, DateTimeOffset DispatchedAt, ActionLifecycle State)`
- New enum: `Agent.Core/Models/ActionLifecycle.cs` — `Dispatched, Acknowledged, Completed, Failed, TimedOut`
- `AgentBackgroundService`: Replace `List<ActionData> _pendingActions` with `ConcurrentDictionary<Guid, PendingAction>`
- `AgentBackgroundService.DispatchActionsAsync`: Inject `correlationId` into `ActionData.Context` before dispatch
- `MineflayerAdapter/index.js`: Echo `correlationId` from received action data in all result events (moveComplete, blockMined, blockPlaced, craftComplete, smeltComplete, flatAreaFound, wanderComplete)
- `AgentBackgroundService.ProcessEventsAsync`: On result event, look up correlationId → transition PendingAction state → log lifecycle
- `AgentBackgroundService.StopAsync`: Log warning for any PendingActions still in Dispatched state
- Timeout sweep: In the governor cycle or DispatchActionsAsync, check all PendingActions in Dispatched state older than 30s (existing per-action timeout). Transition to TimedOut state and log warning. This prevents orphaned PendingActions when adapter crashes or events are lost.
- Test: `PendingAction_LifecycleTransition_DispatchedToCompleted`
- Test: `PendingAction_Timeout_MarkedTimedOut`
- Test: `PendingAction_UnknownCorrelation_Ignored`
- Test: `Shutdown_PendingActions_LoggedAsAbandoned`
- Test: `PendingAction_ConcurrentDispatch_IndependentTracking` — two actions dispatched simultaneously tracked independently
- Test: `PendingAction_StaleDispatch_TimesOutAfter30s` — verify timeout sweep catches stale entries
- Test: `PendingAction_DuplicateCorrelationInEvent_HandledGracefully` — malformed adapter sends same correlationId twice

---

### P1 — Should-ship

#### P1-A: WorldModel defensive copy at construction
**Source**: Deep Code Audit Finding 3 (86%) + Human reviewer annotation (immutable snapshots)
**New finding from external audit.**

Changes:
- `WorldModel` constructor: Replace `var empty = new Dictionary<string, int>()` shared instance with separate `new Dictionary<string, int>()` for each of `_observed` and `_belief`
- `WorldModel.Observe()`: Deep-copy `observation.Inventory` into belief state: `new Dictionary<string, int>(observation.Inventory)`
- Test: `WorldModel_Observe_DoesNotAliasInventory` — mutate source dict after Observe, verify belief unchanged
- Test: `WorldModel_Constructor_SeparateInstances` — verify `_observed.Inventory` and `_belief.Inventory` are not ReferenceEquals

#### P1-B: TryInterruptOnDamage integration test
**Source**: Sprint 23 D-6 carry-forward via Sprint 24 P0-D

Changes:
- Integration test: Simulate consecutive HealthEvents with decreasing health → verify DamageTakenEvent synthesized → verify ActionQueue.ClearAndEnqueue called → verify emergency stop sent
- Verify cooldown: Second damage within DamageInterruptCooldownSeconds is suppressed

#### P1-C: GatherGoalDecomposer TargetCount pass-through
**Source**: Sprint 23 carry-forward via Sprint 24 P1-A

Changes:
- Create `Agent.Planning/Decomposers/GatherGoalDecomposer.cs` if it doesn't exist (note: file not found on branch — may need to create or may be named differently)
- Ensure TargetCount from IItemSpecGoal is passed through to MineBlock parameters (not defaulting to 10)
- Test: `GatherDecomposer_PassesTargetCount_ToMineAction`

#### P1-D: End-to-end gather integration test
**Source**: Sprint 24 P1-B + Reliability Report Priority 4

Changes:
- Create integration test fixture with fake IWorldAdapter
- Test chain: chat intent "get 5 oak_log" → GoalFactory → plan decomposition → action dispatch → fake adapter emits blockMined events → WorldStateProjector → inventory update → goal IsComplete
- Verify the full closed loop without any real Minecraft connection

---

### P2 — Nice-to-have

#### P2-A: Startup constant log
**Source**: Sprint 24 P1-C

Log the top tunable constants at startup via LogInformation:
- DamageInterruptCooldownSeconds, HealthCheckCooldownSeconds, HealthCriticalThreshold
- ReplanGovernor thresholds (IdenticalPlanThreshold, StallRecoverySeconds)
- WorldState MaxFacts, AgentJournal MaxEntries

#### P2-B: ITimeProvider abstraction
**Source**: Sprint 23 D-8 via Sprint 24 P2-A

- `ITimeProvider` interface with `DateTimeOffset UtcNow` property
- `SystemTimeProvider` production implementation
- `FakeTimeProvider` for test control
- Inject into AgentBackgroundService, ReplanGovernor, WorldModel

#### P2-C: Move event throttling
**Source**: Sprint 24 P2-B + Reliability Report Priority 5

- Add `MOVE_EMIT_THROTTLE_MS = 250` constant in index.js
- Throttle `move` event emission to reduce WebSocket traffic

#### P2-D: IWorldObservationGateway note
**Source**: Sprint 23 D-5 via Sprint 24 P2-C

- Add architectural note in architecture.md about future IWorldObservationGateway interface

---

### Deferred to Sprint 26+

| ID | Item | Source | Rationale |
|---|---|---|---|
| D-1 | World model full immutability (copy-on-write at projector boundary) | Deep Code Audit Arch-B | P1-A addresses the constructor aliasing; full immutability is a larger refactor |
| D-2 | Planner routing consolidation (delete HtnPlanner hardcoded branches) | Deep Code Audit Finding 5 + Arch-C | Requires careful migration of CraftItemGoal which has no registry decomposer yet |
| D-3 | Journal semantics decision (bounded log vs event store) | Deep Code Audit Finding 4 + Arch-D | Needs product-level decision on journal's role |
| D-4 | Adapter reconnection strategy | Reliability Report Priority 5 | Larger reliability project |
| D-5 | Structured message classification (replace regex firewall) | Deep Code Audit Finding 6 | Working well enough for now; priority over feature work later |
| D-6 | Typed tool argument parsing (validate once, parse once) | Deep Code Audit Arch-A | Major refactor of tool layer; P0-C addresses the immediate safety gap |
| D-7 | LLM decoupling (already done — Channel pattern in place) | Reliability Report Priority 3 | **Already implemented** — verified in AgentBackgroundService._chatChannel |
| D-8 | Place block navigation step + reference-block selection | Reliability Report Priority 5 | Operational improvement, not blocking |

---

## Files to commit

| Path | Description |
|---|---|
| `Data/Pages/council/deep-code-audit-20260619.md` | External deep code audit (raw) |
| `Data/Pages/council/reliability-report-20260619.md` | External reliability report (raw) |
| `Data/Pages/Tasks/agent-handoff-sprint25.md` | This document |

---

## Acceptance criteria

Sprint 25 is complete when:
1. All P0 items are implemented with passing tests
2. Build compiles with TreatWarningsAsErrors=true and zero warnings
3. All existing tests continue to pass
4. Council review approves with no blocking findings
5. CI green on the sprint-5-tool-safety branch

## Test count target

Current: ~200+ tests (Sprint 23 baseline)
Sprint 25 adds: 17-20 new tests across P0 items (expanded per council B-2)
Expected: 220+ tests passing

---

## Risk notes

1. **P0-D (correlation IDs) is the largest item** — touches both C# (AgentBackgroundService, new records) and JS (index.js event emission). Should be implemented last among P0s after B-1/B-2/C are confirmed green.
2. **Rule E-1 applies** — never patch C# verbatim-string files via agent intermediary. Use `mcp__t__ExecuteIntegration` with paramsFile for all file writes.
3. **StatusTool dedup (P0-B)** may require checking DI registration patterns — the current branch may register tools by scanning for ITool implementations, so deletion + alias may need specific DI wiring.
4. **GatherGoalDecomposer (P1-C)** — the file was not found on the branch. It may exist under a different name or path, or this may be a *create* task rather than a *fix* task. Search the codebase before implementing. If creating from scratch, understand the IGoalDecomposer interface and DecomposerRegistry contract first. Consider deferring if scope exceeds expectations.
5. **Priority ordering note** — the external deep code audit suggested world model immutability as priority #2, but this sprint places it at P1-A (after all P0 tool safety items). This is a deliberate choice: tool safety is the convergent top recommendation from both audits and has more immediate runtime impact. WorldModel aliasing is a correctness risk but has not yet caused observable bugs.
6. **SearchMemoryTool and GetPageTool** also throw on missing arguments rather than returning ToolResult failures. P0-C's try/catch wrapper in CallAsync will catch these, but the tools should eventually be updated to return ToolResult failures directly (deferred to Sprint 26).
