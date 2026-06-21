# MemorySmith.Agent Audit Refinement Report
**Timestamp:** 2026-06-20T133435Z  
**Scope:** sprint-5-tool-safety branch, current repo roadmap, planning layer, background service, and visible test suite.

## Executive summary

The codebase has materially improved since the earlier pass: the background service now has a time abstraction, damage-interrupt logic with cooldowns, structured journaling, action correlation, timeout sweeping, and a best-effort recovery path. Those are the right building blocks for recoverable failures. The remaining risk is that several important failures still collapse into `null`, silent debug output, or one-line warnings, which makes operational errors hard to see and harder to validate in tests. citeturn489835view0turn760407view0turn698782view0turn698782view2

The biggest remaining design issue is that the system still treats some errors as “absence of a result” instead of a first-class outcome. That is true in `GoalFactory` for missing registries and missing data, and again in `HtnPlanner.ReplanAsync`, which ignores its `failureReason` parameter and swallows all exceptions. Those choices reduce visibility, make recovery logic less specific, and leave tests with weak observability signals. citeturn698782view0turn698782view1turn698782view3turn698782view4

The current sprint roadmap already tracks related work such as a `TryInterruptOnDamage` integration test, a `GatherGoalDecomposer` count fix, and a `TimeProvider` abstraction. The audit findings below are intentionally narrower: they focus on error visibility, recovery, and validation mechanics, so they do not duplicate the roadmap items already called out. citeturn240750view4turn240750view0

## Highest-priority findings

### 1) Goal creation failures are still mostly invisible at runtime
**Confidence: 92%**

`GoalFactory.CreateAsync` returns `null` for missing item registries, missing blueprint repositories, missing items, and malformed goal IDs. In the missing-repository cases it emits only `Debug.WriteLine` warnings, which are easy to miss outside a debugger and do not become structured runtime signals. That means a user can ask for a goal, the factory can fail, and the downstream agent loop only sees a generic “could not be created” style fallback. citeturn698782view0turn698782view1

Why this matters:
- Misconfiguration looks identical to “goal unknown”.
- Recovery code cannot distinguish “bad input” from “service unavailable”.
- Tests cannot assert which failure path occurred unless they inspect side effects indirectly. citeturn698782view0turn698782view1

Recommendation:
- Replace nullable goal creation with a rich result type such as `GoalCreationResult` that carries `Success`, `FailureReason`, and a machine-readable diagnostic code.
- Emit a structured journal entry when creation fails due to missing dependencies.
- Keep `null` only for truly unsupported goal names, not for recoverable runtime faults.

### 2) `HtnPlanner.ReplanAsync` throws away the very signal it is given
**Confidence: 96%**

`ReplanAsync` accepts a `failureReason` argument, but the implementation does not use it. It constructs a generic `SimpleGoal`, calls `PlanAsync`, and returns `null` on any exception without surfacing why replanning failed. That makes the method weak for both recovery and testing: the caller provides context, but the planner ignores it. citeturn698782view2turn698782view3turn698782view4

Why this matters:
- Recovery choices cannot vary by failure type.
- The planner cannot distinguish transient route failures from permanent capability gaps.
- Tests cannot prove that replanning responded to the reason it was asked to handle. citeturn698782view3turn698782view4

Recommendation:
- Thread `failureReason` into the replanning policy even if the first version only uses it for logging and metrics.
- Avoid blanket `catch` unless the exception is logged or converted into a structured failure result.
- Consider a `ReplanResult` that includes `Succeeded`, `Plan`, and `FailureReason` rather than returning only `IPlan?`.

### 3) Game error handling is recoverable, but still too batchy and too quiet
**Confidence: 88%**

The background loop only checks `_gameErrors.Reader.TryRead(...)` inside the branch that runs after an action dispatch and settle delay. That means errors are processed only after a dispatched action cycle, not as a continuously drained stream. The current code also reads only one error per cycle, so bursts of errors can lag behind and the recovery path may react more slowly than the underlying fault rate. citeturn489835view0turn760407view0

Why this matters:
- The system can have multiple pending errors while only one is surfaced at a time.
- Recovery latency depends on whether an action was recently dispatched.
- A failing world can appear “stuck” when the queue is actually just not being drained aggressively enough. citeturn489835view0turn760407view0

Recommendation:
- Drain the error channel in a loop until empty, not just once per cycle.
- Consider a small bounded in-memory error buffer with explicit ordering and deduplication.
- Surface the backlog in logs/journal entries so the operator can see whether the agent is recovering, retrying, or falling behind.

### 4) Correlated action completion still relies on tool names, not the correlation IDs that were just introduced
**Confidence: 84%**

The dispatch path now creates a `correlationId` for each action and stores it in the pending-action map, which is good. But the completion and failure helpers still search by tool name and update the first matching dispatched action. That is fragile when the same tool can be in flight more than once, or when late events arrive after a retry or timeout. The system has the right identifier, but not all of the event-routing code uses it yet. citeturn489835view0turn760407view0

Why this matters:
- Duplicate tools can be misattributed.
- Late events can complete the wrong action.
- Validation becomes ambiguous because the state transition is not keyed by the unique identity that was already generated. citeturn489835view0turn760407view0

Recommendation:
- Pass correlation IDs through the event payloads or maintain an explicit action-event mapping.
- Reserve tool-name matching only for legacy fallback paths.
- Add a test where two identical tools are dispatched concurrently and the correct completion/failure target is asserted.

### 5) The strongest behaviors are still under-tested on the negative path
**Confidence: 79%**

The visible service tests cover a lot of happy-path and basic error-channel behavior, but they still rely on polling with `DateTime.UtcNow` and short `Task.Delay` loops to observe state changes. That makes the tests slower, more timing-sensitive, and harder to reason about when a failure happens intermittently. The error-channel tests do verify that a mined-zero `BlockNotFoundEvent` and a generic `ErrorEvent` can abandon a goal, which is good, but the more nuanced recovery cases are still not well proven in the visible suite. citeturn718829view0turn718829view1turn718829view2turn718829view3

Why this matters:
- Polling-based tests tend to hide race conditions.
- They are harder to extend to corner cases like bursts, duplicate events, and timeout races.
- The most important regressions in this system are likely to be timing or recovery regressions, so the tests should make those failures cheap to reproduce. citeturn718829view0turn718829view1

Recommendation:
- Push more behavior behind deterministic seams: clock, queue, adapter, and recovery strategy.
- Add unit-level assertions for the reason codes and recovery decisions before relying on full service integration tests.
- Create a focused test matrix for: missing dependencies, invalid goal names, repeated errors, duplicate tool dispatches, and timeouts.

## Architectural opportunities

### A. Promote “failure” to a first-class domain object
The repeated `null`-on-failure pattern is the biggest obstacle to visibility. A richer result object would let the agent distinguish unsupported input, missing configuration, missing data, transient adapter failures, and policy decisions. That would also let tests assert the exact failure mode instead of inferring it from side effects. citeturn698782view0turn698782view1turn698782view3turn698782view4

### B. Make recovery decisions traceable
`TryRecoverFromGameErrorAsync` already exists and the service journals error-recovery attempts, which is a good base. The next step is to make recovery decisions explain themselves: which error was seen, which action was taken, why that action was chosen, and whether recovery was skipped because it was already attempted for the same goal. citeturn489835view0turn760407view0

### C. Separate “operator-visible error” from “internal diagnostic”
A debug log is useful for developers, but it is not enough for runtime observability. Use structured journal entries, explicit failure reasons, and surfaced status fields so the UI and tests can tell the difference between “not yet attempted”, “attempted and failed”, and “recovered but downgraded”. citeturn698782view0turn698782view1turn489835view0

### D. Prefer deterministic test seams over real-time waits
The service already has a time provider in the live code path, which is the right direction. Tests should lean into that seam and avoid real wall-clock polling wherever possible. That will make recovery, timeout, and cooldown behavior much easier to validate. citeturn489835view0turn760407view0

### E. Keep backlog and codebase state in sync
The roadmap still lists `TimeProvider` as an upcoming sprint item, while the current service already uses one. That suggests the documentation backlog is at least partially stale, or the roadmap item was not reconciled after implementation. It is worth aligning the roadmap with the actual branch state so future audits do not duplicate completed work. This is an inference from the roadmap versus current code, not a direct claim about process quality. citeturn240750view4turn489835view0

## What already looks better than the previous pass

The service now has a concrete time abstraction, structured damage-interrupt handling, action timeout sweeping, and a best-effort recovery path. Those are exactly the kinds of foundations that make future testing easier. The audit concern is not that these features are missing; it is that several failure exits still do not surface enough detail to make them easy to validate or recover from cleanly. citeturn489835view0turn760407view0

## Assumptions

- I reviewed the sprint-5-tool-safety branch and the public roadmap as they existed during this inspection; later commits may change the conclusions. citeturn951625view0turn240750view4
- I treated the GitHub HTML views and the raw file contents as the source of truth for implementation details. citeturn698782view0turn698782view2turn489835view0
- I assumed the goal of this pass is to improve recoverability and validation, not to re-litigate tasks already explicitly queued in the roadmap. citeturn240750view4

## Open questions

1. Should goal creation failures be user-visible in the UI, or only journaled for operators?
2. Should recovery from game errors be allowed to mutate the current goal automatically, or should it only suggest a replacement?
3. Should the correlation ID be added to world events at the adapter boundary, or should the agent maintain a stronger tool-to-action sequencing model internally?
4. Is the roadmap item for `TimeProvider` stale, or is there a second codepath still using wall-clock time that was not visible in the branch snapshot? citeturn240750view4turn489835view0

## Suggested next-step priorities

1. Replace nullable goal creation with a structured result and a surfaced diagnostic path. citeturn698782view0turn698782view1
2. Teach `ReplanAsync` to use `failureReason` and to report failure reasons explicitly. citeturn698782view3turn698782view4
3. Drain and record game errors more aggressively so recovery is observable under load. citeturn489835view0turn760407view0
4. Finish correlation-ID-based action completion so repeated tools cannot cross-wire state. citeturn489835view0turn760407view0
5. Add deterministic tests around these cases before expanding the sprint scope. citeturn718829view0turn718829view1
