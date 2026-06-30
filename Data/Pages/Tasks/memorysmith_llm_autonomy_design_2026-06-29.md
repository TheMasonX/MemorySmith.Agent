# MemorySmith.Agent — LLM Autonomy Beyond Chat
## Design doc, roadmap, and implementation plan

**Baseline commit:** `88c7848af2a8819ebe6ce928bb1428fd2102cee0`  
**Scope:** Add LLM-driven interaction patterns outside of user chat while preserving the current deterministic-first runtime and the existing `AgentBackgroundService` loop.

---

## 1) Executive summary

The current agent already supports several prerequisites for autonomy:

- `IntentDraft.NextSteps` exists for multi-step commands.
- `TaskSequenceGoal` exists for sequential execution.
- `IMemoryGateway` is wired into the runtime for cross-session recall.
- `ActionOutcome` and `_cycleOutcomes` already exist for observation-driven replanning.
- `ITimeProvider` exists, which makes timer-driven wakeups testable.
- `AgentBackgroundService` already owns the loop and the action correlation model.

This means the next step is **not** “build a second agent loop.”  
The next step is to add a small, explicit **runtime signal layer** that can wake the existing loop for reasons other than chat.

Two new autonomy paths are the best fit:

1. **Scheduled wakeups** — the agent wakes on a timer, evaluates state, and decides whether to act.
2. **Completion follow-ups** — when a goal or action completes, the agent can request another LLM pass to continue, branch, or stop.

The core design principle is: **all autonomy re-enters the same planning pipeline**.  
Timers, callbacks, and chained steps should become inputs to the existing runtime, not side channels.

---

## 2) Current state in the repo

The repo already contains the pieces needed for this direction:

- `IntentDraft` includes `NextSteps`, which is the current multi-step hook.
- `TaskSequenceGoal` wraps multiple `IGoal`s and advances sequentially.
- `AgentBackgroundService` already maintains:
  - `_correlatedActions`
  - `_cycleOutcomes`
  - `_lastReplanAt`
  - `ITimeProvider`
  - `ILlmEvaluator`
  - `IMemoryGateway`
- `AgentRuntime` is documented as the future host for a decomposed runtime loop.
- `architecture.md` already frames the agent as a single-host, event-driven runtime with deterministic goal decomposition and LLM-assisted chat interpretation.

That baseline comes from the commit handoff and the current architecture docs, so the design below should extend those seams instead of replacing them.

---

## 3) Problem statement

Chat-only interaction is limiting because it forces the user to be present for every decision point.

Examples:

- “Wake up in five minutes and check whether the furnace finished smelting.”
- “After you finish gathering wood, tell the LLM to decide whether to craft planks or continue chopping.”
- “Run this task, and if it fails because materials are missing, re-enter the planner and chain the follow-up work.”

Today, some of this is partially possible, but it is fragmented:

- Multi-step commands are only represented in the initial parse.
- Completion currently ends the action/goal unless some other event causes a replan.
- Timer-driven behavior is not a first-class runtime concept.
- There is no explicit “notify me on completion and ask again” policy.

The missing abstraction is **re-entry**.

---

## 4) Goals and non-goals

### Goals

- Wake the agent on a timer and let the LLM decide whether to act.
- Allow a goal, intent, or action to request follow-up evaluation after completion.
- Preserve the existing deterministic-first runtime and action correlation.
- Keep the design testable with fake time.
- Support bounded chaining without runaway loops.
- Make wake reasons observable in logs and dashboard output.
- Keep chat, timer, and completion all flowing through one runtime entrypoint.

### Non-goals

- Do not create a second autonomous planner service.
- Do not allow arbitrary recursive self-invocation of the LLM.
- Do not make every action completion automatically re-run the model.
- Do not replace `TaskSequenceGoal`; extend it.
- Do not require a persistent queue/broker for the first version.

---

## 5) Proposed design

### 5.1 Add a runtime signal model

Introduce a small set of explicit runtime signals that can wake the agent:

- `ChatReceived`
- `ActionCompleted`
- `GoalCompleted`
- `TimerDue`
- `ExternalCommand`
- `RecoveryNeeded`

All of them should resolve to the same internal path:

1. capture signal,
2. update runtime state,
3. evaluate whether a new plan is needed,
4. if yes, run the LLM/planner pipeline,
5. dispatch the next action batch.

### 5.2 Make “wake” a first-class concept

Add a `WakeReason` to the runtime, for example:

- `Chat`
- `Timer`
- `GoalCompleted`
- `ActionCompleted`
- `Recovery`
- `Manual`

The runtime should record why it woke up, because different wake reasons produce different evaluation behavior:

- Chat wakeups should interpret intent first.
- Timer wakeups should inspect context and then ask the LLM whether anything should happen.
- Completion wakeups should decide whether to continue, chain, or stop.
- Recovery wakeups should prioritize repairing the current goal.

### 5.3 Introduce a follow-up policy

Add a follow-up policy to the intent/goal layer, not to the transport layer.

Recommended shape:

- `None`
- `OnGoalComplete`
- `OnActionComplete`
- `OnTimer`
- `OnFailure`
- `ChainNextSteps`

This can live in a new `IntentAssessment` / `GoalRequest` extension or as a new small struct attached to `IntentDraft`.

The policy should answer:

- Should the runtime schedule another pass?
- Should it do so immediately or at a delay?
- How many times is the chain allowed to continue?
- Should it continue only if the previous step was successful?

### 5.4 Preserve the existing sequence model

`TaskSequenceGoal` should remain the main mechanism for commands that are explicitly multi-step at parse time.

Use it for:
- “gather wood then build a house”
- “craft planks and then craft a table”
- “smelt ore, then craft tools”

Use a completion follow-up when:
- the next step is not known until the previous step finishes,
- the LLM should inspect the outcome before choosing,
- the agent should pause and wait for time or state changes.

In other words:

- `TaskSequenceGoal` = known sequence
- follow-up policy = conditional sequence continuation

### 5.5 Make timer wakeups declarative

Timers should not directly call the planner.

Instead:

1. a timer service emits `TimerDue`,
2. the runtime receives it as a signal,
3. the runtime asks whether a wake is still relevant,
4. the planner/LLM decides the next action.

This allows:

- cancellation if the world changed,
- deduplication if multiple timers fire,
- testability with fake time,
- centralized logging of wake reasons.

### 5.6 Completion re-entry should be outcome-driven

When a goal or action completes, the runtime should emit a completion signal that includes:

- correlation ID,
- goal ID,
- tool name,
- success/failure,
- summary,
- effects,
- timestamp.

That is already very close to the existing `ActionOutcome` pattern. The completion signal should be a higher-level wrapper around that data.

The LLM evaluator should then receive:

- current goal,
- current world state,
- accumulated action outcomes,
- follow-up policy,
- any pending `NextSteps`,
- any timer context.

The evaluator returns one of:

- stop,
- continue same goal,
- advance sequence,
- create follow-up goal,
- schedule next wake,
- recover/replan.

---

## 6) Suggested types and interfaces

These are suggested shapes, not a requirement to use these exact names.

### Runtime-facing

- `IRuntimeSignal`
- `RuntimeSignalType`
- `RuntimeWakeReason`
- `RuntimeSignalEnvelope`
- `IRuntimeSignalSink`
- `IRuntimeWakeScheduler`

### Intent / goal-facing

- `IntentAssessment`
- `GoalRequestContinuation`
- `FollowUpPolicy`
- `WakePolicy`
- `ContinuationLimit`

### Scheduling / persistence

- `IScheduledWakeStore`
- `ScheduledWake`
- `WakeScheduleEntry`
- `IClock` or reuse `ITimeProvider`
- `IAutonomyHistory`

### Evaluation

- `ILlmEvaluator.EvaluateAsync(...)` should be expanded so it can distinguish:
  - timer-driven check,
  - completion-driven check,
  - explicit chat turn.

---

## 7) Runtime flow

### 7.1 Chat flow
1. User chat arrives.
2. Chat interpreter returns `IntentDraft`.
3. Intent manager converts it into a goal request.
4. Runtime sets the goal or sequence.
5. The loop dispatches actions.
6. Completion signals can optionally re-enter planning.

### 7.2 Timer flow
1. Scheduler reaches due time.
2. Runtime emits `TimerDue`.
3. Runtime asks whether the wake is still relevant.
4. If yes, LLM evaluator inspects current state.
5. The result may be:
   - no-op,
   - a single action,
   - a replan,
   - a follow-up goal.

### 7.3 Completion flow
1. Action completes and correlation resolves.
2. Outcome is appended to cycle history.
3. If the goal says follow-up is desired, runtime emits `GoalCompleted` or `ActionCompleted`.
4. LLM evaluator gets a fresh pass.
5. The LLM decides whether to:
   - continue,
   - branch,
   - schedule another wake,
   - stop.

---

## 8) Guardrails

This feature needs strict limits or it will become noisy.

### Required guardrails

- Maximum chain depth.
- Maximum wake frequency per goal.
- Cooldown between timer-triggered evaluations.
- Deduplication of identical wake reasons.
- No wake if the world state has not meaningfully changed.
- Separate handling for success, failure, and timeout.
- Logs for every suppressed wake.

### Strong recommendation

Use named boolean gates for wake eligibility, the same way the new style guidance prefers named locals for complex conditions. That makes each cause of a wake independently reviewable.

---

## 9) Observability requirements

Every wake should log:

- wake reason,
- goal ID,
- correlation ID if present,
- whether the wake was accepted or suppressed,
- suppression reason,
- whether the LLM was actually called,
- what follow-up action was chosen.

This should surface in:

- structured logs,
- dashboard status,
- journal entries,
- test assertions.

A silent wake suppression is a bug.

---

## 10) Persistence model

For the first version, keep persistence minimal:

- scheduled wakes can live in-memory,
- follow-up policy can be stored on the current goal/runtime state,
- sequence state can remain in `TaskSequenceGoal`,
- cross-session memory can be used only for facts, not as the scheduler itself.

Later versions can persist scheduled wakes if the agent needs restart-resilience.

Recommended order:

1. in-memory schedule,
2. optional durable schedule store,
3. replay on startup,
4. dashboard UI for pending wakes.

---

## 11) Roadmap

### Phase 0 — Contract and docs
**Outcome:** Define the vocabulary before touching runtime behavior.

Deliverables:
- `WakeReason`
- `RuntimeSignal`
- `FollowUpPolicy`
- update architecture docs
- update AGENTS / task docs with the new autonomy model
- confirm naming and lifecycle rules

Exit criteria:
- team agrees on what a wake is,
- team agrees on what can trigger it,
- team agrees on suppression rules.

### Phase 1 — Internal signal plumbing
**Outcome:** All non-chat triggers can enter the same runtime path.

Deliverables:
- signal envelope type
- runtime signal sink
- timer emitter using `ITimeProvider`
- wake logging
- wake deduplication
- suppression cooldowns

Exit criteria:
- timer wakes can be simulated in tests,
- a timer wake can reach the planner,
- no duplicated planning on repeated timer ticks.

### Phase 2 — Completion follow-up
**Outcome:** Completion events can request another LLM pass.

Deliverables:
- completion signal type
- follow-up policy attached to goal/intent
- evaluator can request continuation
- sequence continuation limits
- outcome-aware follow-up logging

Exit criteria:
- a goal can explicitly ask for “evaluate again when done”,
- a successful completion can cause a second planning pass,
- failure can choose between retry, stop, or branch.

### Phase 3 — Chaining and branching
**Outcome:** The agent can continue work after the first task finishes.

Deliverables:
- `TaskSequenceGoal` enhancement or companion continuation goal
- support for “do X, then ask whether to do Y”
- branch decisions based on `ActionOutcome`
- partial-result chaining

Exit criteria:
- “gather wood, then build” still works,
- “gather wood, then decide what to do” works,
- chain depth limits are enforced.

### Phase 4 — Persistence and restart resilience
**Outcome:** Autonomy survives process restarts if needed.

Deliverables:
- optional scheduled wake store
- wake replay on startup
- stale wake cleanup
- expiry semantics
- metrics for missed wakes

Exit criteria:
- the runtime can restart without silently losing scheduled wake intent,
- expired wakes are discarded safely.

### Phase 5 — Dashboard and operator controls
**Outcome:** Operators can see and control autonomous behavior.

Deliverables:
- pending wakes list
- follow-up policy display
- manual wake trigger
- cancel wake
- “pause autonomy” toggle
- reason for last suppressed wake

Exit criteria:
- operators can understand why the agent woke up,
- operators can stop chain behavior without killing the bot.

---

## 12) Implementation plan by file area

### Core runtime
- `WebUI.Blazor/AgentBackgroundService.cs`
  - add runtime signal handling
  - add wake dispatch path
  - add completion re-entry path
  - preserve existing action correlation and cooldowns

### Core models
- `Agent.Core/Models/IntentDraft.cs`
  - keep `NextSteps`
  - optionally add follow-up metadata or move it into a new assessment type
- `Agent.Core/Models/ActionOutcome.cs`
  - expand only if needed for wake policies or branch hints
- `Agent.Core/Runtime/AgentRuntime.cs`
  - add signal/scheduler interfaces to the runtime composition

### Planning
- `Agent.Planning/IntentManager.cs`
  - parse chained or follow-up-capable intents
  - convert follow-up policy into goal requests
- `Agent.Planning/LlmChatInterpreter.cs`
  - teach the prompt about optional follow-up requests and timer-friendly outputs
- `Agent.Planning/Goals/*`
  - sequence and continuation goals
  - keep sequence max-depth guards

### Observability
- `IAgentJournal`
  - add wake-related entries if needed
- dashboard / REST
  - show scheduled wakes, last wake reason, and suppressed wake reasons

### Tests
- unit tests for wake eligibility
- unit tests for cooldown/suppression
- integration tests for completion re-entry
- integration tests for timer wakeups
- regression tests for runaway chain prevention

---

## 13) Risks and mitigations

### Risk: runaway loops
**Mitigation:** max chain depth, cooldowns, deduplication, and suppression logs.

### Risk: duplicate work after completion
**Mitigation:** correlation IDs, idempotent follow-up checks, and outcome-aware wake deduplication.

### Risk: timer noise
**Mitigation:** timer wakes should be sparse and stateful, not a constant polling loop.

### Risk: confusing operator behavior
**Mitigation:** surface wake reasons in the dashboard and journal.

### Risk: architecture drift
**Mitigation:** keep all wake paths funneled through the same runtime entrypoint.

---

## 14) Acceptance criteria

The feature is ready when:

- the agent can wake on a timer and choose to act,
- a goal can request re-evaluation after completion,
- chained work continues safely without recursion,
- suppression and dedup reasons are visible,
- tests cover the normal path, the no-op path, and the duplicate-prevention path.

---

## 15) Recommended next task slice

The smallest useful slice is:

1. define `RuntimeSignal` / `WakeReason`,
2. wire timer wakeups into `AgentBackgroundService`,
3. add follow-up policy to the goal/intent contract,
4. add one LLM re-entry path after completion,
5. cover it with tests and logging.

That gives you the new behavior without overbuilding the scheduler.
