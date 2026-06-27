# MemorySmith.Agent Deep Audit Report
**Branch:** `sprint-35-llm-first`  
**Commit:** `d2ef16ab86d433cc62912939c213cde088dcaf05`  
**Date:** 2026-06-26  
**Scope:** Whole-codebase audit with emphasis on in-progress runtime wiring, planning, and operational hardening.

## Executive summary

The latest commit is a real step toward consolidating legacy behavior, but it is not yet a clean end-state. The most important issue is that the new “context wiring” path is only partially implemented: the dispatcher now merges `ActionData.Context` into `ActionData.Arguments`, but the current producer path does not actually populate coordinates in the way the task card describes, and the merge is broad enough to create hidden coupling across tools. `MoveToTool` still requires explicit `x/y/z` arguments, while `SearchMemoryTool` currently returns search results only and no coordinate payload. fileciteturn7file0 fileciteturn27file0 fileciteturn30file0 fileciteturn18file0

The second major issue is operational hardening around the new SQLite telemetry sink. The commit adds the sink and suppresses `NU1903` at the project level, but I do not see startup fallback, explicit failure isolation, or new tests for lock/unavailable behavior. That leaves a greenfield codebase carrying a permanent vulnerability suppression with no scoped guardrail. fileciteturn8file0 fileciteturn13file0 fileciteturn23file0

The third issue is process drift: the sprint handoff says Wave D is complete, but the task cards for both `TSK-0004` and `TSK-0014` still show `Status: Open`. That makes it easy to duplicate work, misread sprint health, or assume the feature set is farther along than it is. fileciteturn24file0 fileciteturn18file0 fileciteturn23file0

## Priority findings

| Priority | Finding | Why it matters | Confidence | Evidence |
|---|---|---:|---:|---|
| P0 | Context wiring is incomplete and overly permissive | The new merge path consumes context, but the current producer path does not populate coordinates end-to-end, and the merge can silently leak unrelated context values into downstream tool inputs. | 95% | fileciteturn7file0 fileciteturn27file0 fileciteturn30file0 fileciteturn18file0 |
| P1 | SQLite telemetry sink lacks visible failure isolation and test coverage | The sink is enabled in startup config and the task acceptance criteria explicitly call for crash-free behavior when the DB is locked/unavailable, but the commit shows no defensive path or new tests. | 90% | fileciteturn8file0 fileciteturn12file0 fileciteturn23file0 |
| P1 | Project-wide `NU1903` suppression is too broad for a greenfield codebase | A project-level suppression will mask future SQLite-related security warnings beyond the intended sink dependency. | 88% | fileciteturn13file0 |
| P2 | Task/status metadata drift is already present | The handoff marks Wave D done, but the task records still say Open, which weakens sprint traceability and invites duplicate work. | 98% | fileciteturn24file0 fileciteturn18file0 fileciteturn23file0 |

## Detailed findings

### 1) Context carry is only half built, and the dispatcher-level merge is too coarse
`ActionData` explicitly defines a shared per-plan `Context` bag, and `TSK-0004` says `SearchMemoryTool` should write coordinates into that context so `MoveToTool` can read them when explicit arguments are absent. The current commit only implements the consumer-side merge in `AgentBackgroundService`, where every non-internal context key is copied into arguments before tool dispatch. `MoveToTool` still validates only `x/y/z` arguments, and `SearchMemoryTool` currently emits `query`, `results`, `bestPageId`, and `count` only — no coordinate payload. That means the example path described in the task card is not yet real end-to-end. fileciteturn28file0 fileciteturn18file0 fileciteturn7file0 fileciteturn27file0 fileciteturn30file0

The architectural risk is bigger than the feature gap. The merge is effectively an implicit “hydrate all tools with all context” rule. That can silently satisfy required fields for unrelated tools, hide planner bugs, and move the contract from explicit tool schemas to ambient state. In a greenfield codebase, that is the kind of legacy-like coupling that is easy to regret later. A safer shape is an explicit allowlist per tool or a dedicated context-hydration step for only the tools that declare it. fileciteturn7file0 fileciteturn27file0

**Recommended fix:** keep `ActionData.Context` as the shared plan state, but hydrate `MoveToTool` explicitly from `Context` inside the tool or through a small schema-aware adapter. Add a focused allowlist for keys like `nearestWoodX/Y/Z` rather than copying every non-internal key into arguments. Add a regression test that proves `SearchMemory → MoveTo` works with no explicit coordinates and that unrelated context keys are ignored.

### 2) SQLite telemetry needs failure isolation, retention rules, and proof
The commit wires `Serilog.Sinks.SQLite` into `Program.cs` and enables it via `Agent:Logging:Sqlite` in `appsettings.json`. The task card for `TSK-0014` explicitly requires that runtime logging “emit entries to sqlite without crashing when db is locked or unavailable,” and that existing sinks remain active. In the current change set, I do not see a startup guard, a fallback path, or tests for lock/unavailable cases. The commit message also says tests are unchanged, which means this new runtime path appears to be unexercised by the current suite. fileciteturn8file0 fileciteturn12file0 fileciteturn23file0 fileciteturn1file1

The package-level behavior may be fine in the happy path, but the repo’s own acceptance criteria are stricter than “it compiles.” For a runtime telemetry feature in an autonomous agent, startup failure or silent log loss is expensive. The current implementation also leaves retention/size management undefined for `logs/agent-telemetry.db`, which matters because telemetry sinks tend to accumulate forever unless they are intentionally rotated or pruned. fileciteturn8file0 fileciteturn23file0

**Recommended fix:** wrap sink activation in a hard failure boundary or a degraded-mode branch, document what happens when SQLite cannot open the DB, and add a test that simulates a locked/unwritable path. Define retention expectations now, before the database becomes a long-lived hidden state store.

### 3) The project-wide `NU1903` suppression is broader than the problem it describes
`WebUI.Blazor.csproj` suppresses `NU1903` for the whole project, with a comment that the warning comes from a transitive SQLite native package rather than from the sink logic. That may be true for the present dependency tree, but it is still a global warning suppression. In a codebase trying to reduce legacy and technical debt, that is the wrong default unless it is narrowly scoped and revisited promptly. fileciteturn13file0

**Recommended fix:** scope the suppression as narrowly as possible, or gate it behind a documented exception review. Treat this as temporary debt with an owner and removal criteria.

### 4) Sprint and task metadata are already drifting
The sprint handoff says Wave D is included in the latest branch and presents the work as complete, but the underlying task cards still say `Status: Open`. `TSK-0004` is still open even though `AgentBackgroundService.cs` has the dispatcher merge, and `TSK-0014` is still open even though `Program.cs` and `WebUI.Blazor.csproj` now include the SQLite sink. That is not just a doc nit; it makes it harder to know what is actually finished and what still needs implementation or test coverage. fileciteturn24file0 fileciteturn18file0 fileciteturn23file0

**Recommended fix:** update task status fields, or explicitly state that these cards are intentionally left open for follow-up work. Right now the repo tells two different stories.

## Supplemental implementation notes

### What looks good
`ChatInterpreter` cleanup is directionally correct. Removing the dead regex fields reduces confusion, and the fallback intent path still preserves deterministic behavior for stop/cancel, status, inventory, help, and coordinate navigation. I did not find evidence that the cleanup itself introduces a runtime bug. fileciteturn9file0

### What remains ambiguous
I could not verify a workflow run artifact for this exact commit through the connector; `fetch_commit_workflow_runs` returned no attached runs, so the “build/tests green” claim is currently supported by the commit message and handoff text rather than by a visible CI artifact in this session. fileciteturn38file0 fileciteturn1file1

### Likely next engineering step
The highest-value follow-up is to turn context carry into an explicit, tested contract rather than an ambient merge. That gives you the behavior the task card wants, keeps the tool schemas honest, and prevents a hidden state channel from becoming the next legacy system.

## Assumptions

I treated the task JSON files as the authoritative source for whether work is actually open or done. I also treated the current branch snapshot at `d2ef16ab86d433cc62912939c213cde088dcaf05` as the audit target, not the historical sprint plan. fileciteturn18file0 fileciteturn23file0 fileciteturn1file1

## Open questions

Should context hydration be tool-specific instead of global?  
Should `SearchMemoryTool` be the only producer of nearest-coordinate keys, or should the planner and other tools be allowed to write them too?  
Should the SQLite sink remain enabled by default, or should it be opt-in until lock/unavailable behavior is tested?  
Should `TSK-0004` and `TSK-0014` be closed in the task store now that the runtime change exists, or are they intentionally left open for the remaining test and producer-side work?

## Overall assessment

**Verdict:** good directional cleanup, but not yet a clean consolidation point.  
**Overall confidence:** 92% that the three highest-priority issues above are real and worth addressing before the branch is treated as a stable baseline.

