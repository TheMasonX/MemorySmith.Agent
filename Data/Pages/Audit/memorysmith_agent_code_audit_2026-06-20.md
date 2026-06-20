# MemorySmith.Agent Deep-Dive Code Audit
_Date: 2026-06-20_

## Executive summary

I reviewed the current repo, the active PR/branch, the roadmap, the architecture notes, the coding conventions, and the most relevant runtime/planning/tooling paths before writing this audit. The repository describes itself as the MemorySmith wiki + agentic world model harness, and the active PR is the long-lived `sprint-5-tool-safety` branch. The roadmap is now at Sprint 23 and already contains Sprint 24 priorities, so I treated those items as backlog to verify rather than as new suggestions to duplicate. citeturn946541view0turn214405view0turn853735view1

Overall assessment: the codebase is in materially better shape than a typical agent loop, especially around safety and recoverability. The strongest evidence is the dispatcher/adapter split, structured world-state projection, emergency-stop and damage-interrupt handling, and a time abstraction already threaded through the background service. The main remaining risks are not “missing basics” so much as boundary issues: partial schema validation, identity/collision edge cases for goal decomposition, backlog/task drift, and a few places where hardcoded fallback logic could become a maintenance hotspot. citeturn305774view0turn483741view1turn665660view1turn445802view0

## Top findings in plain language

1. The planned Sprint 24 `TimeProvider` abstraction looks already implemented in the branch, so that backlog item appears stale or at least partially complete. Confidence: 95%. citeturn853735view1turn665660view1turn361574view0

2. `ToolDispatcher` is a real safety improvement: it validates tool schemas, rejects unknown tools, and converts execution failures into structured `ToolResult` failures instead of letting them tear down the loop. Confidence: 93%. But its validator is explicitly only a lightweight subset of JSON Schema, so future tools can still drift into a false sense of validation coverage. Confidence on that risk: 90%. citeturn305774view0

3. The roadmap’s gather-goal work is still relevant. The code shows gather goals are count-aware in the planner/background path, but the planning/goal identity layer still looks vulnerable to collision or pass-through mistakes, which matches the Sprint 24 `GatherGoalDecomposer TargetCount` item. Confidence: 78%. citeturn853735view1turn665660view1turn327196view0

4. Damage interruption and passive health checks are thoughtfully implemented, but the roadmap correctly calls out an integration test as a high priority. That is the right next test because the behavior depends on queue clearing, cooldown timing, and event ordering. Confidence: 88%. citeturn853735view1turn665660view1

5. The architecture is broadly healthy and already split into bounded contexts, but there are still a few cross-cutting seams where logic is embedded in the runtime loop or goal factory. Those seams are good refactor targets because they will keep the planner deterministic, the tool layer explicit, and the world-adapter contract narrow. Confidence: 82%. citeturn853735view2turn305774view0turn665660view1

6. The branch/roadmap naming is inconsistent enough to increase duplication risk: the PR page still calls this Sprint 5-6 work, while the roadmap has already advanced to Sprint 23 and is planning Sprint 24. Confidence: 96%. citeturn214405view0turn853735view1

## Scope and methodology

I checked the repo root, the open PR, the roadmap, the architecture note, the agent runloop, the planner, the goal factory, the world-state projector, the tool dispatcher, and the app wiring. I also checked the project conventions in `AGENTS.md` so I would not recommend changes that conflict with the team’s established workflow or naming rules. citeturn946541view0turn214405view0turn515543view0

I treated the roadmap as the source of truth for active sprint priorities and avoided restating items that were already explicitly scheduled there unless the code suggested the item was already implemented, partially implemented, or mis-scoped. The roadmap’s current Sprint 24 list is: integration test for `TryInterruptOnDamage`, `GatherGoalDecomposer` target-count pass-through, `TimeProvider` abstraction, and an `IWorldObservationGateway` design note. citeturn853735view1

## Findings

### 1) `TimeProvider` is already threaded through the runtime; the roadmap item is probably stale

The Sprint 24 backlog says to add a `TimeProvider` abstraction for testable time-dependent logic. However, the branch already injects `ITimeProvider? timeProvider = null` into `AgentBackgroundService`, defaults to `SystemTimeProvider.Instance`, and uses `_timeProvider.UtcNow` for goal setting, cancellation, journaling, damage timing, passive health timing, and replan timing. The app wiring also registers `SystemTimeProvider.Instance` in `Program.cs`. That means the abstraction is not merely planned; it is already in the runtime path. citeturn853735view1turn665660view1turn361574view0

Impact: if the backlog is left unchanged, the team may spend time re-implementing an already-present abstraction or may fail to test the actual remaining gaps, which are likely around coverage and consistency rather than raw availability of the interface. Confidence: 95%. Recommendation: retitle or close the backlog item, then replace it with an explicit test/coverage task that verifies all time-sensitive code paths use the abstraction consistently. citeturn853735view1turn665660view1turn361574view0

### 2) Tool execution is much safer now, but schema validation is still intentionally incomplete

`ToolDispatcher` now validates each tool’s `InputSchema`, returns a structured failure for unknown tools, and catches tool exceptions so a bad tool call becomes a `ToolResult(false, ...)` rather than a process-level failure. That is a strong architectural improvement because it keeps the agent loop observable and recoverable. citeturn305774view0

The catch is that the validator is explicitly a lightweight subset of JSON Schema rather than a full implementation. That means some constraints can be silently under-enforced if a future tool relies on richer schema semantics. The risk is less “bug today” than “validation debt will become invisible as the tool set grows.” Confidence: 90%. Recommendation: either formally document the supported subset in the tool contract or move toward a shared schema validator so tool authors do not assume unsupported constraints are being enforced. citeturn305774view0

### 3) Gather-goal identity still looks fragile, and this matches an already-planned Sprint 24 item

The roadmap explicitly calls out a `GatherGoalDecomposer` target-count pass-through fix for Sprint 24. That is a good sign that the team already knows this area needs attention. The code confirms why it matters: gather goals are count-aware in the dynamic goal creation path, and the active goal/state machinery now has to preserve that count correctly across planning, completion checks, and failure tracking. citeturn853735view1turn665660view1turn327196view0

My read is that the likely failure mode is not “gathering the wrong item” but “count-specific intent being collapsed into item-only identity somewhere in the chain,” which can produce weird retry/recovery behavior or make two different gather goals interfere with one another. Confidence: 78%. Recommendation: make the target count part of any identity used for decomposition, failure keys, recovery guards, and journaling tags, then add a regression test that proves two `Gather:<item>` goals with different counts stay distinct end-to-end. citeturn853735view1turn327196view0turn665660view1

### 4) Damage interruption is well-designed, but it needs the integration test the roadmap already promises

`AgentBackgroundService` now tracks previous health, synthesizes damage events when health drops, rate-limits repeated damage interrupts, and separately rate-limits passive low-health `GetStatus` enqueues. It also clears the action queue on goal changes and cancel paths, which is exactly the sort of behavior you want in a fire-and-forget action architecture. citeturn665660view1

The remaining risk is coordination, not existence. The right thing to test is whether the queue is always cleared when damage requires an interrupt, whether the cooldowns behave correctly under repeated hits, and whether the agent resumes cleanly after a status refresh. That aligns directly with the Sprint 24 integration-test item. Confidence: 88%. Recommendation: keep the integration test high priority and make it assert queue state, interrupt frequency, and replan behavior across at least one repeated-damage scenario. citeturn853735view1turn665660view1

### 5) The architecture is already split well, but there are still seams where hidden coupling can grow

The architecture note describes three bounded contexts: Agent Core, MemorySmith Knowledge, and World Adapters, with a runtime flow that goes from UI/API into the background service, planner, tool dispatcher, and world adapter before feeding journal/world-model/damage-interrupt logic. That is a solid shape for an agentic system because it gives the planner, memory, and adapter layers distinct jobs. citeturn853735view2

The remaining design smell is that some of the cross-cutting behavior still lives in large orchestration surfaces such as `AgentBackgroundService`, `GoalFactory`, and the app wiring. That is not a defect by itself, but it creates a maintenance tax when behavior expands: new timing rules, new tool types, or new goal classes can end up being implemented in multiple places. Confidence: 82%. Recommendation: continue the architectural direction already started in the repo by extracting narrow interfaces around time, world observation, goal decomposition, and tool execution so the orchestration layer only coordinates, not interprets. citeturn853735view2turn305774view0turn665660view1

### 6) The planner is deterministic-first, but the missing LLM fallback should be tracked as an explicit limitation

The planner currently performs direct task-library matches and phase-by-phase decomposition, and it throws if no actions are produced because the LLM fallback is not implemented yet. That is acceptable if the team wants deterministic behavior first, but it is an explicit capability boundary. citeturn144565view0

Impact: any reduction in task-library coverage or any mismatch in goal decomposition can turn into a hard failure rather than a degraded-but-usable plan. Confidence: 90%. Recommendation: keep this as a deliberate product decision for now, but add a test that proves the planner fails loudly and predictably when no decomposition path exists, so the failure mode stays diagnosable instead of surprising. citeturn144565view0

### 7) Goal factory fallback logic is practical, but it is becoming a maintenance hotspot

`GoalFactory` has grown beyond simple registry lookup into a mix of static goals, dynamic prefixes, built-in direct-mine fallbacks, and special-case source/yield mappings. That makes the system more resilient when registry data is missing, which is good, but it also means the factory now carries domain knowledge that may belong in a more explicit goal/spec registry or policy object. Confidence: 65%. citeturn715578view2turn900615view2

The architectural risk is that every new “helpful fallback” makes the factory more powerful and less transparent. Over time that can hide missing data problems and make it harder to reason about why a goal was created one way instead of another. Recommendation: keep the fallback behavior, but move the rules into a dedicated policy or resolver layer with tests so the factory becomes a thin composition root again. citeturn715578view2turn900615view2

### 8) Planning and sprint documentation are drifting apart

The PR page still labels this as Sprint 5-6 work, while the roadmap has advanced through Sprint 23 and is now planning Sprint 24. That is not just a cosmetic issue; it increases the odds that an engineer, reviewer, or future audit will miss an already-scheduled task or duplicate work. Confidence: 96%. citeturn214405view0turn853735view1

Recommendation: make the roadmap the single sprint-truth artifact and add a short “current sprint / next sprint” pointer in the repo root or PR description so reviewers do not have to reconcile two different naming schemes. citeturn853735view1turn214405view0

## What I would prioritize next

First, close the loop on the Sprint 24 items that are already partly resolved in code: the time abstraction, the gather-goal target-count identity path, and the damage-interrupt integration test. Second, harden the tool schema contract so the supported validation subset is explicit. Third, keep extracting orchestration logic out of the background service and goal factory only where the refactor clearly reduces coupling rather than just moving code around. citeturn853735view1turn665660view1turn305774view0

## Assumptions

I assumed the active branch and roadmap are authoritative for sprint planning, even though the PR title and roadmap sprint numbers are not aligned. I also assumed the code snippets retrieved from the branch represent the latest commit on the PR branch, because the GitHub pages and raw files reflected that branch name. citeturn214405view0turn853735view1

I did not treat every TODO-style comment as a defect. I only escalated items when they had a clear impact on correctness, recoverability, testability, or architectural clarity. citeturn515543view0turn305774view0turn665660view1

## Open questions

1. Is the Sprint 24 `TimeProvider` item now obsolete, or is there still a remaining surface outside `AgentBackgroundService` that has not been converted yet? Confidence in ambiguity: 40%. citeturn853735view1turn665660view1turn361574view0

2. Does `GatherGoalDecomposer` still collapse target-count identity anywhere outside the main goal object, especially in failure keys or recovery logic? Confidence in ambiguity: 55%. citeturn853735view1turn327196view0turn665660view1

3. Which JSON Schema features are intentionally unsupported by the dispatcher validator, and should that subset be enforced/documented at the tool authoring boundary? Confidence in ambiguity: 30%. citeturn305774view0

4. Should the roadmap be the only sprint tracker, or are there still branch-local notes that need to be synchronized before Sprint 24 begins? Confidence in ambiguity: 35%. citeturn853735view1turn214405view0

## Confidence legend

- 95–96%: supported by multiple direct sources and low ambiguity.
- 88–93%: strong evidence with minor inference.
- 75–82%: likely, but some branch-path detail or identity flow still needs direct test confirmation.
- 65% or lower: architectural recommendation rather than a confirmed defect.

## Evidence index

- Repo root and README: citeturn946541view0
- Active PR / branch: citeturn214405view0
- Roadmap and Sprint 24 backlog: citeturn853735view1
- Architecture note: citeturn853735view2
- AGENTS / workflow conventions: citeturn515543view0
- Tool dispatcher: citeturn305774view0
- Background service / time abstraction / interrupts: citeturn665660view1
- Program wiring / time provider registration: citeturn361574view0
- Planner fallback behavior: citeturn144565view0
- GoalFactory dynamic goal creation: citeturn715578view2turn900615view2
- Gather goal behavior: citeturn327196view0
