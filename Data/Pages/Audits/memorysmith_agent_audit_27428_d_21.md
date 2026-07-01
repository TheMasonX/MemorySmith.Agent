# MemorySmith.Agent code audit — commit `27428d21ce40bbe398487a3911b55422e0957680`

## Scope
Deep-dive audit of the current codebase with emphasis on the latest handoff commit and on the agent’s ability to chain actions, detect unexpected world results, and replan toward goals.

## Executive summary
This commit itself is mostly documentation and task scaffolding, but it sits on top of a runtime architecture that is already moving toward a richer agent loop: `ActionOutcome`, `ILlmEvaluator`, `TaskSequenceGoal`, `IntentDraft`, and `IntentManager` are all steps in the right direction. The remaining risk is that the runtime still behaves like a mostly reactive executor with a few special cases layered on top.

The biggest architectural gap is that the code does not yet model goal/planning/execution/result/recovery as a first-class state machine. It mostly models *intent → goal → plan → dispatch → event updates* and then relies on ad hoc heuristics to recover when something goes wrong.

## Findings from the latest commit

### 1) The commit adds no execution-path changes
The commit adds a handoff page, roadmap edits, and new task JSON files. It does not itself change the planner, dispatcher, or world-state machinery. That means the repo still depends on pre-existing runtime behavior for all of the “smart chaining” goals.

### 2) Command execution remains too trust-based
`ChatOptions.CommandExecutionEnabled` is documented as defaulting to `false`, but the initializer is `true`, so command execution is enabled by default despite the safety comment. `LlmChatInterpreter.BuildSystemPrompt` also does not enumerate a safe command surface; it only says commands are enabled or disabled. `HandleChatEventAsync` then executes any slash-prefixed command that the LLM emits as long as the string starts with `/`.

This is a high-risk trust boundary because the LLM can emit malformed, overly broad, or unexpected commands and the runtime treats them as executable without a typed command schema or allowlist.

### 3) Multi-step support exists, but only as fixed chaining
`IntentDraft.NextSteps`, `TaskSequenceGoal`, and `IntentManager.ParseCommandString` provide a basic sequencing mechanism. That supports simple chains like “gather then craft then build,” but it is still a narrow parser-driven feature rather than a general planner that can synthesize new subtasks from changing world conditions.

### 4) Observation-driven replanning is present but shallow
`ActionOutcome` and `ILlmEvaluator` are a real foundation for replanning. However, the evaluator only runs after at least 3 outcomes, fast-paths away when all outcomes succeeded, and only sees a compact world snapshot plus the last 10 outcomes. That means the loop can notice persistent failure, but it is not yet a robust model of expected-vs-actual outcomes per step.

## New findings from the codebase

### 5) `pendingResponse` handling is inconsistent and can duplicate history/chat behavior
In `HandleChatEventAsync`, `pendingResponse` is recorded into chat history before the switch, and then some branches record it again. The `command` branch also enqueues the command and pushes it to the dashboard, while the outer post-switch block may still enqueue the textual response again.

This is not a catastrophic bug, but it is a source of duplicate conversational artifacts and makes the control flow harder to reason about, especially once the agent starts chaining more frequently.

### 6) `TaskSequenceGoal` is a thin wrapper over a mutable index
`TaskSequenceGoal` is a record-like goal object with an internal `_currentStep` pointer. The class exposes the current step, remaining steps, and `TryAdvance()`, but the sequence completion semantics are very simple: it reports complete only when the index is past the last step. That is fine, but it means sequence advancement must be perfectly synchronized with event handling or the goal can stall in a “current step complete, sequence not complete” limbo.

That makes the sequence feature fragile if any step completes without the expected event path or if a step fails to emit its completion signal.

### 7) Planning still depends on a single execution queue
The main loop in `AgentBackgroundService.DispatchActionsAsync` plans, clears the queue, enqueues the plan, dispatches, and optionally breaks for replanning. That gives you a linear loop with some reactivity, but not a robust planner/executor split. A step that needs a follow-up decision can only get one through the evaluator or through goal completion/failure; there is no richer subgoal execution state.

### 8) World freshness is still represented as a fragile boolean
`WorldState.IsInventoryStale` is still a single flag that is set in some places and cleared in others. The codebase already has enough complexity that a single boolean is too weak to represent state provenance, freshness, or confidence. It can be cleared by one event path and left stale by another, or overwritten by a late event snapshot.

### 9) `WorldState` and its builder are easy to mutate inconsistently
`WorldState` is an immutable record only at the surface. Internally, its `Builder` creates new dictionaries and snapshots, which is fine, but there are still many call sites mutating `Facts`, `Inventory`, and `IsInventoryStale` through different mechanisms. That makes it possible for different subsystems to hold subtly different assumptions about the same state.

### 10) The code still mixes domain state with transport state
`AgentBackgroundService` is doing goal management, planner orchestration, event projection, chat ingestion, dashboard updates, correlation tracking, emergency-stop behavior, inventory freshness gating, and damage interrupts in a single class. That is a classic source of deep bugs because a change in one responsibility can alter another through shared state.

### 11) Command execution is not enforced at runtime
`CommandExecutionEnabled` only influences the LLM prompt. There is no runtime guard in `HandleChatEventAsync` that rejects `intent.Intent == "command"` when command execution is disabled, and there is no schema validation against a command allowlist before dispatching a slash command. That means the safety setting is advisory rather than authoritative.

### 12) Multi-step chains can be silently truncated
The `NextSteps` pipeline in `HandleChatEventAsync` parses the first action plus each remaining string with `IntentManager.ParseCommandString`. Any unrecognized step is dropped without warning, and the code will still create a shorter `TaskSequenceGoal` if at least two valid steps remain. That can change the intended meaning of a chain without surfacing the mismatch to the LLM or the player.

### 13) `AgentBackgroundService` is carrying too many responsibilities
The orchestration service is simultaneously handling event projection, goal lifecycle, replanning, chat interpretation, command dispatch, correlation, dashboard updates, health interrupts, and inventory freshness gates. That coupling is already producing fragile control flow and will become a larger bug source as the agent starts synthesizing more ad hoc action sequences.

## Risk-ranked issues

### P0 — Safety/behavior correctness
1. Command execution is enabled by default while the prompt contract says it should be disabled by default.
2. Slash-command execution is not type-safe or schema-validated.
3. The runtime still depends on mutable queue-and-correlation state inside a single orchestration class.

### P1 — Planning/replanning correctness
4. Replanning is outcome-based but not expectation-based.
5. Multi-step chaining is fixed-pattern parsing, not general planning.
6. Inventory freshness is modeled as a boolean rather than a richer freshness state.

### P2 — Maintainability and future extensibility
7. `AgentBackgroundService` is too large and too coupled.
8. Chat response recording/dispatch has duplicated control flow.
9. `TaskSequenceGoal` is fragile without richer step state and explicit postconditions.

## What to refactor next to make intelligent chaining achievable

### 1) Introduce a first-class planning model
Add explicit types for:
- `Goal`
- `Plan`
- `PlanStep`
- `Action`
- `Precondition`
- `Postcondition`
- `ObservedResult`
- `ReplanTrigger`

The planner should operate on those objects rather than inferring intent from strings and ad hoc goal names.

### 2) Make actions typed and tool-driven
Replace broad slash-command execution with a typed command/tool registry. The LLM should produce either a constrained command object or a tool call against a schema the runtime validates before dispatching.

### 3) Model expected-vs-actual results explicitly
Each action should declare what success looks like. The event loop should compare actual observations against those expectations and invoke replanning when the results diverge, even if the action itself did not technically fail.

### 4) Replace freshness booleans with snapshot/version semantics
Inventory, world state, and goal-relevant facts should carry timestamps, sequence numbers, or freshness versions. That lets the agent reason about stale-but-not-false data instead of treating freshness as a simple flag.

### 5) Split the orchestration class
Move out at least these responsibilities:
- chat interpretation
- goal lifecycle
- event projection
- dispatch/correlation
- replanning policy
- dashboard/reporting

That will make bugs easier to localize and will reduce accidental coupling.

## Recommended verification work
- Add tests for command execution defaults and denied-command filtering.
- Add tests for duplicate chat response/history behavior.
- Add tests for `TaskSequenceGoal` advancement and failure recovery.
- Add tests for replanning when an action completes with the wrong observed outcome.
- Add tests for inventory freshness transitions after spawn, death, mine, craft, and status events.

## Bottom line
The repository is moving in the right direction, but it is not yet at a point where it can reliably and autonomously string together arbitrary multi-step actions under changing conditions. The next meaningful step is not another special-case heuristic; it is a typed plan/result model with validated tool calls and explicit expected outcomes.

