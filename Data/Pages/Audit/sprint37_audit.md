# Sprint 37 Audit — MemorySmith.Agent

## Scope
Reviewed the Sprint 37 completion summary against the current implementation goals for the `sprint-35-llm-first` branch at `245f78f7`, focusing on the new observation pipeline, dispatch wiring, intent extraction, and the remaining Sprint 38 risks.

## Executive delta versus the Sprint 36 handoff

Sprint 37 materially improved the agent’s architecture. The observation path now has a real contract (`ActionOutcome : IObservationSummary`), the dispatch loop now captures outcomes instead of raw tool results, and intent-to-goal mapping has been extracted out of the chat interpreter into `IntentManager`. The DI wiring is now consistent with the prompt-enrichment plan.

The important delta is that the pipeline is now structurally present, but still not fully closed. The current implementation has the right seams for an LLM-orchestrated agent, yet there are still placeholder values, legacy fallback logic, and not-yet-wired confirmation scaffolding that keep Sprint 38 squarely in the critical path.

## What Sprint 37 actually completed

1. **Observation contract closed enough for the next stage.**  
   `ActionOutcome` now implements `IObservationSummary` explicitly, so the LLM evaluator can consume structured tool outcomes directly once the replanning loop is wired.

2. **Dispatch now produces outcomes.**  
   `DispatchActionsAsync` now calls `CallWithOutcomeAsync` instead of `CallAsync`, and the journal outcome path is handled explicitly in the background service. That is the correct shape for observation-driven replanning.

3. **Intent mapping has been extracted.**  
   `IntentManager` now owns the intent-to-goal mapping, which is the right move for Principle-1. `LlmChatInterpreter` delegates to it when injected.

4. **Prompt/tool-name visibility improved.**  
   `ToolDispatcher.RegisteredNames` is wired into `Program.cs`, replacing the earlier nondeterministic alias-dropping path. That fixes a real prompt-quality issue.

## New findings and deltas to carry into Sprint 38

### 1) The goal correlation is still a placeholder
The dispatch loop is now outcome-aware, but the goal correlation is still using `Guid.Empty` for `CallWithOutcomeAsync`. That means the observation pipeline is not yet goal-scoped in a meaningful way.

This is the main architectural gap left by Sprint 37. Until each action outcome carries a real goal ID, the system cannot reliably accumulate, compare, and evaluate a goal’s observation history.

**Impact:**  
Observation-driven replanning, recovery, and future journaling will be much weaker than the sprint narrative implies unless `IGoal.Id` replaces the placeholder.

**Priority:** P0 for Sprint 38.

### 2) IntentManager extraction is partial until the fallback switch is removed
The extraction is valuable, but the handoff should be clear that `LlmChatInterpreter` still retains legacy mapping logic as fallback. That means the interpreter is not yet a pure parser and still violates the intended separation.

**Impact:**  
There are now two places that can decide goal semantics. That invites drift, especially when new intents, confirmation states, or ambiguous cases are added.

**Priority:** P0/P1 for Sprint 38. Remove the fallback mapping once the new path is proven.

### 3) `IntentAssessment` is scaffolding, not a working gate
`IntentAssessment` exists, but its value only appears when it actively gates whether an intent becomes a goal, a clarification request, or a no-op. If the implementation stops at the DTO, then the agent still acts too early on ambiguous inputs.

**Impact:**  
The system will continue to over-commit on uncertain user language instead of asking for clarification.

**Priority:** P1 for Sprint 38.

### 4) The `CallAsync` journal path needs an audit pass
`ToolDispatcher.CallAsync` no longer emits its own action-completed/failed journal entry. That is probably correct for the new outcome-driven model, but it creates an implicit contract: every meaningful runtime path must either use `CallWithOutcomeAsync` or consciously accept journal silence.

**Impact:**  
Any remaining `CallAsync` callers may become invisible in telemetry or lose useful trail data.

**Priority:** Medium, but important. Audit all remaining `CallAsync` call sites.

### 5) The safety wall remains correct and should not be weakened
`ToolDispatcher` still needs to remain the hard boundary for schema validation and execution safety. Do not move validation into the LLM layer or relax the dispatcher to “help” the model. The model should choose; the dispatcher should validate.

**Impact:**  
This is still the most important hard boundary in the system.

**Priority:** Preserve as-is.

## What is now the right direction

The project is now best understood as three layers:

- **LLM layer:** intent interpretation, clarification, evaluation, recovery.
- **Tool layer:** schema validation and execution safety.
- **World layer:** canonical state reduction and observation history.

Sprint 37 moved the code closer to that model. The remaining work is about finishing the contract, not rethinking the architecture again.

## Recommended next-step order

1. Replace `Guid.Empty` with real goal IDs and make outcome correlation first-class.
2. Remove the legacy intent→goal fallback from `LlmChatInterpreter`.
3. Wire `IntentAssessment` into an actual confirmation gate.
4. Audit all remaining `CallAsync` call sites and migrate them intentionally.
5. Wire `ActionOutcome[]` into evaluator/replanner input.
6. Only after that, continue decomposing `AgentBackgroundService`.

## Source grounding

Reviewing this sprint should always start from the source files, not the handoff prose:

- Sprint 37 completion summary and commit log provided by the user.
- `245f78f7` Sprint 37 head.
- Sprint 37 commits explicitly called out in the handoff:
  - `cbe70577` — `ActionOutcome : IObservationSummary`
  - `5ea45e38`, `db6fd62c`, `9e4c28b6` — dispatch wiring to `CallWithOutcomeAsync`
  - `af4ebd06` — `IntentManager`
  - `04f26687` — `LlmChatInterpreter` refactor
  - `78546148` — `IntentAssessment`
  - `ce439c41` — DI wiring + version bump

## Short verdict

Sprint 37 is a real architectural step forward. It did not finish the job, but it did remove enough friction that Sprint 38 can finally focus on closing the observation loop, eliminating legacy intent mapping, and making the LLM the real high-level orchestrator.
