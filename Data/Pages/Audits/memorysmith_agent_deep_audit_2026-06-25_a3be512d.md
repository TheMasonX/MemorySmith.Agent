# MemorySmith.Agent Deep Code Audit

**Repository:** TheMasonX/MemorySmith.Agent  
**Branch:** `sprint-35-llm-first`  
**Commit:** `a3be512d916ea6b03a9573da6c716866ce1286e1`  
**Audit date:** 2026-06-25

> **Sprint 49 Wave D annotations (2026-06-25):**
> - Finding 2 (Tool outcomes): ✅ **Implemented** via TSK-0110 — added `OutcomeType` to `ToolResult`, updated `CallWithOutcomeAsync` mapping.
> - Finding 3 (Damage interrupt): ✅ **Implemented** via TSK-0119 — stop callback wrapped in try/catch, queue clear guaranteed.
> - Finding 4 (Version drift): ✅ **Implemented** — README, roadmap, Program.cs header, and `/api/about` all synced to v0.50.0 / Sprint 49.
> - Finding 5 (Logger wiring): ✅ **Implemented** via TSK-0120 — `ILogger<ToolDispatcher>` passed in `Program.cs`.
> - Finding 1 (Build origin): ⏳ **Deferred** (TSK-0107) — requires invasive refactor of `DecomposeBuild` signature.

## Executive summary

This commit is a narrow but important hardening pass. It closes two concrete concurrency risks in the action pipeline and documents the remaining deferred tasks. The bigger story is that the codebase is still carrying a few contract mismatches that will keep generating edge-case bugs unless they are normalized at the abstraction level.

The strongest findings are:

1. **Build origin handling still conflates “missing origin” with the real coordinate `(0,0,0)`**. That makes legitimate builds at world origin ambiguous and can force fallback behavior when the origin is actually valid.  
2. **Tool outcome semantics remain lossy at the dispatch boundary**. The code already has a rich `ActionOutcome` model, but the current mapping still collapses many distinct states into `Completed` or `Failed`, which weakens observation-driven replanning and diagnostics.  
3. **Damage interrupt flow depends on the stop callback succeeding before the queue is cleared**. That is an understandable ordering choice, but it makes the highest-priority recovery path fragile if WebSocket send fails.  
4. **Documentation/version drift is now obvious enough to be operationally misleading**. README, roadmap, and `/api/about` disagree on the current version/sprint state.

### Overall confidence

| Area | Confidence | Why |
|---|---:|---|
| Build-origin sentinel bug | 96% | Directly visible in `HtnPlanner` and `HtnTaskLibrary` |
| Tool outcome lossy mapping | 91% | Directly visible in `ToolDispatcher`, `ActionOutcome`, and dispatch loop |
| Damage interrupt fragility | 82% | Directly visible in `ActionQueue.ClearAndEnqueueAsync` and its call site |
| Docs/version drift | 98% | Directly visible in README, roadmap, and `/api/about` |
| WebSocket shutdown verification gap | 55% | Commit patch says it is fixed, but the fetched file snapshot still showed the pre-fix terminal path |

## What changed in this commit

The commit message and diff show two changes:

- **TSK-0113:** `ActionQueue` moved from mixed `ConcurrentQueue` / lock coverage to a single lock around every operation.
- **TSK-0112:** `WebSocketBridge.RunReceiveLoopWithRetryAsync` is intended to complete the inbound channel on all terminal paths.

Evidence: commit diff summary and task handoff note in `Data/Pages/Handoffs/sprint-49-wavec-dashboard-wave1-plus.md`, plus the task JSONs for TSK-0112 and TSK-0113.

## Findings

### 1) Build origin sentinel collision
**Severity:** High  
**Confidence:** 96%

`HtnPlanner.ReadOriginFact(...)` returns `0` whenever the fact is missing or unparsable, and `HtnTaskLibrary.DecomposeBuild(...)` treats the triple `(0,0,0)` as “no origin set” and auto-resolves it. That means a legitimate build at world origin cannot be represented unambiguously.

Why it matters:
- A real build location at `(0,0,0)` will be treated as missing.
- The code may trigger `ResolveAutoOrigin(...)` or `FindFlatArea` when the user actually supplied a valid origin.
- This is exactly the kind of silent contract bug that survives tests until an uncommon coordinate appears.

Evidence:
- `Agent.Planning/HtnPlanner.cs` reads origin facts with `return 0` on missing values.
- `Agent.Planning/HtnTaskLibrary.cs` checks `if (originX == 0 && originY == 0 && originZ == 0)` before auto-origin logic.
- The roadmap already lists “Build Origin” as an area of prior sprint work, which makes this a known hotspot rather than a new surprise.

Recommendation:
- Replace the sentinel triple with an explicit nullable origin value object, e.g. `BuildOrigin?`.
- Preserve the distinction between “unset” and “set to zero.”
- Add tests for a blueprint intentionally anchored at `(0,0,0)`.

### 2) Tool outcomes are still being flattened too early
**Severity:** Medium-High  
**Confidence:** 91%

The architecture already has a rich `ActionOutcome` model with `Completed`, `NoProgress`, `Blocked`, `Unreachable`, and `TimedOut`. But `ToolDispatcher.CallWithOutcomeAsync(...)` still maps the raw `ToolResult` to only `Succeeded(...)` or `Failed(...)`. That collapses the semantic signal before the planner / evaluator can use it.

Why it matters:
- Observation-driven replanning loses important distinctions.
- A “blocked” action and a “hard failure” look the same downstream.
- Diagnostics become noisier because the logs know more than the planner does.

Evidence:
- `Agent.Core/Models/ActionOutcome.cs` defines a richer enum and factory methods.
- `Agent.Tools/ToolDispatcher.cs` maps `result.Success ? ActionOutcome.Succeeded(...) : ActionOutcome.Failed(...)`.
- `WebUI.Blazor/AgentBackgroundService.cs` logs `_journal?.LogOutcome(outcome)` immediately after dispatch, so any lossy mapping is baked into the journal and evaluator input.

Recommendation:
- Promote the richer states all the way through the tool execution contract.
- Make `ToolResult` or the dispatcher carry explicit outcome metadata, not just a boolean.
- Map known cases like timeout, unreachable, and blocked prerequisites to the matching `OutcomeType`.

### 3) Damage interrupt is correct in intent, but too coupled to stop delivery
**Severity:** Medium  
**Confidence:** 82%

`TryInterruptOnDamageAsync(...)` uses `ActionQueue.ClearAndEnqueueAsync(...)`, which awaits the stop callback before acquiring the queue lock and clearing the plan. That preserves the “stop first, then clear” ordering, but it also means a transient send failure or cancellation on the stop path prevents the queue clear entirely.

Why it matters:
- The bot may keep running stale actions precisely when it most needs to stop.
- A WebSocket hiccup during emergency handling becomes a plan-safety failure.
- The highest-priority recovery path now depends on best-effort network delivery.

Evidence:
- `AgentBackgroundService.TryInterruptOnDamageAsync(...)` passes `worldAdapter.SendActionAsync(...)` as the stop callback.
- `ActionQueue.ClearAndEnqueueAsync(...)` awaits that callback before locking and mutating the queue.

Recommendation:
- Keep “stop first” ordering, but separate side effects from queue mutation.
- Use a best-effort stop send with logging, then guarantee the queue clear/enqueue in a `finally` path.
- Add a test that simulates stop send failure and verifies the priority `GetStatus` still replaces stale work.

### 4) The project has major documentation/version drift
**Severity:** Medium  
**Confidence:** 98%

There are multiple version markers that disagree:
- `README.md` says **v0.49.0** and “Sprint 49 complete.”
- `Data/Pages/roadmap.md` says **Current version: v0.40.0** and “Latest: Sprint 41 (in progress).”
- `WebUI.Blazor/Program.cs` returns `/api/about` version **0.46.0** and “Sprint 46 — Tightening the Contracts.”

Why it matters:
- Operators cannot trust the roadmap at a glance.
- Future audits may duplicate already-completed work because the docs disagree about what is current.
- This is especially risky in a sprint-driven repo where the docs are part of the workflow.

Evidence:
- README feature block and roadmap block disagree.
- `/api/about` in `Program.cs` reports a different version/phase again.

Recommendation:
- Centralize version and sprint metadata in one source of truth.
- Generate README/roadmap/about output from that single source.
- Add a lightweight CI check that fails on drift between the source of truth and rendered docs.

### 5) Duplicate-registration diagnostics are currently muted
**Severity:** Low  
**Confidence:** 70%

`ToolDispatcher.Register(string name, ITool tool)` has a warning path for overwrites, but the dispatcher is currently constructed as `new ToolDispatcher(journal)` in `Program.cs`, so `_logger` is null. That means the duplicate-registration warning cannot actually emit.

Why it matters:
- Alias registration is intentional, but accidental overwrite bugs become harder to spot.
- This is a small observability gap rather than a correctness bug.

Evidence:
- `Agent.Tools/ToolDispatcher.cs` has optional `ILogger<ToolDispatcher>? logger`.
- `WebUI.Blazor/Program.cs` creates `ToolDispatcher` without passing a logger.

Recommendation:
- Pass a logger into the dispatcher constructor.
- Keep the overwrite warning, because alias collisions are exactly the kind of regressions that sneak in during refactors.

## Existing sprint / task coverage to avoid duplication

Do not duplicate work that is already complete in this commit or explicitly deferred in the handoff:

- **TSK-0112** and **TSK-0113** are the two tasks implemented here.  
- **TSK-0107** remains deferred: build-origin cleanup and the `(0,0,0)` overload problem.  
- **TSK-0110** remains deferred: structured tool outcomes / preservation of `ActionOutcome` semantics.

The roadmap also shows the next broad priorities:
- LLM model upgrade
- `goto()` timeout safety
- structured chat classification
- dashboard event bus
- `IBuildGoal` marker interface
- semantic build locations
- world KB deployment verification
- configurable agent responses

That means the next sprint should not reinvent build origin handling or outcome semantics from scratch; those are already known work items.

## Implementation guidance

1. **Introduce an explicit build origin type**  
   Use a nullable value object instead of sentinel coordinates. This is the cleanest fix for the build-origin ambiguity.

2. **Lift outcome semantics into the tool contract**  
   Keep `ActionOutcome` as the journal/evaluator artifact, but stop collapsing `ToolResult` into a boolean too early.

3. **Decouple emergency stop delivery from queue mutation**  
   Treat stop signaling as best-effort and queue clearing as guaranteed.

4. **Unify version metadata**  
   One source of truth, then generate README/roadmap/about from it.

5. **Wire logging into dispatcher construction**  
   This is a small change with good diagnostic payoff.

## Assumptions and open questions

### Assumptions
- The supplied commit SHA is the intended audit target.
- The branch snapshot and the commit diff may not have been perfectly synchronized by the connector, so I treated the commit diff as authoritative for the change intent and called out the verification gap where needed.

### Open questions
- Is the `WebSocketBridge` finally-completion patch fully present in the built tree, or only in the commit diff metadata? The fetched file snapshot still showed the pre-fix terminal-path behavior.
- Should `ActionOutcome` be expanded further, or should the underlying `ToolResult` contract become the primary carrier of structured status?
- Is the roadmap intentionally stale as a historical log, or is it meant to reflect current state? If it is current-state documentation, it needs automated sync.

## Evidence index

- `README.md`
- `Data/Pages/roadmap.md`
- `Data/Pages/Handoffs/sprint-49-wavec-dashboard-wave1-plus.md`
- `WebUI.Blazor/Program.cs`
- `Agent.Planning/HtnPlanner.cs`
- `Agent.Planning/HtnTaskLibrary.cs`
- `Agent.Core/Models/ActionQueue.cs`
- `Agent.Core/Models/ActionOutcome.cs`
- `Agent.Tools/ToolDispatcher.cs`
- `WebUI.Blazor/AgentBackgroundService.cs`
- `Data/Tasks/tsk-0112-fix-websocket-clean-shutdown-complete-inbound-channel-on-all-terminal-paths.json`
- `Data/Tasks/tsk-0113-fix-actionqueue-race-prone-surface-area-add-lock-protection-to-all-operations.json`
