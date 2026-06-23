# MemorySmith.Agent code audit report

Audit target: the repository as exposed by GitHub for `TheMasonX/MemorySmith.Agent`, with the currently available PR head `sprint-5-tool-safety` merged into `main`. The repo page shows `main` as the default branch, and the only surfaced PR is PR #1 with head `sprint-5-tool-safety`; I could not verify a distinct `sprint-35-llm-first` branch from the available repo views, so I treated the exposed PR head as the closest auditable target. citeturn618533view0turn420385view0

I also checked the sprint-35 handoff document in the repo. It already tracks the same build-origin, API auth, and connect-announcement work described in your request, so I did not find a separate duplicate task item to add beyond that plan. fileciteturn13file0

## Executive summary

Overall, the codebase is moving in the right direction: the latest work adds real guardrails at the tool boundary, better observability, better chat routing, and cleaner world-model behavior. The strongest evidence of progress is the new schema validation in `ToolDispatcher`, the journal plumbing, the auth bootstrap in `Program.cs`, and the explicit build-origin parsing in chat and goal creation. fileciteturn15file0turn23file0turn24file0turn31file0

The main concern is not “missing features”; it is that a few critical paths now contain enough branching, fallback state, and policy logic that the code can easily make the wrong decision even when the data is present. The clearest example is build-origin resolution: the explicit/chat/fact/auto-detect precedence is described as a three-tier flow, but the current decomposer still toggles `requireOrigin` only from whether chat supplied coordinates, not from whether stored facts exist. That makes the fallback behavior likely to fire in cases where it should not. Confidence: 85%. fileciteturn17file0turn18file0turn13file0

The second major theme is architecture drift. `AgentBackgroundService` has become an orchestration hotspot for connection management, goal lifecycle, health interrupts, dashboard signaling, journaling, replan throttling, and action correlation. `GoalFactory` is also becoming a policy engine rather than a factory. Both are maintainability risks even if the immediate behavior works. Confidence: 90% for the architectural smell, 75% for near-term defect risk. fileciteturn26file0turn16file0

## Highest-priority findings

### 1) Build-origin precedence is likely wrong in the non-chat path
**Severity:** High  
**Confidence:** 85%

The build origin design says precedence should be: explicit chat coordinates, then stored facts, then auto-detect via `FindFlatArea`. The decomposer follows that narrative on paper, but the actual implementation sets `requireOrigin = !bg.HasExplicitOrigin`, which ignores whether any origin facts were found. That means the auto-detect branch can be triggered whenever the user did not type coordinates, even if the world state already contains a valid saved origin. fileciteturn17file0turn18file0

Why this matters: it can cause the bot to “forget” an already-resolved build site and re-scan or re-choose an origin. That is the kind of bug that is hard to spot in happy-path tests but very visible in live play.

Recommended fix: make origin resolution return a structured result such as `{ origin, provenance, isComplete }` and pass that through to decomposition, or at minimum change the flag to reflect both explicit coords and stored facts. Add a regression test for “facts exist, no chat coords, do not auto-detect.”

### 2) Partial explicit origin is treated as explicit even when incomplete
**Severity:** Medium  
**Confidence:** 75%

`BuildGoal` marks `HasExplicitOrigin` true when any one of `OriginX`, `OriginY`, or `OriginZ` is present. But the description and downstream logic assume a full coordinate triple. This makes a partially-populated build goal look explicit while still being incomplete. The description also interpolates nullable values if only one axis is present. fileciteturn17file0

Recommended fix: treat the origin as explicit only when all three coordinates are present, or validate at parse time and reject partial coordinates outright. This would reduce state ambiguity and remove a class of malformed-goal edge cases.

### 3) Tool schema validation is useful, but the validator is intentionally partial
**Severity:** Medium  
**Confidence:** 80%

`ToolDispatcher` now enforces schema validation before execution, which is a meaningful safety improvement. But the implementation only handles a narrow subset: root `object`, top-level `properties`, required fields, and primitive type checks. It does not validate nested structures, enums, numeric bounds, array item schemas, `additionalProperties`, or combinators like `oneOf`. The comments imply all registered tools fit this subset, but that is a policy assumption, not a mechanically enforced contract. fileciteturn15file0

Recommended fix: either codify the subset as a formal invariant with tests, or move to strongly typed tool arg models / a proper schema validator for any tools that need more than the shallow subset. The current solution is good as a guardrail, but it should not be mistaken for full schema support.

### 4) Chat routing improved, but parsing remains brittle in several places
**Severity:** Medium  
**Confidence:** 70%

The chat interpreter is materially better than before: it now has deterministic craft routing, optional build coordinates, whole-word bot-name matching, and a cleaner status heuristic. That said, the parser is still regex-heavy and will continue to have false-positive and false-negative edge cases as the command language grows. The build regex accepts a broad free-text blueprint phrase plus an optional `at X Y Z` suffix; the craft regex similarly accepts loose item phrases. fileciteturn23file0

The LLM interpreter also adds truncated-JSON salvage logic. That is a pragmatic reliability improvement, but it is also a brittle recovery path that can silently accept malformed output. Confidence: 65% that this is a manageable near-term risk, 85% that regression tests are needed around it. fileciteturn24file0

Recommended fix: add a small command corpus test suite that covers ambiguous commands, partial coordinates, “craft” vs “gather” collisions, and truncated LLM outputs. Keep the deterministic fast path, but reduce the amount of hand-built regex logic over time.

### 5) `AgentBackgroundService` is carrying too many responsibilities
**Severity:** High as an architecture issue  
**Confidence:** 90%

The background service now handles: reconnect loops, goal transitions, emergency stop, journaling, health interrupts, replan throttling, dashboard updates, action correlation, and chat announcements. This is classic orchestration sprawl. Even if each piece is individually correct, the class is becoming the place where unrelated policy decisions accumulate. fileciteturn26file0

Recommended refactor: split into smaller collaborators, at minimum:
- connection/session manager
- goal lifecycle manager
- action dispatcher / correlation tracker
- health and interrupt policy
- telemetry/journal sink

That will make the code easier to reason about and dramatically easier to test in isolation.

### 6) `GoalFactory` is drifting away from a factory into a policy service
**Severity:** Medium  
**Confidence:** 85%

`GoalFactory` now knows about built-in direct-mine items, craft-item goal creation, build-origin parameters, registry fallback, and logging policy. That is useful in the short term, but it is also an indicator that the class is doing synthesis, validation, fallback, and domain policy selection all at once. fileciteturn16file0

Recommended refactor: separate “parse request”, “resolve domain data”, and “materialize goal object” responsibilities. This would also make it easier to add new goal types without bloating the factory further.

## Additional observations

The auth bootstrap in `Program.cs` looks like a real improvement: it maps `MEMORYSMITH_API_KEY` into `Agent:Memory:ApiKey`, configures a separate world-memory gateway, and cleanly falls back to `BaseUrl` when `WorldKbUrl` is empty. That aligns with the sprint handoff and closes a very real class of 401 failures. Confidence: 90%. fileciteturn31file0turn13file0

The world model got a concrete quality-of-life fix: defensive copies now prevent shared mutable inventory state between observed and belief snapshots. That is a solid reliability improvement. The prediction rules are still deliberately approximate, but the state-sharing bug itself looks addressed. Confidence: 85%. fileciteturn25file0

The connect announcement in `AgentBackgroundService` is a small but useful usability win. It is not a bug fix, but it does reduce “is the bot alive?” uncertainty in live play. Confidence: 95% that this is user-visible and intentional. fileciteturn26file0

## Duplication and sprint-plan check

I compared the current implementation against the sprint-35 handoff note. The handoff already includes the same three workstreams you called out: API auth fix, connect announcement, and build-origin coordinate handling with auto-detect fallback. That means the current code is not obviously duplicating a separate, untracked plan item; it is mostly executing the existing sprint plan. fileciteturn13file0

I did not find evidence of a second, independent sprint plan that would create obvious duplicate work from the materials I could access. The main risk is not duplication; it is that the plan itself is now large enough that responsibilities are starting to overlap in code.

## What is working well

The following are genuine improvements worth keeping:
- Dispatch-time schema validation at the tool boundary. fileciteturn15file0
- Deterministic chat routing for craft/build/status commands. fileciteturn23file0turn24file0
- Auth/bootstrap wiring for the agent and world memory paths. fileciteturn31file0
- Defensive copy fixes in the world model. fileciteturn25file0
- Explicit goal failure/recovery metadata hooks. fileciteturn27file0

## Assumptions

- I treated the exposed PR head `sprint-5-tool-safety` as the auditable target because that is the branch and PR surface I could verify from GitHub. citeturn420385view0turn618533view0
- I assumed the sprint-35 handoff reflects the intended current plan because it explicitly describes the build-origin/auth/connect work you referenced. fileciteturn13file0
- I assumed the code in the PR diff is representative of the latest accessible commit for the branch because the connector surfaced the merged PR head SHA and diff metadata. citeturn420385view0

## Open questions

- Should partial build coordinates be rejected outright, or should missing axes be filled from facts?
- Is `FindFlatArea` meant to be a hard fallback only when no facts exist, or should it also be used when facts exist but are stale?
- Are any tool schemas intentionally outside the validator’s supported subset?
- Should `AgentBackgroundService` keep owning action correlation and interrupt policy, or should those be moved into separate services before the next sprint?
- Is there an intended branch name different from the one currently exposed by GitHub for this repo?

## Confidence snapshot

- Build-origin precedence bug: 85%
- Partial explicit origin inconsistency: 75%
- Schema validator incompleteness: 80%
- Chat/LLM routing brittleness: 65%
- BackgroundService orchestration sprawl: 90%
- GoalFactory policy creep: 85%
- Auth wiring improvement: 90%
- World model defensive-copy fix: 85%

## Bottom line

This is a meaningful step forward, but the codebase is at the point where the remaining problems are less about missing features and more about decision ownership. The next round of improvements should focus on making origin resolution, goal synthesis, tool validation, and service orchestration more explicit and less stringly-typed. That will reduce bugs faster than adding more heuristics.
