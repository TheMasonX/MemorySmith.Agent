# Agent Handoff — Sprint 39 (Full) / Sprint 40 Kickoff

**Date:** 2026-06-22  
**Branch:** sprint-35-llm-first  
**Sprint 39 head (second half):** f90c4a47  
**Tests (local baseline):** ~608 passed / 0 failed (577 pre-sprint + 31 new Sprint 39 second-half tests)

---

## What Was Done (Sprint 39 Second Half)

Sprint 39 second half completes three work items left from the first half.

### P1 — Concrete LlmEvaluatorImpl + DispatchActionsAsync wiring

**Agent.Planning/LlmEvaluatorImpl.cs** (new file, commit ec4db4ca)  
Implements `ILlmEvaluator`:
- Fast-paths: fewer than 3 outcomes → skip; all outcomes successful → skip; provider unavailable → skip
- Calls `ILlmProvider.CompleteAsync` with a compact system prompt + world state snapshot + last 10 outcomes
- Parses `{ "replan": true|false }` JSON from the response (with prose-stripping)
- Any error (network, timeout, parse) → returns false (conservative: continue current plan)

**AgentBackgroundService.cs** patched (commit 40fbe34d):
- Added `ILlmEvaluator? llmEvaluator = null` and `AgentRuntime? agentRuntime = null` optional constructor params
- Replaced the 2-line Sprint 38 TODO comment in `DispatchActionsAsync` with the actual evaluation call:
  - `_llmEvaluator.EvaluateAsync(_currentGoal, snapshot, _worldState, ct)` after each `_cycleOutcomes.Enqueue`
  - On `true` → `break` exits the action dispatch loop → outer while calls `PlanAsync` again (fresh plan)
  - Exception handling: OperationCanceledException re-thrown; all others → log Warning + continue

**Program.cs** patched (commit 1ede3f6d):
- Registered `LlmEvaluatorImpl` as `ILlmEvaluator` singleton
- Injected `llmEvaluator: sp.GetRequiredService<ILlmEvaluator>()` into the ABS factory

### P2 — AgentRuntime decomposition (concrete manager implementations)

Six concrete manager classes created under `WebUI.Blazor/Managers/`:

| Class | Interface | File commit |
|---|---|---|
| IntentManagerImpl | IIntentManager | d62868d3 |
| PlanningManagerImpl | IPlanningManager | 84822535 |
| ExecutionManagerImpl | IExecutionManager | 00c5d982 |
| RecoveryManagerImpl | IRecoveryManager | 536e9633 |
| StateManagerImpl | IStateManager | 559c3917 |
| DashboardPublisherImpl | IDashboardPublisher | 844a4a0d |

**IntentManagerImpl**: wraps `IChatInterpreter` + existing `IntentManager` (goal mapper). Bridges the slim `IIntentManager.ProcessChatAsync(username, message, state, ct)` signature to the full `IChatInterpreter.InterpretAsync` which needs botName, onlinePlayers, and playerPosition. Uses `state.Position` for botPosition; playerPosition is null (distance gate degrades gracefully).

**PlanningManagerImpl**: wraps `IPlanner` + `IReplanGovernor?`. Holds `IsReplanRequested` flag. `RequestReplan()` sets it; `PlanAsync()` clears it.

**ExecutionManagerImpl**: wraps `IToolCaller`. Serializes `ActionData.Arguments` (Dictionary) to JSON → JsonElement for `CallWithOutcomeAsync`. `SetCurrentGoal(IGoal?)` stores the current goalId for ActionOutcome correlation.

**RecoveryManagerImpl**: Sprint 39 stub — always returns false. Recovery logic stays in `AgentBackgroundService.TryRecoverFromGameErrorAsync` until Sprint 40.

**StateManagerImpl**: thread-safe `WorldState` read-model backed by `WorldStateProjector`. `Apply(WorldEvent)` → lock → `_projector.Apply(_current, ev)`. `Reset(WorldState)` for goal transitions.

**DashboardPublisherImpl**: sends `agentStatusUpdated` event to all SignalR clients with health/food/position/inventory from `IStateManager.Current`. Uses anonymous object (Sprint 40: swap for typed AgentStatusUpdate DTO).

**Program.cs** also registered all 6 managers and `AgentRuntime` singleton (commit 1ede3f6d). `AgentRuntime? agentRuntime` is now injected into ABS (stored as `_agentRuntime`) for Sprint 40 to delegate to.

### P3 — Typed GoalRequest + deeper schema validation

**Agent.Planning/IntentManager.cs** refactored (commit 168a1104):
- `GoalRequest` changed from `sealed record(GoalName, Parameters)` to `abstract record(GoalName)` with `abstract Parameters` property
- Four typed subclasses added: `GatherGoalRequest(Item, Count)`, `CraftGoalRequest(Item, Count)`, `BuildGoalRequest(Blueprint, OriginX?, OriginY?, OriginZ?)`, `NavigateGoalRequest(X, Y, Z)`
- `IntentManager.BuildGoalRequest` updated to return typed subclasses instead of raw dictionary-param GoalRequests
- Backward compat preserved: all callers use `goalRequest.Parameters` (virtual → override, same return type)

**Agent.Tools/ToolDispatcher.cs** extended (commit 3faf0a5c):
- `ValidateAgainstSchema` refactored: type check now in one branch, constraint check delegated to new `CheckConstraints` method
- `CheckConstraints` (private static): validates `minimum`/`maximum` (numeric), `enum` (any type via raw JSON comparison), `minLength`/`maxLength` (string)
- 10 new tests in `Sprint39SchemaValidationExtensionTests`

### Tests added

**Sprint39Tests.cs** (commit f90c4a47) — 31 new test methods across 3 new fixture classes:
- `Sprint39LlmEvaluatorImplTests` (9) — skip conditions, replan/continue JSON, null/unparseable response, prose-wrapped JSON extraction, world state propagation to LLM message
- `Sprint39TypedGoalRequestTests` (12) — GoalName correctness, Parameters content, IntentManager routing for all 4 intent types + null intent
- `Sprint39SchemaValidationExtensionTests` (10) — minimum/maximum/enum/minLength/maxLength validation, boundary values, enum for integers

---

## Architecture State Post Sprint 39

```
Chat → IChatInterpreter → IntentDraft → IntentManager.BuildGoalRequest → GoalRequest
     → AgentBackgroundService.TryCreateGoalFromChatAsync → GoalFactory → IGoal
     → DispatchActionsAsync → IPlanner.PlanAsync → ActionPlan
     → foreach(action) → IToolCaller.CallWithOutcomeAsync → (ToolResult, ActionOutcome)
     → _cycleOutcomes.Enqueue → ILlmEvaluator.EvaluateAsync → shouldReplan?
         → break → outer while → PlanAsync again (fresh plan)
```

`AgentRuntime` record now registered and injected into ABS. It contains all 6 manager implementations as typed properties. Sprint 40 will migrate ABS logic to delegate through this runtime.

---

## Sprint 40 Priorities

### P0 — Full ABS → AgentRuntime delegation
`AgentBackgroundService` is still 1702 lines. The managers exist but ABS doesn't yet delegate to them for its core loops. Sprint 40 completes the migration:
1. `HandleChatEventAsync` → delegates to `IIntentManager.ProcessChatAsync` (IntentManagerImpl)
2. `DispatchActionsAsync` inner loop → delegates to `IExecutionManager.DispatchAsync` (ExecutionManagerImpl)
3. `ProcessEventsAsync` → delegates to `IStateManager.Apply` (StateManagerImpl)
4. `PublishStatusAsync` → delegates to `IDashboardPublisher.PublishStatusAsync` (DashboardPublisherImpl)
5. `TryRecoverFromGameErrorAsync` → migrates to `IRecoveryManager.TryRecoverAsync` (RecoveryManagerImpl)
6. `DispatchActionsAsync` outer loop → delegates to `IPlanningManager.PlanAsync` (PlanningManagerImpl)
7. ABS.ExecuteAsync becomes: `while(!ct.IsCancellationRequested) { await runtime.TickAsync(ct); }`

### P1 — IntentAssessment wrapper (Sprint 36 design target)
Wrap `IntentDraft` in `IntentAssessment { IntentDraft, RiskLevel, RequiresConfirmation, ReasoningSummary }`. Confidence (how sure we are what the user meant) is separate from risk (impact of doing it). Low confidence → ask clarification; high risk → ask confirmation.

### P2 — ExecutionManagerImpl.SetCurrentGoal wiring
Connect `ExecutionManagerImpl.SetCurrentGoal` to ABS's `SetGoal` so ActionOutcomes carry the real GoalId. Currently defaults to `Guid.Empty`.

### P3 — RecoveryManagerImpl full implementation
Extract `TryRecoverFromGameErrorAsync` from ABS into `RecoveryManagerImpl`. Needs: `IChatInterpreter` (parse recovery intent), `GoalFactory` (create recovery goal), and `SetGoal` entry point (via the decomposed pipeline).

### P4 — DashboardPublisherImpl DTO alignment
Replace the anonymous object in `PublishStatusAsync` with the typed `AgentStatusUpdate` DTO from `WebUI.Blazor/Dtos.cs`, and wire in the current `IGoal.Name` from the `IPlanningManager`/`IStateManager` pipeline.

---

## AGENTS.md Notes for Next Agent

1. **GoalRequest is now abstract.** The only constructor is in the 4 typed subclasses. Do NOT write `new GoalRequest("...", dict)` — use `new GatherGoalRequest(...)` etc. or `IntentManager.BuildGoalRequest(draft)`.

2. **LlmEvaluatorImpl is in Agent.Planning.** It uses `ILlmProvider` (Agent.Planning.Llm) and `ILlmEvaluator` (Agent.Core). Placing it in Agent.Planning was necessary because Agent.Core has no project references.

3. **Manager impls are in WebUI.Blazor/Managers/.** They can reference all project assemblies (Agent.Core, Agent.Planning, Agent.Tools, Agent.World.Minecraft, Agent.Memory, Agent.Construction).

4. **AgentRuntime is already registered in DI** as a singleton (Program.cs). It can be injected into any component that needs access to all 6 manager components.

5. **ABS still does the work.** The managers exist and are registered but ABS has NOT yet delegated its core loops to them. Sprint 40 does the actual loop delegation.

6. **`RecoveryManagerImpl` always returns false.** Recovery logic is still in `ABS.TryRecoverFromGameErrorAsync`. The RecoveryManagerImpl is a placeholder that makes the AgentRuntime record constructable.
