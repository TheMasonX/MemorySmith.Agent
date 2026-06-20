# MemorySmith.Agent Code Audit
**Scope:** latest publicly visible `main`-branch code on GitHub plus open PR #1 (`sprint-5-tool-safety`) and the repo roadmap / agent guidelines.  
**Date:** 2026-06-19  
**Repo:** `themasonx/memorysmith.agent`

## Executive summary

This codebase is moving in the right direction architecturally: the repository has a clear bounded-context split, the roadmap is explicit, and the current PR is focused on the exact areas that matter most for safety and plan quality. The biggest issue I found is that the current implementation still appears to lag the PR/sprint intent in a few key places, especially around tool validation and planner behavior. The most important risks are:

1. **Tool argument validation is still not enforced at the dispatcher boundary in the code I inspected.** Both `ToolDispatcher.CallAsync` and the older `ToolEngine.CallAsync` still contain TODOs for schema validation, despite the PR description claiming validation is part of Sprint 5. That is a real safety gap because unvalidated inputs can reach tool execution. citeturn451881view0turn744121view0turn570814view0
2. **Legacy tool infrastructure still exists alongside the new dispatcher.** The PR says `ToolEngine`, `ToolRegistry`, and `IToolRegistry` were deleted or replaced, but the repository pages I inspected still show these modules present. That creates architectural drift and increases the odds of bugs, duplicated logic, and incomplete migrations. citeturn570814view0turn533875view0turn744121view0turn910347view0
3. **The planner can silently drop work.** `HtnPlanner` skips unknown phases without surfacing an error or warning. If a phase name changes, a decomposer is missing, or a task is misconfigured, the plan can degrade silently. citeturn862838view0
4. **`ReplanAsync` is not failure-aware.** It rebuilds a goal from the original phases and does not use the failure reason to change strategy. That makes repeated failure loops likely. citeturn862838view0
5. **A few hidden defaults can mask bad state.** Build origin facts default to `0`, which can lead to incorrect placement if origin facts are absent, and block ID parsing in the world projector is somewhat brittle. citeturn935378view1turn720596view0

The roadmap already shows several upcoming tasks that overlap with this audit, especially `TryInterruptOnDamage`, `GatherGoalDecomposer` target-count pass-through, `TimeProvider`, and `IWorldObservationGateway`. I have intentionally **not** duplicated those as “new tasks”; I only call them out where they matter for coordination. citeturn470988view0

## High-confidence findings

### 1) Tool validation is promised but not actually enforced in the code I inspected
**Confidence: 95%**

The PR description says Sprint 5 adds tool safety via argument validation before execution. However, the current `ToolDispatcher.CallAsync` still contains a TODO to validate arguments against `tool.InputSchema` before dispatch, and `ToolEngine.CallAsync` contains the same TODO. That means the safety invariant appears to be incomplete in the visible implementation. citeturn570814view0turn451881view0turn744121view0

**Why it matters:** this is the exact kind of boundary where schema validation must be non-optional. If the input object is allowed to flow into tools unchecked, malformed inputs can become logic bugs, unexpected runtime exceptions, or unsafe tool calls.

**Implementation-level recommendation:** move schema validation into the one true dispatch boundary, fail closed on invalid input, and add direct tests that assert invalid payloads never reach tool execution.

---

### 2) Legacy tool abstractions still coexist with the newer dispatcher
**Confidence: 90%**

The PR description says `ToolEngine`, `ToolRegistry`, and `IToolRegistry` were deleted in favor of the newer `ToolDispatcher` model. But the repository views I inspected still show `Agent.Tools/ToolEngine.cs`, `Agent.Tools/ToolRegistry.cs`, and the dispatcher module all present. That is a sign of incomplete migration or parallel architecture that can drift over time. citeturn570814view0turn533875view0turn744121view0turn910347view0

**Why it matters:** duplicated execution paths are one of the fastest ways to accumulate inconsistencies. Even if one path is “dead,” it becomes a maintenance trap unless it is removed or clearly isolated.

**Implementation-level recommendation:** collapse the tool layer to a single orchestration path, delete the old registry/engine surface area, and add a repo-wide reference sweep so no callers still bind to the removed concepts.

---

### 3) Planner phase handling can fail silently
**Confidence: 88%**

`HtnPlanner` iterates phases and only expands those that are known to the library. Unknown phases are skipped with no visible error path, and the planner only throws if the final action list is empty. That means partially invalid plans can still execute with missing segments. citeturn862838view0

**Why it matters:** silent degradation is worse than a hard failure here. A typo in a phase name, a missing decomposer registration, or a mismatch between goal definitions and the task library can cause incomplete plans that look legitimate.

**Implementation-level recommendation:** treat unknown phases as a first-class planning error, or at minimum log them with enough context to fail fast in test and to triage quickly in production.

---

### 4) Replanning is too generic and ignores failure context
**Confidence: 96%**

`ReplanAsync` rebuilds a `SimpleGoal` from the original phases and re-enters `PlanAsync`; it does not use the `failureReason` to influence the next plan. This makes replanning effectively “same request, try again,” which is rarely enough for robust agent behavior. citeturn862838view0

**Why it matters:** replanning should be adaptive. If a tool timed out, if an action was blocked by missing resources, or if the environment changed, the replanner should choose a different branch or insert recovery steps.

**Implementation-level recommendation:** convert failure reasons into structured replanning signals and make the planner select recovery strategies rather than replaying the same phase list.

---

### 5) Hidden fallback state can create wrong-world assumptions
**Confidence: 72%**

`BuildGoal` and its planner support code read build-origin facts from the world state, but when those facts are missing the code falls back to `0`. That is convenient, but dangerous: a missing fact becomes a valid coordinate rather than an explicit failure. citeturn935378view1turn862838view0

**Why it matters:** this kind of fallback can make builds appear to “work” while actually anchoring them to the wrong location.

**Implementation-level recommendation:** replace the implicit `0` default with a nullable/explicit origin model so the planner must prove the origin is known before it builds.

---

### 6) Block ID normalization in the projector is brittle
**Confidence: 70%**

`WorldStateProjector.ApplyBlockMined` derives an item key by splitting the block string on `:` and using the second segment. That works for a common namespaced identifier, but the logic is brittle and assumes a simple format forever. citeturn720596view0

**Why it matters:** a parser this small is easy to forget and hard to debug when edge-case block IDs appear.

**Implementation-level recommendation:** use a dedicated normalization helper that handles namespaced identifiers explicitly and is covered by tests for odd and future block ID shapes.

## Architectural assessment

The overall architecture has the right broad direction: core state projection is separate from tool execution, planning is separate from execution, and the roadmap is openly staged. The repository also documents several bounded contexts and has a sprint/review model, which is a good sign for maintainability. citeturn376970view0turn470988view0

The main architecture risk is **transitional duplication**. The codebase appears to be in the middle of several foundational shifts at once: tool orchestration, planner extensibility, journal/world-model layering, and lifecycle handling. That is normal during an active sprint, but it only stays healthy if the old surfaces are aggressively removed and the new boundaries are enforced consistently. The current evidence suggests that some old surfaces are still present while the new ones are not yet fully hardened. citeturn570814view0turn533875view0turn744121view0turn910347view0turn862838view0

From an architecture-improvement perspective, the highest leverage moves are:

- **One dispatch boundary for tools.** Validation, timeouts, and telemetry should live there.
- **One planner contract for replanning.** Failure reasons should reshape the next plan, not just re-enter the same pipeline.
- **Typed state instead of implicit facts where possible.** Defaulting to `0` or parsing strings late is fragile.
- **Explicit error surfaces for “unknown” states.** Unknown phases, missing facts, and mismatched decomposers should be visible in logs/tests immediately.

## Sprint-duplication check

I compared the audit themes against the public roadmap and the PR description before naming findings. These items are already on the near-term roadmap, so I am **not** treating them as new audit tasks:

- `TryInterruptOnDamage` integration test. citeturn470988view0
- `GatherGoalDecomposer` target-count pass-through fix. citeturn470988view0
- `TimeProvider` abstraction. citeturn470988view0
- `IWorldObservationGateway` note/design doc. citeturn470988view0

I did still reference them when they affected priority or explained why a related design area deserves attention, but I did not count them as separate “new” recommendations.

## Detailed implementation notes

### Tool layer
`ToolDispatcher` is the right shape for a cleaner architecture, but it is not yet the fully enforced safety gate that the PR description implies. The TODO comments in both the dispatcher and the older engine are the clearest evidence that the validation boundary is still incomplete. citeturn451881view0turn744121view0turn570814view0

### Planner layer
`HtnPlanner` contains several good ideas, including goal-specific decomposition and fallback behavior. The problem is not the concept; it is the silent failure modes. Unknown phases and repeated replans should never look successful unless the planner can explain why they are safe. citeturn862838view0

### World model / projection
`WorldStateProjector` is mostly disciplined and is described as stateless/pure, which is a good property for correctness. The one place I would tighten immediately is normalization logic for mined block IDs, because small parsing shortcuts tend to become long-term maintenance bugs. citeturn720596view0

### Goal definitions
`BuildGoal` and `SurviveNightGoal` show that the team is already thinking in bounded, testable terms. The hidden risk is not the goal objects themselves; it is the way they depend on weakly typed world facts or magic defaults to make decisions. citeturn935378view1turn935378view2

## Assumptions

- I treated the publicly visible GitHub `main` code and the open PR/roadmap pages as the source of truth for this audit.
- I did **not** execute the test suite locally, so any test-health statements in the README were treated as claims rather than independently verified facts. The README does claim 200+ tests and a passing suite, but I did not re-run them. citeturn376970view0
- I assumed the PR branch is intended to land into the current sprint workstream and that the roadmap is the authoritative sprint tracker. citeturn570814view0turn470988view0

## Open questions

- Is the older tool path intentionally kept for compatibility, or is it supposed to be removed entirely before merge?
- Should unknown planner phases fail fast, or is silent skipping an accepted compatibility policy?
- Is the `0` build-origin fallback an explicit design choice, or just a placeholder until typed origin state lands?
- Is the sprint-24 work expected to close the remaining planner/world-model gaps before the next larger architecture pass?

## Recommended next actions

1. Enforce schema validation in exactly one tool dispatch boundary and delete the redundant tool execution surface area. citeturn451881view0turn744121view0turn570814view0
2. Make planner failures explicit when phases are unknown or decomposers are missing. citeturn862838view0
3. Make replanning consume structured failure context instead of replaying the same phase list. citeturn862838view0
4. Replace implicit build-origin defaults with explicit typed state. citeturn935378view1turn862838view0
5. Tighten block ID normalization and add edge-case tests. citeturn720596view0

## Confidence notes

- Tool validation gap: **95%**
- Legacy tool duplication: **90%**
- Silent phase skipping: **88%**
- Replan ignores failure context: **96%**
- Build origin default risk: **72%**
- Block ID parsing brittleness: **70%**

These values reflect the strength of the evidence I observed in the current GitHub pages, not just a general gut feel.
