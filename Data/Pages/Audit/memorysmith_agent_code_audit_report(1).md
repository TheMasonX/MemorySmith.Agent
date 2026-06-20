# MemorySmith.Agent Code Audit Report

**Scope:** Latest commit on `sprint-5-tool-safety` / PR #1, with a forward-looking read on the active sprint plan and the immediate upcoming sprint items.  
**Method:** I compared the branch plan, task backlog, roadmap, and the implementation in the current branch tip. I treated sprint notes as hypotheses and only counted them as complete when I could verify the code path.

## Executive summary

This branch shows meaningful progress toward a safer tool-execution model, a richer world model, and better planner extensibility. The strongest positive signal is that the repo already has a clear roadmap and task inventory, which makes it possible to audit for duplication and drift. The strongest negative signal is that several sprint claims appear ahead of the implementation: the most important one is tool argument validation. The current `ToolDispatcher` still contains a TODO to validate input against `InputSchema` before dispatch, yet it executes the tool immediately after a registry lookup. That is a real seam failure, not a cosmetic gap. The same pattern appears in other places: the sprint plan promises capped world facts, stronger replanning preservation, and controlled adapter shutdown behavior, but the current code excerpts do not fully show those guarantees being enforced. citeturn980875view0turn721575view0turn208860view0

My overall read is that the codebase is moving in the right direction structurally, but the current implementation still mixes high-value domain logic with orchestration in a few deep modules. The next highest leverage work is to deepen the right module interfaces, tighten the tool execution seam, and make failure-handling paths observable rather than silently absorbed. That aligns with the architecture skill’s emphasis on finding a real seam versus a hypothetical one, testing at the module interface, and deleting shallow tests after the module is deepened. citeturn649057search1turn649057search12turn649057search14

## Highest-priority findings

### 1) Tool execution is not actually enforcing the input contract
**Confidence: 95%**

The sprint plan states that `ToolDispatcher.CallAsync` validates arguments against `InputSchema` before execution. In the code, `ToolDispatcher` still has a `// TODO: validate arguments against tool.InputSchema before dispatching` comment and then directly calls `tool.ExecuteAsync(arguments, cancellationToken)`. That means the dispatcher is still trusting the caller’s JSON payload to reach the tool implementation intact. For a tool-safety sprint, this is the most important gap because the module interface is still porous at the exact seam that should be hardened. citeturn721575view0turn980875view0

**Why it matters:** invalid shape, unexpected properties, and maliciously crafted payloads can reach deeper code paths. Even if individual tools perform their own checks, the dispatcher is the correct leverage point because it normalizes the interface for the whole tool surface. The architecture skill explicitly prefers stronger module interfaces and one seam, not many ad hoc checks. citeturn649057search1turn649057search14

**Recommended direction:** move schema validation into the dispatcher as the first hard gate; make rejected payloads return a structured validation failure before any tool logic runs; then delete redundant shallow validation elsewhere once the dispatcher interface is trusted. That is the kind of test-surface deepening the skill recommends. citeturn649057search12

### 2) Replanning preserves only a narrow slice of context and hides failure causes
**Confidence: 88%**

`HtnPlanner.ReplanAsync` rebuilds the plan from `currentPlan.Phases`, then selectively preserves only context keys whose names start with a small hard-coded prefix list such as `SearchMemory:`, `CraftItem:`, `FindFlatArea:`, `Build:`, and `MoveTo:`. That is better than a full reset, but it is still a fragile heuristic: any context outside those prefixes is dropped, and any future goal type that needs durable state must be added by hand. The method also accepts `failureReason` and `originalGoal` parameters but does not use them in the visible implementation, and it catches all exceptions and returns `null` without surfacing the cause. citeturn670094view1

**Why it matters:** replanning is the kind of deep module where preserving the wrong abstraction is costly. The current version encodes planner-specific recovery policy in string prefixes, which is low-leverage and hard to extend. It also makes debugging hard because failed replans disappear into a silent `null`. citeturn670094view1turn649057search14

**Recommended direction:** replace prefix-based preservation with typed carry-forward data on the goal or action model; make `failureReason` part of the actual replanning decision; and log or surface replan failures so the caller can distinguish “no valid plan” from “planner threw.”

### 3) The world model appears richer than before, but the Sprint 5 claim of a capped fact store was not evidenced in the inspected code
**Confidence: 72%**

The sprint plan says world facts are capped at 1000 and carry a `Source` field. In the inspected `WorldStateProjector`, I found structured handling for health, position, movement, status, and raw fact storage for many events, but I did not find evidence in the reviewed files that a 1000-fact cap is being enforced. The models folder contains `WorldState.cs`, but the visible repository listings and the projector excerpts do not show the cap or a `Fact` record being used there. citeturn208860view0turn104234view4turn471643view0turn471643view1turn471643view2

**Why it matters:** uncapped fact growth is a memory and locality problem. The module will accumulate stale observations, which makes debugging and planner prompts noisier over time. If the cap exists elsewhere, it needs to be verified directly; if it does not, this is a useful next-sprint cleanup item rather than a mere omission. citeturn208860view0turn104234view4

**Recommended direction:** make the cap explicit in the `WorldState` module, define retention policy in one place, and expose a stable query surface for “recent facts” versus “canonical state.”

### 4) Shutdown handling is less robust than the sprint note suggests
**Confidence: 83%**

The sprint note says the Minecraft adapter should handle `SIGTERM` by waiting five seconds and then escalating to `SIGKILL`. The inspected adapter shutdown path logs shutdown, calls `bot.quit?.()`, closes the websocket, and then exits the process. I did not see the documented five-second grace period or a kill escalation in the reviewed code. citeturn721575view0turn104234view12turn104234view13

**Why it matters:** graceful shutdown is a reliability seam. Without a timed escalation path, the process may terminate too early or too late depending on how the runtime and adapter respond. That is especially important for long-running agent loops that manage network state and external process state. citeturn721575view0turn104234view12

**Recommended direction:** implement a deterministic shutdown sequence with a bounded wait, explicit cancellation propagation, and a final forced-exit path. Keep the adapter module responsible for its own exit contract rather than relying on ambient process behavior.

## Secondary findings and codebase health opportunities

### A) The current architecture still has a few shallow seams that should become real seams
**Confidence: 86%**

The repo already shows a pattern of dispatcher, planner, projector, and adapter modules, which is a good foundation. The next improvement is to make each seam harder and more explicit: the dispatcher should own schema validation; the planner should own typed goal decomposition and typed carry-forward state; the world model should own retention and observation normalization; and the adapter should own transport lifecycle and shutdown rules. That increases leverage and locality, and it reduces the amount of “glue logic” spread across unrelated modules. citeturn980875view0turn670094view1turn104234view4turn104234view12turn649057search1

### B) Error handling is still too optimistic in the planner path
**Confidence: 84%**

Returning `null` from replanning after a catch-all exception is simple, but it conflates “no plan exists” with “the planner crashed.” Those are different states and should not share the same return channel. A stronger interface would use a discriminated result or explicit failure record so the caller can decide whether to retry, degrade, or stop. citeturn670094view1

### C) Several sprint claims should be revalidated before they are treated as done
**Confidence: 79%**

The roadmap and PR notes describe completed or in-progress work such as input validation, fact caps, action timeouts, and shutdown hardening. At least one of those claims is contradicted by the implementation excerpt, and others were not directly verified in the reviewed files. Before planning the next sprint, the team should treat the sprint notes as a checklist to verify, not as proof of completion. citeturn721575view0turn208860view0turn980875view0turn104234view12

### D) There is a likely opportunity to delete shallow tests after the module interface is deepened
**Confidence: 81%**

Once the dispatcher’s validation and the planner’s replanning contract are moved into stronger module interfaces, tests that only restate shallow behavior at lower layers will become redundant. The architecture skill explicitly recommends deleting those once the deeper interface is tested, because they add maintenance cost without protecting the real seam. citeturn649057search12

## Duplication check against the active sprint plan

I compared the branch plan and roadmap against the code paths I reviewed. The only item that should be treated as a possible duplication risk is if the next sprint repeats the same work already promised in Sprint 5: dispatcher validation, action timeout enforcement, shutdown hardening, and world-model retention. Those are not safe to re-plan as new work until the current branch actually proves them in code. The upcoming Sprint 24 items in the roadmap are narrower and look more like follow-on hardening: a `TryInterruptOnDamage` integration test, a `GatherGoalDecomposer` pass-through fix, a `TimeProvider` abstraction, and an `IWorldObservationGateway` design note. citeturn208860view0

## Assumptions

I assumed the branch tip on `sprint-5-tool-safety` is the intended audit target because the PR and roadmap both indicate the branch is still active and has moved well beyond the PR title. I also assumed the roadmap is the authoritative sprint sequence unless the implementation clearly contradicts it. Where I could not verify a claimed feature directly in code, I treated it as unproven rather than complete. citeturn721575view0turn208860view0

## Open questions

Is `WorldState.cs` enforcing the 1000-fact cap and `Source` metadata, or is that still pending? Is `/api/agent/command` locking tool names down through a separate path not yet inspected, or is that protection still partial? Does the timeout logic in `DispatchActionsAsync` cover all action execution paths, including retries and replans, or only the main happy path? citeturn471643view0turn471643view1turn471643view2turn980875view0turn721575view0

## Recommended next moves

1. Make schema validation a hard dispatcher guarantee and test it at the dispatcher interface.  
2. Replace prefix-based replanning context with typed carry-forward state and explicit failure reporting.  
3. Verify and, if needed, implement fact retention limits in `WorldState`.  
4. Hard-test shutdown behavior under SIGTERM and confirm escalation behavior.  
5. Remove or rewrite any shallow tests that only duplicate behavior now owned by deeper module interfaces.  

## Evidence index

Primary sources reviewed: repository root, PR #1 discussion and sprint notes, roadmap, task list, `ToolDispatcher`, `HtnPlanner`, `AgentBackgroundService`, `WorldStateProjector`, `Program.cs`, and the mineflayer adapter shutdown path. Architecture guidance sources: Matt Pocock’s codebase-architecture skill and deepening guidance. citeturn953730view0turn721575view0turn208860view0turn438379view0turn980875view0turn670094view1turn670094view2turn104234view4turn104234view10turn104234view12turn649057search1turn649057search12turn649057search14
