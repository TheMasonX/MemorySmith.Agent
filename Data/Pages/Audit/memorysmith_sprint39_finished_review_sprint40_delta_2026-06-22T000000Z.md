# MemorySmith.Agent — Sprint 39 Finished Review and Sprint 40 Delta

Branch: `sprint-35-llm-first`  
HEAD reviewed: `4bf9850a`  
Focus: delta only, with still-valid findings carried forward from the prior audit.

## Executive summary

Sprint 39’s second half materially improved the architecture: the dispatch loop now actually calls the evaluator after each structured `ActionOutcome`, and the `Program.cs` wiring clearly shows the new chat-routing, planner, tool, and runtime seams being composed together. The hot path now includes structured outcomes and an evaluator decision point instead of a TODO comment, which is the right shape for observation-driven replanning. fileciteturn52file0L195-L207

The biggest remaining risks are not around the new features themselves, but around boundary cleanup: a few old partial-refactor seams still exist, and they matter more now that the evaluator is live. The same old `IntentManager` split is still partly duplicated in the background service, `Guid.Empty` is still used as a placeholder goal identity in the outcome stream, and `_cycleOutcomes` is still only cleared on some lifecycle edges. Those are now live-coupling risks, not just style issues. fileciteturn46file0L167-L196 fileciteturn52file0L170-L206 fileciteturn32file0L3-L12 fileciteturn40file0L14-L20 fileciteturn42file0L19-L26

## Still-valid findings from the prior audit

### 1) Runtime metadata is still stale

`Program.cs` still advertises `v0.37.0` in the file header, and `/api/about` still reports `Version = "0.37.0"` with `Phase = "Sprint 36 — AgentRuntime + Observation-Driven Replanning"`. That is now out of date relative to a finished Sprint 39, and it will keep confusing anyone checking the running app, dashboard, or deployment status. fileciteturn54file0L3-L4 fileciteturn55file0L83-L92

Recommendation: bump the visible version/phase strings as part of the Sprint 40 handoff, so the code and runtime metadata agree again.

### 2) `IntentManager` is still split across layers

The parser-side seam is cleaner than before, but it is still not fully centralized. `IntentManager` owns gather/build/craft goal-name creation, while `AgentBackgroundService.HandleChatEventAsync` still directly handles navigation, follow-player fallback, response emission, and `MoveTo` enqueues. That means the intent-to-action semantics are still split between the service and the manager. fileciteturn22file0L39-L86 fileciteturn46file0L167-L266

This is workable, but it is not yet a single semantic authority. It will become harder to reason about once Sprint 40 starts moving loops out into manager classes.

Recommendation: either move navigation into the same semantic routing layer as gather/build/craft, or explicitly document that navigation is intentionally a direct service-level action and will stay that way.

### 3) `LlmChatInterpreter` still carries an unused `IntentManager` dependency

`LlmChatInterpreter` still accepts `IntentManager? intentManager` in its constructor, stores it in `_intentManager`, and then never uses it anywhere in the file. At the same time, the actual goal routing now happens later in `AgentBackgroundService`, not in the parser. That makes the constructor parameter look like leftover refactor debris rather than a live dependency. fileciteturn23file0L44-L58 fileciteturn46file0L167-L196

Recommendation: remove that constructor argument if the parser is no longer responsible for goal routing, or move the routing responsibility fully into a dedicated semantic layer so the dependency is real.

### 4) Goal correlation still collapses to `Guid.Empty` outside an active goal

The dispatch loop still calls `CallWithOutcomeAsync(_currentGoal?.Id ?? Guid.Empty, ...)`. That means any action dispatched outside a real active goal, or during a transitional gap, is stamped with a placeholder identity instead of a meaningful goal identifier. Now that `_cycleOutcomes` is an actual input to the evaluator, this is no longer harmless. fileciteturn52file0L170-L206

Recommendation: either exclude non-goal outcomes from the evaluator stream or give them a separate, explicit lifecycle category so `Guid.Empty` does not masquerade as a real goal.

### 5) `_cycleOutcomes` lifecycle is still asymmetric

`_cycleOutcomes` is cleared on `SetGoal()` and when a new plan is generated, but not on goal completion or cancellation. The service now consumes that queue in the dispatch loop, so stale outcomes can survive a goal transition until the next `SetGoal()` call happens to overwrite them. fileciteturn32file0L3-L12 fileciteturn40file0L14-L20 fileciteturn42file0L19-L26

Recommendation: clear `_cycleOutcomes` on cancel and completion as well, or make the evaluator consume a scoped snapshot that cannot outlive the active goal.

## New findings from the finished sprint

### 6) `GetStatusTool` is registered twice as two separate instances

`Program.cs` now registers `new GetStatusTool(world)` twice: once under its canonical tool name and once again under the `Status` alias. Because the tool appears stateless, this is probably not a user-facing bug today, but it is still wasteful and makes aliasing semantics less obvious than they need to be. It also makes future stateful tool changes riskier. fileciteturn54file0L205-L223

Recommendation: instantiate once, then alias the same instance under both names, or explicitly document why two instances are acceptable.

### 7) The evaluator bridge is real now, so evaluator telemetry needs to be equally real

The good news is that the earlier TODO is gone: `DispatchActionsAsync` now enqueues the structured outcome, calls the evaluator, and breaks the loop when the evaluator says to replan. That is the right control point. fileciteturn52file0L195-L207

The remaining gap is observability: once the evaluator is in the hot loop, its skip reason, false-negative behavior, and any offline fallback become first-class runtime behavior. If those decisions are opaque, you will not be able to tell whether the bot is holding a plan too long, replanning too eagerly, or silently defaulting to the wrong answer.

Recommendation: add structured logging or journal entries for evaluator skip reasons, provider-offline fallback, and final replan decisions so you can diagnose the control loop from production logs.

## What is stronger now

The overall intent pipeline is much healthier than it was in the first audit. `IntentDraft` is now a proper semantic record in `Agent.Core`, `IntentManager` owns goal-request construction, and `ChatInterpreter` has been reduced to deterministic fast-paths for the safe commands. That is a much cleaner separation than the earlier mixed parser/goal-creation path. fileciteturn27file0L24-L52 fileciteturn22file0L39-L86 fileciteturn56file0L188-L204 fileciteturn57file0L5-L65

`ToolDispatcher` is also materially better than before: it now validates schema-bound arguments and exposes structured `ActionOutcome` handling through `CallWithOutcomeAsync`, which is exactly the sort of boundary hardening that makes a future LLM-driven runtime more survivable. fileciteturn50file0L90-L196

## Sprint 40 delta recommendations

1. Make the evaluator loop fully lifecycle-safe: clear `_cycleOutcomes` on cancel and completion, and stop emitting `Guid.Empty` into the goal-tracking stream.
2. Remove the stale `IntentManager` dependency from `LlmChatInterpreter` unless it is going to be used again for real routing.
3. Deduplicate `GetStatusTool` registration so aliases share one instance.
4. Centralize navigation semantics or explicitly fence them off as a direct service-level special case.
5. Update `Program.cs` version/phase metadata before the next handoff.
6. Add one integration test that proves `DispatchActionsAsync` → evaluator → replanning behaves correctly with a real goal, real outcomes, and a non-empty goal id.
7. Add evaluator-facing telemetry so provider-offline and parse-fallback decisions are visible in logs.

## Bottom line

Sprint 39 finished in a much better place than the first half: the evaluator is wired, the dispatch loop now owns a real decision boundary, and the chat/intents path is cleaner. The remaining Sprint 40 work should focus less on adding more behavior and more on collapsing the last inconsistent seams so the new control loop cannot be confused by stale lifecycle state, placeholder goal IDs, or split semantic routing.
