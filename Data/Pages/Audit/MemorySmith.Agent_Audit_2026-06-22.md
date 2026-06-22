# MemorySmith.Agent Audit — 2026-06-22

## Scope

I reviewed the branch and the sprint-36 handoff state against the actual repository files that were visible through the connector, with emphasis on:

- `WebUI.Blazor/AgentBackgroundService.cs`
- `Agent.Tools/ToolDispatcher.cs`
- `Agent.Core/Interfaces/IAgentJournal.cs`
- `Agent.Core/Models/ActionOutcome.cs`
- `Agent.Core/WorldStateProjector.cs`
- `Agent.Planning/LlmChatInterpreter.cs`
- `Agent.Planning/HtnTaskLibrary.cs`
- `WebUI.Blazor/Program.cs`
- `MemorySmith.Agent.Tests/Sprint36Tests.cs`
- the sprint-36 handoff document

I did **not** run the full build or test suite locally, so anything that depends on execution remains a code-review finding rather than a verified runtime failure.

## Executive summary

The direction is good: the codebase is moving toward an LLM-first control plane with deterministic tools doing the grunt work. The main problem is that several sprint-36 improvements are still **partially wired** or **architecturally incomplete**, which means the code has the shape of the next system but not the behavior yet.

The most important issues are:

1. The new tool-name prompt injection exists in `LlmChatInterpreter`, but `Program.cs` still does not pass registered tool names into it, so the LLM does not actually get the extra context yet.
2. The new `ActionOutcome` / `CallWithOutcomeAsync` path exists, but the main dispatch loop still calls `CallAsync`, so observation-driven replanning and structured outcomes are not actually active.
3. `ToolDispatcher.All` exposes tool instances, not registration names. That means the suggested `dispatcher.All.Select(t => t.Name)` wiring would lose alias names such as `Status`, and the order is nondeterministic because it comes from `ConcurrentDictionary.Values`.
4. The parser still maps LLM JSON directly into goal names inside `LlmChatInterpreter.ParseDecision`, which is a lingering version of the brittle behavior the handoff says you are trying to move away from.
5. `ApplyItemCrafted` is wired, but `ItemConsumedEvent` is still a stub. Inventory state is therefore optimistic and can temporarily overcount until a status refresh reconciles it.
6. The new tests cover the damage interrupt and build retry gate, but the two highest-value missing tests called out in the handoff (`CallWithOutcomeAsync` and `ItemCraftedEvent` inventory updates) are still absent.

## Findings

### 1) The tool-name prompt enrichment is implemented, but it is not wired into DI yet
**Severity:** High

`LlmChatInterpreter` now accepts `registeredToolNames` and appends `Registered tools: ...` to the system prompt. That logic is real. But `WebUI.Blazor/Program.cs` still constructs `LlmChatInterpreter` without passing any tool list, so the prompt enrichment is inert at runtime.

Why this matters: the whole point of the change is to make the LLM more aware of the available deterministic tools. Right now, the code still behaves like the old system.

Recommended fix:
- Pass the registered tools into the interpreter constructor.
- Prefer a stable, explicit registration-name list rather than tool instance names.
- Sort the names before injecting them so the prompt does not vary across runs.

### 2) The suggested tool-name pipeline loses alias names and is nondeterministic
**Severity:** High

`ToolDispatcher.All` returns `_tools.Values`, which means:
- the order is not stable, because it is backed by a `ConcurrentDictionary`
- alias keys are lost, because only values are exposed
- an alias like `Status` can disappear even though the runtime accepts it

That creates a subtle mismatch between what the runtime can execute and what the LLM is told is available.

Recommended fix:
- expose registration metadata, not just tool instances
- preserve aliases if they matter to planning or prompt quality
- sort the exported tool names before building the prompt

### 3) `CallWithOutcomeAsync` is not yet used by the dispatch loop
**Severity:** High

`AgentBackgroundService.DispatchActionsAsync` still calls:

```csharp
var result = await toolCaller.CallAsync(...)
```

There is no `CallWithOutcomeAsync` usage in the current dispatch path.

Why this matters:
- the new observation-driven replanning path is not actually live
- the journal does not receive structured `ActionOutcome` records from runtime actions yet
- the code currently has the new abstraction, but not the behavior the abstraction is supposed to enable

Recommended fix:
- wire `DispatchActionsAsync` to `CallWithOutcomeAsync`
- decide whether `ActionOutcome` is the canonical journal event or just an auxiliary artifact
- thread the outcome into any future replan-evaluation step

### 4) `CallWithOutcomeAsync` will duplicate journal entries if adopted as-is
**Severity:** Medium-High

`CallAsync` already writes a journal entry for success/failure. Then `CallWithOutcomeAsync` calls `_journal?.LogOutcome(outcome)`, and the default `LogOutcome` implementation also writes an `ActionCompleted` or `ActionFailed` entry.

So once the new method is wired in, each action will produce at least two journal records for the same tool event unless the older logging is removed or re-scoped.

Recommended fix:
- choose one journal source of truth for action completion/failure
- either make `CallAsync` a raw transport call with no journal side effects, or make `CallWithOutcomeAsync` a pure wrapper that does not cause a second log
- if both are kept, give them distinct event semantics

### 5) The parser still creates goal names from structured chat output
**Severity:** High

The sprint handoff says parsers should never create goals, but `LlmChatInterpreter.ParseDecision` still maps parsed intent fields into `goalName` and `parameters` for `gather`, `craft`, `build`, and `navigate`.

That is exactly the kind of tight coupling and brittle parsing the LLM-first refactor is trying to reduce. It also keeps goal creation logic inside the parser instead of moving it into a dedicated intent-to-goal layer.

Recommended fix:
- move the `intent -> goal` mapping out of the parser
- make the interpreter return a pure intent draft
- let a dedicated goal/intent manager translate intent into goal objects
- keep the parser focused on interpretation, not action synthesis

### 6) `ItemCraftedEvent` inventory wiring is only half complete
**Severity:** Medium-High

`WorldStateProjector.ApplyItemCrafted` now adds the crafted item to inventory, which is good. But `ItemConsumedEvent` is still stored as facts only, and the projector comment explicitly says full wiring is deferred.

This means inventory is optimistic:
- crafted output can appear before ingredient removal is modeled
- any logic reading the projected inventory can temporarily overestimate what the bot really has
- the next `StatusEvent` becomes the reconciliation point

Recommended fix:
- wire `ItemConsumedEvent` in the same sprint as the crafted output update, or
- explicitly document that inventory is provisional between craft and status reconciliation
- add tests covering the combined craft/consume/status flow

### 7) The sprint-36 tests still miss the two most important pending cases
**Severity:** Medium

The handoff already calls out missing tests for:
- `CallWithOutcomeAsync_Success_ReturnsOutcomeAndLogsToJournal`
- `CallWithOutcomeAsync_ToolFailure_ReturnsFailedOutcome`
- `ItemCraftedEvent_UpdatesInventory`

Those are still absent from the visible test file.

Recommended fix:
- add direct tests for `CallWithOutcomeAsync`
- add a projector test for `ItemCraftedEvent`
- add one end-to-end test that shows `ActionOutcome` actually contributes to planner replanning once the runtime is wired

### 8) `/api/about` is still stale
**Severity:** Medium

`WebUI.Blazor/Program.cs` still reports:

- `Version = "0.28.0"`
- `Phase = "Sprint 33 — Build verify, DI logger wiring, base64 sweep, Rule E-2"`

That does not match the sprint-36 handoff target of `v0.36.0`.

Recommended fix:
- bump the version metadata
- update the phase text to reflect the current sprint and architecture

### 9) The current design still has some silent coupling between adapter events and state projection
**Severity:** Medium

`WorldStateProjector` now handles `ItemCollectedEvent`, `ItemCraftedEvent`, and `StatusEvent`, which is good. But the overall world model still relies on a mix of:
- optimistic event updates
- periodic status reconciliation
- direct inventory mutation from specific event types

That is workable, but it needs a sharper contract. Right now, a reader has to infer which events are authoritative and which are only diagnostic.

Recommended fix:
- document which events are authoritative for inventory, position, and health
- separate “observed facts” from “confirmed state”
- consider tagging projection updates as provisional vs confirmed

### 10) The Htn build retry gate is sensible, but it depends on the event fact being present
**Severity:** Low-Medium

`HtnTaskLibrary.DecomposeBuild` now checks `event:FlatAreaFound:SearchedRadius` and stops retrying at radius `48`. That avoids an infinite retry loop, which is good.

The edge case is that the logic depends on the `SearchedRadius` fact being present and correctly populated. If the adapter or projector contract changes, the retry gate could silently revert to the old behavior.

Recommended fix:
- keep the projector and decomposer contract tightly tested
- add a regression test for missing or malformed `SearchedRadius`
- consider storing the scan radius in a dedicated typed fact structure rather than a stringly-typed fact key

## What is already improved

A few parts are clearly moving in the right direction:

- `TryInterruptOnDamageAsync` now makes the stop-first ordering explicit and awaited.
- `ActionOutcome` is a better abstraction than “tool succeeded/failed” alone.
- `WorldStateProjector` now distinguishes collected drops from mined blocks, which is the right move for Mineflayer accuracy.
- `HtnTaskLibrary` no longer lets the flat-area scan retry loop spin forever.
- The tests show a healthier focus on regression-proofing the brittle parts.

## Assumptions

- The repo state is the sprint-35-llm-first branch at the handoff’s referenced head.
- The files visible through the connector are representative of the current code state.
- The implementation goal is to make LLM interpretation primary, while keeping deterministic tools for execution and verification.
- The audit is focused on correctness, architecture, and maintainability, not on style preferences.

## Open questions

1. Should the prompt receive registration keys, tool instance names, or both?
2. Should aliases such as `Status` be surfaced to the LLM at all, or only kept for backward compatibility?
3. Is `ActionOutcome` meant to replace `JournalEntry` for action execution, or just augment it?
4. Should `ActionOutcome` carry structured effects from the adapter layer, or is success/failure sufficient for now?
5. Should the parser still emit goal names in this sprint, or is that now considered technical debt to remove immediately?
6. Should crafted output and ingredient consumption be modeled in the same sprint so inventory never drifts optimistic for long?
7. Should tool export order be deterministic everywhere that the LLM sees it?

## Recommended next steps

1. Wire `Program.cs` to pass a deterministic, alias-aware tool list into `LlmChatInterpreter`.
2. Remove duplicated journal logging before switching dispatch to `CallWithOutcomeAsync`.
3. Move goal creation out of `LlmChatInterpreter.ParseDecision`.
4. Finish `ItemConsumedEvent` wiring and add the missing projector test.
5. Add the missing `CallWithOutcomeAsync` tests.
6. Bump `/api/about` to the sprint-36 version.
7. Re-run the full CI suite before council review.
