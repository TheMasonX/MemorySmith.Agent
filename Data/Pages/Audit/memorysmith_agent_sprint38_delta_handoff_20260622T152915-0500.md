# MemorySmith.Agent Sprint Handoff Guide — Delta Update
**Timestamp:** 2026-06-22 15:29:15 -0500  
**Branch:** `sprint-35-llm-first`  
**Base handoff:** `Data/Pages/Tasks/agent-handoff-sprint35-llm-first.md`  
**Status:** Updated delta guide after Sprint 38 review and source audit

## Purpose

This guide updates the original Sprint 35 handoff with what actually changed in the branch since that document was written. It keeps the original architectural framing, but focuses on the deltas that matter for Sprint 39 and beyond.

The original handoff established the long-term shape of the system:

- LLM owns intent.
- ToolDispatcher is the execution safety wall.
- WorldStateProjector is the canonical state reducer.
- Deterministic fast-paths are limited to the safe set.
- `ActionOutcome` becomes the shared execution artifact.

Those principles still stand, but the branch is now in a more advanced, partially completed state than the original handoff described.

---

## What changed since the original handoff

### 1) Inventory truth moved from a status snapshot to event-driven updates

The original Sprint 35 handoff framed inventory truth as something the runtime would eventually reconcile through player collection, status snapshots, and projector events. Sprint 38 changes the gather pipeline so `GetStatus` is no longer emitted from `GatherItemDecompose`, which avoids the inventory reset bug caused by status overwriting incremental collection. The new behavior depends on `ItemCollectedEvent` / mined-block event handling and the projector, not a mid-gather status refresh.

Practical effect: gather progress is now less brittle, and inventory accumulation is not wiped by a status refresh at the wrong moment.

### 2) Chat processing is now more LLM-centric, but not fully single-owned

The original handoff already wanted LLM-first intent handling. Sprint 38 continues that direction by removing the legacy intent→goal switch from `ParseDecision` and pushing goal mapping into `IntentManager` when available.

What remains:
- A compatibility path still exists for tests and legacy paths when `IntentManager` is absent.
- The fast-path set is intentionally narrow: cancel, status, inventory, help.
- This is not yet a pure “one brain” architecture in the strictest sense, but it is substantially closer than the original branch.

### 3) Goal correlation was introduced, but current goals still have placeholder identity

Sprint 38 adds `IGoal.Id`, `ActionOutcome.GoalId`, and correlation plumbing in the dispatch loop. The problem is that the concrete goal implementations inspected in this branch still use the default `Guid.Empty` value.

That means the new outcome tracking exists structurally, but it is not yet uniquely identifying real goals. This is the most important unfinished delta because Sprint 39’s observation-driven replanning will depend on it.

### 4) Observation-driven replanning exists as a stub, not a complete loop

The original handoff described `ActionOutcome` as the universal execution artifact and planned for future replanning. Sprint 38 adds `ILlmEvaluator` and starts collecting cycle outcomes, but the evaluator is still a stub and the outcome list is not yet clearly reset at goal boundaries.

This is the right direction, but it is still a scaffold rather than a finished control loop.

### 5) Tool dispatch got stricter and safer

`ToolDispatcher` now validates arguments against schemas, catches tool exceptions, and logs collision warnings when a named registration overwrites an existing one. That is a meaningful hardening of the execution boundary the original handoff was aiming for.

The validator is still intentionally minimal, so it should be treated as a guard rail, not a full JSON Schema engine.

### 6) Test coverage now reflects the Sprint 38 contract changes

The new tests cover:
- removal of `GetStatus` from gather decomposition,
- `BlockMinedEvent` projector facts,
- end-to-end gather completion without `GetStatus`,
- `ILlmEvaluator` existence,
- `ActionOutcome` goal-id wiring,
- schema validation failures,
- unknown tool failures,
- collision warning behavior.

The test suite is moving in the right direction, but one test still risks confusing future readers because the projector test name suggests stale-flag clearing, while the actual stale-flag clearing happens in `AgentBackgroundService`.

---

## Delta summary against the original handoff

| Original handoff area | Current branch state | What changed |
|---|---|---|
| `GetStatus` in gather flow | Removed from `GatherItemDecompose` | Fixes the inventory reset loop |
| Parser creates goals | No longer true in the main path | Goal mapping now flows through `IntentManager` when injected |
| `ActionOutcome` concept | Real record now exists | Used in dispatch loop, but goal identity is still placeholder-based |
| Replanning | Planned, not complete | `ILlmEvaluator` exists as a stub; real evaluation is still Sprint 39 work |
| Tool safety | Intended safety wall | Now enforces schema validation and exception-to-`ToolResult` conversion |
| Tests | Sparse / contract-driving | Expanded with Sprint 38 coverage |

---

## 6-seat LLM council review

### Seat 1 — Source-grounded archivist
The code changes match the sprint summary on the big items: `GetStatus` was removed from gather decomposition, `ToolDispatcher` gained warning-on-collision behavior, and `IntentManager` is now part of the chat-to-goal path. The important caveat is that the new goal identity system is only half-real because the concrete goals still inherit the default `Guid.Empty`.

### Seat 2 — Runtime reliability engineer
The runtime is safer than before, especially at the tool boundary. The remaining reliability issue is not the dispatcher anymore; it is the new per-cycle outcome buffer and goal identity model. Those need to be finished before the evaluator becomes meaningful.

### Seat 3 — Planner / architecture reviewer
The branch is still in a hybrid state. The parser is less responsible than before, but not fully empty. That is acceptable for a transitional sprint, provided the fallback path is treated as compatibility only and removed once tests are updated.

### Seat 4 — Test-focused reviewer
The test suite now does a better job of encoding the new contract. One naming issue remains: a projector test reads like it verifies stale-flag clearing, while the stale-flag clear actually happens in the background service event handler. That is not a production bug, but it is a maintenance hazard.

### Seat 5 — Skeptical reviewer
The biggest hidden risk is not the obvious ones that were fixed. It is the mismatch between the new “goal-aware outcome” architecture and the current implementation of goals. If the goal IDs are all empty, then the new observation loop cannot tell one goal from another. That is a real architectural gap.

### Seat 6 — Synthesizer
Sprint 38 is a successful transition sprint, not a final architecture sprint. The correct read is: the branch has crossed the line from “conceptual redesign” to “partially enforced redesign.” The next sprint should finish identity, boundary cleanup, and evaluator wiring instead of adding more parallel pathways.

---

## Closed items

These should no longer be treated as open questions for Sprint 39 planning:

- `ToolDispatcher.CallAsync` is the main safety boundary for tool execution.
- Double journaling is not the active problem it looked like at first.
- `BlockMinedEvent` projector behavior and inventory stale-flag clearing are split intentionally across projector and runtime handler.
- The legacy chat fallback path is deliberate compatibility behavior, not an accidental hidden production branch.

---

## Remaining open risks

### 1) Goal correlation still needs real identity
`IGoal.Id` defaults to `Guid.Empty`, and the concrete goals checked in this branch do not override it. Sprint 39 should give every real goal an actual stable ID so `ActionOutcome` becomes useful for causal analysis and replanning.

### 2) `_cycleOutcomes` should be boundary-aware
The new per-cycle outcome buffer needs an explicit lifecycle policy. It should be cleared or rotated when a new goal starts, and it should not silently accumulate stale outcomes across goals.

### 3) The LLM navigation path still has a follow-player gap
The current prompt schema does not expose a `target` field, yet the runtime still has a special-case branch for `target == "player"`. In practice, that means the “follow me / come here” style behavior is not well-supported by the production LLM path.

### 4) The parser compatibility path should be retired on schedule
The legacy `TryParseTruncatedJson(..., null)` and null-`IntentManager` paths are acceptable as transitional scaffolding. They should disappear once the test suite no longer depends on them.

### 5) The `IChatInterpreter` return type change is still deferred
The original Sprint 38 plan already deferred the `IntentDraft` surface change. That deferment still makes sense. It just needs a crisp Sprint 39 test update plan so the change does not become a lingering TODO.

---

## Updated sprint handoff guidance for Sprint 39

### Keep
- LLM-first intent ownership.
- ToolDispatcher as the strict execution boundary.
- WorldStateProjector as the sole state reducer.
- Narrow deterministic fast-paths only for safe operations.
- `ActionOutcome` as the shared execution artifact.

### Finish
- Give goals stable IDs.
- Make the per-cycle outcome list lifecycle-safe.
- Wire the evaluator into a real replanning decision.
- Remove the remaining parser compatibility fallback once tests are updated.
- Close the follow-player navigation gap.

### Do not reopen
- The inventory truth fix.
- The tool dispatch validation direction.
- The collision warning on registration.
- The `BlockMinedEvent` stale-flag handling split.

---

## Suggested Sprint 39 acceptance criteria

1. Every concrete goal returns a real non-empty `Guid` from `Id`.
2. `ActionOutcome` records distinct `GoalId` values in the runtime.
3. `_cycleOutcomes` is reset or rotated at goal boundaries.
4. `ILlmEvaluator` is invoked with a meaningful outcome set.
5. The chat path no longer depends on legacy parser fallback in production.
6. Navigation/follow-player behavior is either fully modeled in the LLM prompt or explicitly removed from the supported contract.
7. Test names match the responsibility of the component under test.

---

## Reference notes

This guide updates the original handoff rather than replacing it. Read it alongside the original Sprint 35 document when planning changes that touch:
- intent parsing,
- goal creation,
- action correlation,
- tool execution,
- or replanning.

The original file remains the best source for the intended architecture; this update is the delta view for the current branch state.
