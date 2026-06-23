
# MemorySmith.Agent Revised Sprint Audit
## Sprint 38 complete review — branch `sprint-35-llm-first` — HEAD `eaccd5d1`

**Timestamp (UTC):** 20260622T200908Z

## Executive summary

Sprint 38 is a real step forward. The codebase now has a much clearer separation between intent parsing, execution safety, world projection, and observation-driven replanning.

The strongest wins are:
- `ActionOutcome` now implements `IObservationSummary`, giving the runtime an actual observation artifact to feed into future evaluation loops.
- `DispatchActionsAsync` now calls `CallWithOutcomeAsync`, and the dispatcher no longer double-logs success/failure journal entries.
- `IntentManager` now owns intent-to-goal mapping, which is the correct Principle-1 separation. `LlmChatInterpreter` delegates to it when present.
- The Mineflayer adapter now emits authoritative item pickup events via `playerCollect`, clears inventory drift on craft and smelt with status refreshes, and protects the chat handler with defensive try/catch.
- `WorldStateProjector` now treats `ItemCollectedEvent` as the authoritative inventory source, and it also supports crafted and consumed items.

The remaining work is no longer “make the bot smarter” in the abstract. It is now about closing the loop:

intent → goal → action → observation → evaluation → replanning

The biggest remaining risks are:
1. goal identity is still effectively provisional unless every concrete goal overrides `IGoal.Id`, because the interface default is still `Guid.Empty`;
2. the evaluation loop is still only a stub, even though the contract exists;
3. `LlmChatInterpreter` still retains backward-compatibility paths for test/legacy use, which is acceptable for now but should be removed in the next round once the test surface is updated;
4. the adapter is better, but it still relies on status snapshots in a few places where direct event emission would be richer and less brittle.

## What Sprint 38 got right

### 1) Observation contracts are now real
`ActionOutcome` is no longer just a success/failure result. It now exposes `IObservationSummary.Summary`, which means a future evaluator can reason over structured outcomes directly. That is the exact seam needed for observation-driven replanning.

### 2) Dispatch now carries outcomes, not just tool results
`ToolDispatcher.CallWithOutcomeAsync` now returns a structured `ActionOutcome`, and `DispatchActionsAsync` logs and accumulates those outcomes explicitly. The old double-logging path is gone, which was the right fix.

### 3) Principle-1 is now enforced at the right layer
`LlmChatInterpreter` now delegates goal mapping to `IntentManager`, and its prompt includes the richer intent schema with confidence and clarification support. The parser is moving toward intent assessment rather than goal creation.

### 4) Inventory truth is much better than before
The adapter now emits `itemCollected` from Mineflayer `playerCollect`, guarded so it only fires for the bot's own pickups. `WorldStateProjector.ApplyItemCollected` normalizes the item name and updates inventory from that event. That is the right authoritative source for mined drops such as diamond rather than diamond ore.

### 5) The gather pipeline no longer depends on a stale refresh step
`HtnTaskLibrary` removed `GetStatus` from `GatherItemDecompose`, and the background service now clears inventory-stale state on `BlockMinedEvent`, so gather can progress from direct mining events instead of waiting for a status refresh that might never come.

### 6) The adapter is safer operationally
The chat handler now has a defensive try/catch, which means a malformed or unexpected chat event should not crash the adapter process. That is a meaningful reliability win.

## Remaining findings and recommendations

### A) Goal identity is still too weak
`IGoal.Id` currently defaults to `Guid.Empty`. The background service now passes `_currentGoal?.Id ?? Guid.Empty` into `CallWithOutcomeAsync`, so any goal that does not override `Id` will collapse observation correlation.

Recommendation: make non-empty goal identity unavoidable. Either:
- enforce `Id` in a base goal class, or
- validate on `SetGoal` and reject any goal that still returns `Guid.Empty`, or
- assign a unique ID in the goal factory/runtime when the goal is created.

This is the most important remaining technical gap because the observation stream is only useful if it can be tied to a real goal instance.

### B) The observation loop exists, but the evaluator is still a stub
`ILlmEvaluator` has been introduced, and `DispatchActionsAsync` now accumulates `_cycleOutcomes`, but the actual `EvaluateAsync` call is still a Sprint 39 TODO.

Recommendation: wire the evaluator as soon as possible, but keep it narrowly scoped:
- input: active goal + recent `ActionOutcome[]`
- output: continue / replan / clarify / abandon
- no execution side effects inside the evaluator itself

### C) The chat interpreter still needs one more cleanup pass
The parser now delegates goal mapping correctly, but `ParseDecision` and `TryParseTruncatedJson` still contain fallback logic for legacy/test paths when `IntentManager` is absent. That is fine for transition, but it should be treated as temporary.

Recommendation: Sprint 39 should remove the remaining fallback mapping once the test suite is updated to inject `IntentManager` everywhere.

### D) AgentBackgroundService is still a god file
The service now owns connection handling, event processing, damage interrupts, chat handling, goal completion, replanning, outcome accumulation, and evaluation stub wiring. The Sprint 38 changes made it more capable, but not smaller.

Recommendation: keep the current structure until the evaluator is working, then split by responsibility:
- intent and chat interpretation
- planning and replanning
- action dispatch
- world-state update and projection
- recovery and interruption
- dashboard and telemetry

### E) Craft and smelt still rely on snapshot refreshes
The adapter still ends both `craft` and `smelt` by calling `sendBotStatus()`. That is useful as reconciliation, but it is still a snapshot-based pattern, not a fine-grained event stream.

Recommendation: expose dedicated events for:
- crafted output
- ingredient consumption
- smelting input consumption
- smelting output creation

`WorldStateProjector` already has `ItemCraftedEvent` and `ItemConsumedEvent` support, so the adapter should eventually emit those directly instead of relying on snapshot reconciliation for everything.

### F) `FindFlatArea` is much better, but should keep proving its origin provenance
The adapter now supports scan origins, ground checks, proximity weighting, and direct `findFlatArea` completion events. The runtime also auto-sets build origin from qualifying scans and falls back after repeated zero-area results. That is a strong improvement, but it should continue to treat origin provenance explicitly.

Recommendation: keep build-origin source explicit in facts or structured events so the planner and evaluator can distinguish:
- user-specified origin
- scan-derived origin
- fallback-at-bot-position origin

## Mineflayer adapter changes still worth making

The adapter is already much stronger, but I would still recommend these additions:

### 1) Expose pickup, craft, and smelt as structured item-flow events
Right now pickup is excellent because it is authoritative. Craft and smelt should reach that same level of directness. The projector already knows how to consume `ItemCraftedEvent` and `ItemConsumedEvent`; the adapter should emit those when it can, instead of treating status snapshots as the only reliable reconciliation step.

### 2) Surface more pathfinding state
Mineflayer pathfinder already provides goal lifecycle and movement state. The adapter should expose:
- goal set / goal reached
- path updated / reset / stopped
- “is mining”, “is building”, and movement-related reason codes if available
- path failure summaries and repeated-replan suppression conditions

This matters because the LLM should not just know that movement failed; it should know whether it failed because the bot was stuck, rerouted, interrupted, or never had a viable path.

### 3) Surface reachability and feasibility facts
For mining/building, the adapter should include or query:
- whether the target block is visible
- whether it is diggable
- approximate dig time
- whether a placement face is valid
- whether the current location is actually navigable

These are excellent pre-flight observations for the evaluator before it decides to continue a plan.

### 4) Surface inventory deltas, not only snapshots
Keep snapshots for reconciliation, but add deltas for:
- slot changes
- held item changes
- equipment changes
- inventory window transitions
- item drop / pickup / consumption deltas

That will make the state projector and evaluator much more precise.

### 5) Keep the chat guardrails
The chat handler try/catch is the right call. Keep filtering system messages at the adapter boundary so the LLM does not waste cycles on teleport confirmations, join/leave notifications, or server chatter.

## Recommended TSK task system task outlines

### Goal identity hardening
Define a single, non-optional way to assign a unique ID to every goal instance. Update goal creation so no active goal can exist with an empty identifier. Add tests proving outcomes remain associated with the correct goal across multiple cycles and cancellations.

### Observation-driven replanning
Implement the `ILlmEvaluator` runtime path. Feed it a compact list of recent `ActionOutcome` items plus the active goal. Make it decide between continue, replan, clarify, or abandon. Keep execution side effects out of the evaluator.

### Adapter event enrichment for item flow
Add direct adapter events for crafted output, ingredient consumption, and smelting output/input transitions. Use `playerCollect` as the authoritative pickup event, and keep `sendBotStatus()` only for reconciliation.

### Pathfinding and feasibility telemetry
Expose path lifecycle transitions and path failure causes from Mineflayer pathfinder. Add reachability and diggability facts so the evaluator can see whether a planned action is actually feasible before it retries or reroutes.

### Inventory delta projection
Expand the adapter and projector to prefer deltas over snapshots where possible. Add slot-change, held-item-change, and equipment-change events so inventory state stops depending on periodic refreshes alone.

### Intent confirmation gate
Finish the `IntentAssessment` path by making confidence and clarification questions affect runtime behavior. Ambiguous or high-risk instructions should ask for confirmation rather than creating goals immediately.

### Chat interpreter final cleanup
Remove the remaining legacy goal-mapping fallback once test coverage has been updated to inject `IntentManager` everywhere. Keep deterministic fast-paths only for truly zero-risk commands.

### Runtime decomposition follow-up
After the evaluator is live, split `AgentBackgroundService` into smaller orchestration services by responsibility. That should make future work safer and reduce the chance that a single file keeps absorbing planning, recovery, and telemetry concerns.

### Test hardening for observation logic
Add tests that prove:
- goal IDs are non-empty and stable
- action outcomes are associated with the right goal
- item collection updates inventory from drop names, not mined block names
- craft/smelt inventory deltas are correct
- zero-area flat scans do not cause infinite retry loops
- chat handler exceptions are contained

## Bottom line

Sprint 38 successfully moved MemorySmith.Agent from “LLM-assisted control” toward a real agent architecture. The core seams are now in the right places. The next leap is to make those seams do actual reasoning work:
- real goal IDs,
- real evaluator wiring,
- richer adapter observations,
- and fewer snapshot-based fallbacks.

That is the point where the system stops just reacting to commands and starts meaningfully adapting to what it observes.
