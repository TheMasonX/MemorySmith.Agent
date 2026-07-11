# Sprint 37 — Complete Handoff

**Branch:** sprint-35-llm-first | **HEAD:** (to be updated after push) | **Version:** v0.37.0  
**Date:** 2026-06-22

---

## What Was Delivered

### P0-A — ActionOutcome : IObservationSummary
`Agent.Core/Models/ActionOutcome.cs`

Added `: IObservationSummary` to the `ActionOutcome` record with an explicit interface implementation:
```csharp
string IObservationSummary.Summary => ObservationSummary;
```
This enables ActionOutcome to be passed directly to any LLM evaluator or observation consumer that accepts `IObservationSummary`, completing the observation-driven replanning pipeline contract.

### P0-B — DispatchActionsAsync → CallWithOutcomeAsync
Three files changed:

1. **`Agent.Tools/Interfaces/IToolCaller.cs`** — Added `CallWithOutcomeAsync` as a default interface method. Existing implementations (test doubles, mocks) need no changes — they automatically inherit the default that delegates to `CallAsync`.

2. **`Agent.Tools/ToolDispatcher.cs`** — Removed the final `_journal?.Log(entry)` (ActionCompleted/ActionFailed) from `CallAsync`. Validation-failure and exception journal entries are preserved (different semantics). Updated `CallWithOutcomeAsync` XML doc to reflect Sprint 37 is complete.

3. **`WebUI.Blazor/AgentBackgroundService.cs`** — In `DispatchActionsAsync`:
   - `toolCaller.CallAsync(...)` → `toolCaller.CallWithOutcomeAsync(Guid.Empty, ...)`
   - Added `_journal?.LogOutcome(outcome)` after the call
   - Removed manual ActionCompleted / ActionFailed journal entries from success/failure paths
   - Added Sprint 37 P2-B TODO comment for future LLM evaluator wiring

> **NOTE:** `goalId = Guid.Empty` is a placeholder until `IGoal.Id` is defined (Sprint 38).

### P1-A — IntentManager class (PRINCIPLE-1 enforcement)
`Agent.Planning/IntentManager.cs` (new file)

Extracted the intent→goal mapping switch from `LlmChatInterpreter.ParseDecision` into:
- `IntentManager.BuildGoalRequest(IntentDraft)` → returns `GoalRequest?`
- `GoalRequest` record: `{ GoalName, Parameters }` — ready for `GoalFactory.CreateAsync`

This is the first PRINCIPLE-1 compliant routing path: the parser now produces `IntentDraft` (semantic data), and `IntentManager` maps it to goal factory inputs.

### P1-B — LlmChatInterpreter refactored
`Agent.Planning/LlmChatInterpreter.cs`

- Constructor gains `IntentManager? intentManager = null` parameter
- `ParseDecision` delegates to `IntentManager.BuildGoalRequest(draft)` when injected
- Legacy local switch retained as fallback for backward compat (Sprint 38 target: remove it)
- `_intentManager` field threaded from constructor to ParseDecision call in `InterpretAsync`

### P1-C — IntentAssessment record
`Agent.Planning/IntentAssessment.cs` (new file)

```csharp
public sealed record IntentAssessment(
    IntentDraft Draft,
    RiskLevel RiskLevel,
    bool RequiresConfirmation,
    string ReasoningSummary);

public enum RiskLevel { Low, Medium, High }
```

Confidence ≠ risk. "Build a house" can be high-confidence + high-risk. Sprint 38 will wire `IntentAssessment` into the confirmation gate.

### Program.cs — DI wiring
`WebUI.Blazor/Program.cs`

- Registered `IntentManager` as singleton
- Injected `intentManager: sp.GetRequiredService<IntentManager>()` into `LlmChatInterpreter`
- Version bumped to `v0.37.0`

### Tests — Sprint37Tests.cs
`MemorySmith.Agent.Tests/Sprint37Tests.cs` (new file, 10 tests)

| Category | Tests |
|---|---|
| P0-A IObservationSummary | 3 (implements check, Summary→ObservationSummary map, failed outcome) |
| P0-B IToolCaller default | 2 (success path, failure path) |
| P1-A IntentManager | 4 (gather, craft, build with coords, navigate, conversation→null, gather without item) |
| P1-C IntentAssessment | 1 (record field accessibility) |

**Total new test count: ~318+**

### Council Review
`Data/Pages/council/sprint37-council-20260622.md`

6-seat review passed at 0.88 average confidence, 0 blocking findings, 4 deferred.

---

## Sprint 38 Priorities

**P0 — Complete PRINCIPLE-1 enforcement**
- [ ] Remove the legacy switch from `LlmChatInterpreter.ParseDecision` (legacy path is Sprint 38 target)
- [ ] Remove the legacy switch from `TryParseTruncatedJson` (same migration)
- [ ] Change `IChatInterpreter.InterpretAsync` to return `IntentDraft` directly (removes `ChatInterpretation.GoalName`)

**P1 — IGoal.Id property**
- [ ] Add `Guid Id { get; }` to `IGoal` interface
- [ ] Replace `Guid.Empty` in `DispatchActionsAsync.CallWithOutcomeAsync(Guid.Empty, ...)` with `_currentGoal.Id`

**P2 — Observation-driven replanning (Sprint 37 P2-B TODO)**
- [ ] `Plan → Execute → ActionOutcome[] → LLM Evaluate → Replan?`
- [ ] Accumulate `outcome[]` in `DispatchActionsAsync` dispatch loop
- [ ] Pass to a new `ILlmEvaluator.EvaluateAsync(goal, outcomes[])` → bool (replan?)

**P3 — AgentRuntime.TickAsync decomposition**
- [ ] `AgentBackgroundService.ExecuteAsync → AgentRuntime.TickAsync()`
- [ ] Each IAgentRuntimeComponent wires to a concrete implementation
- [ ] `ItemConsumedEvent` wiring (ingredient deduction during craft)

---

## Key Architectural Invariants (DO NOT VIOLATE)

1. **PRINCIPLE-1: Parsers never create goals.** `LlmChatInterpreter` produces `IntentDraft`. `IntentManager` maps to `GoalRequest`. `GoalFactory` creates `IGoal`. No shortcuts.

2. **ActionOutcome is the universal tool result artifact.** Every `ToolDispatcher.CallAsync` execution produces (or can produce) an `ActionOutcome`. Recovery, replanning, journaling, and world-state updates all consume this.

3. **WorldStateProjector is the sole state reducer.** No direct inventory mutation outside it.

4. **ToolDispatcher is the safety wall.** Schema validation happens at this boundary. Never bypass it.
