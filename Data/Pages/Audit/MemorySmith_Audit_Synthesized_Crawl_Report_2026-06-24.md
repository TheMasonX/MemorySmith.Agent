# MemorySmith.Agent Deep Dive Audit
**Branch:** `sprint-35-llm-first`  
**Repo:** `TheMasonX/MemorySmith.Agent`  
**Audit date:** 2026-06-24  
**Status:** Synthesis from staged crawls; still incomplete at full source-tree coverage.

## Scope

This report synthesizes evidence from:
- `README.md`
- `AGENTS.md`
- `Data/Pages/architecture.md`
- `Data/Pages/roadmap.md`
- `Agent.Planning/Router/PlannerRouter.cs`
- `WebUI.Blazor/AgentBackgroundService.cs`
- supporting branch search results and task/roadmap references

The branch contains a strong architectural discipline, but the runtime still shows a recurring pattern of contract drift, hidden fallback behavior, and heavyweight coordination concentrated in a few core files.

---

## Executive Summary

### Overall assessment

The codebase is thoughtfully engineered and unusually well-governed for an autonomous-agent system. It has:
- explicit bounded contexts,
- deterministic-first design rules,
- a sprint/council process,
- broad test coverage claims,
- and a documented effort to eliminate silent failures.

The main concern is that several subsystems still encode important behavior through implicit contracts rather than explicit types or centralized policy objects. The strongest examples are build-origin handling, planner fallback semantics, and the agent runtime’s lifecycle state.

### Highest-risk themes

1. **Runtime state is concentrated in `AgentBackgroundService`.**  
   The hosted service is effectively a coordination hub for planning, chat, damage interrupts, replan policy, action correlation, and connection management. That makes it a high-value component but also a major maintenance and regression hotspot. fileciteturn9file0

2. **Planner fallback still reconstructs synthetic goals.**  
   `PlannerRouter` falls back to a `SimpleGoal` shell when original goal context is unavailable, and the same reconstruction pattern appears in more than one place. That is a contract-risk signal because metadata and goal capabilities can be lost during replanning. fileciteturn8file0

3. **The repo is still paying down long-lived contract drift.**  
   AGENTS.md exists specifically because the codebase repeatedly hit problems with silent catches, swallowed events, parser-to-goal shortcuts, and file corruption during patching. That is a sign of recurring architectural failure modes rather than isolated bugs. fileciteturn5file0

4. **Documentation/version drift is real.**  
   README still presents v0.35.0 / Sprint 35 as complete, while roadmap content in the branch describes v0.40.0 with Sprint 41 in progress and a later observability phase in flight. fileciteturn3file0turn4file0

---

## Key Findings

### F1 — `AgentBackgroundService` is a coordination hotspot with god-object characteristics
**Confidence: 97%**

The constructor injects a large number of collaborators and the class owns many independent concerns: world adapter, tool calling, planner, SignalR hub, goal factory, chat interpreter, journal, reconnect policy, replan governor, time provider, intent manager, LLM evaluator, and runtime container. The file also maintains many pieces of mutable state for health, stalls, action correlation, and build checkpointing. fileciteturn9file0

This is not automatically a bug, but it is an architectural risk because the runtime becomes difficult to test and reason about. The comments themselves show the team already recognizes this via the `AgentRuntime` decomposition work. fileciteturn9file0

**Recommendation:** continue decomposing the service into explicit runtime managers, with `AgentBackgroundService` reduced to orchestration only.

---

### F2 — Planner routing is better, but replanning still loses domain fidelity when `OriginalGoal` is absent
**Confidence: 95%**

`PlannerRouter.ReplanAsync` reconstructs a `SimpleGoal` when the original goal object is unavailable. The same pattern is repeated in the private decomposer adapter. The code comments explicitly note that reconstructing a shell goal previously caused decomposer routing to fall back incorrectly. fileciteturn8file0

This is an important signal: the system is aware that goal identity matters, but the fallback contract still allows that identity to be dropped.

**Recommendation:** make `OriginalGoal` mandatory in replan context, or introduce a richer durable goal snapshot that preserves the type/capability contract.

---

### F3 — Build-origin handling appears to be a live domain-model debt
**Confidence: 90%**

The repo contains dedicated build-origin guidance and multiple sprint references to build-origin coordination. The architecture docs also call out build-origin as part of the runtime flow and mention auto-detected origins and state-derived origins as part of the goal lifecycle. fileciteturn6file0turn10file2

The risk is that build-origin semantics are becoming a cross-cutting domain concept rather than a simple set of coordinates. That usually means the design should become a value object or state machine, not remain a loose set of nullable fields or sentinel values.

**Recommendation:** elevate build origin into a first-class value object with explicit source/state markers.

---

### F4 — Configuration-heavy runtime policy should not be scattered through the hosted service
**Confidence: 88%**

`AgentBackgroundService` contains many named constants and per-tool overrides: action timeouts, damage cooldowns, stall thresholds, and health gates. Some of this is appropriate, but the quantity suggests configuration has leaked into orchestration code. fileciteturn9file0

This makes testing harder and increases the risk that runtime policy changes are only applied in one path.

**Recommendation:** move policy values into a dedicated runtime options object.

---

### F5 — Silent-failure history is a major architectural pattern, not an isolated defect
**Confidence: 96%**

AGENTS.md explicitly adds rules banning silent catches and dropped events, with examples and rationale. The document states that earlier sprints had defects caused by silent event drops and swallowed exceptions. fileciteturn5file0

This is highly valuable: it means the codebase already knows its own failure mode. But it also means there is still likely latent risk anywhere new catch/default branches appear.

**Recommendation:** require warnings/errors in every catch/default path that discards work, and consider adding repo-wide lint/test checks for those patterns.

---

### F6 — The current architecture is strong, but the planner/intent/runtime boundary still needs simplification
**Confidence: 84%**

The architecture docs describe a clean pipeline: Chat → Intent → GoalRequest → GoalFactory → Goal → Planner. The roadmap also says the current phase is observability-first and includes build-origin consolidation and typed replan outcomes. fileciteturn6file0turn4file0turn5file0

That is the right direction. The remaining risk is that responsibilities are still split across old and new flows, especially where legacy goal-name or fallback semantics remain in place.

**Recommendation:** finish the migration so the intent layer is the only place that maps semantic input to goal requests, and the planner is responsible only for decomposition/execution policy.

---

## Strengths

### S1 — Architectural discipline is unusually good
**Confidence: 97%**

This repo has:
- bounded contexts,
- explicit design principles,
- rules against magic numbers,
- explicit observability rules,
- and a documented sprint/council review process. fileciteturn6file0turn5file0turn4file0

That is a strong foundation.

### S2 — Correlated action tracking is a major plus
**Confidence: 98%**

The runtime maintains action correlation and action lifecycle tracking in the hosted service. That is the right direction for an autonomous system and materially improves diagnosis compared with fire-and-forget agents. fileciteturn9file0

### S3 — The repo clearly cares about testability and determinism
**Confidence: 94%**

The architecture docs, AGENTS.md, and roadmap repeatedly emphasize deterministic-first design, injectable time providers, and avoiding magic numbers. fileciteturn5file0turn6file0turn4file0

---

## Refactoring Opportunities

### R1 — Extract runtime state into explicit manager classes
**Confidence: 93%**

A likely target shape:
- `GoalLifecycleManager`
- `ActionDispatchManager`
- `DamageInterruptManager`
- `ReplanManager`
- `ConnectionManager`
- `ChatIntentManager`

This would reduce `AgentBackgroundService` from a policy-rich coordinator to a shell host.

### R2 — Replace fallback goal reconstruction with explicit replan context
**Confidence: 92%**

`ReplanGoalContext` should preserve enough information to route a replan without reconstructing a synthetic shell goal.

### R3 — Consolidate build-origin policy
**Confidence: 89%**

Build origin should be modeled in one place, not inferred separately by the planner, decomposers, and runtime event handlers.

### R4 — Move operational policies to options/config
**Confidence: 87%**

Timeouts, thresholds, and cooldowns are currently embedded as constants in the hosted service. These should be centralized.

### R5 — Introduce repo-wide guards for silent failure patterns
**Confidence: 95%**

Search and static checks for:
- `catch { }`
- `catch (Exception)`
- `return null` in critical paths
- empty default branches
- swallowed event handlers

---

## Evidence-Based Open Questions

1. Is the remaining `SimpleGoal` reconstruction strictly a compatibility fallback, or is it still relied on in normal runtime flows?  
2. Are build-origin states now intended to become a first-class domain object, or is the current coordinate-based model still the long-term plan?  
3. Is `AgentRuntime` intended to fully replace the current monolithic coordination file, or is it only a partial extraction layer?  
4. Are the stale version references in README intentionally retained for branch history, or are they an unresolved documentation bug?  
5. Which of the current constants in `AgentBackgroundService` are meant to remain code-level invariants versus runtime settings?

---

## Assumptions

- The branch snapshot may differ from current default-branch or later sprint state.
- The current crawl is incomplete at full source-tree coverage.
- Findings based on architecture docs and runtime service code are more reliable than those based only on roadmap text.
- Some roadmap items already represent future work or aspirational design, not guaranteed bugs.

---

## Confidence Summary

| Area | Confidence | Notes |
|---|---:|---|
| Documentation/version drift | 99% | Directly visible in README vs roadmap |
| Runtime coordination hotspot | 97% | Strong evidence from AgentBackgroundService |
| Planner fallback contract risk | 95% | Directly visible in PlannerRouter |
| Silent-failure risk pattern | 96% | Explicitly documented in AGENTS.md |
| Build-origin domain debt | 90% | Strong architectural signal, incomplete source audit |
| Refactoring priority | 88% | Reasoned synthesis from current evidence |

---

## Bottom Line

This branch is architecturally disciplined, but it is now at the point where further complexity should be paid down rather than layered on top. The biggest gains will come from:
1. making runtime state explicit,
2. making replan context durable,
3. centralizing build-origin policy,
4. and removing fallback-driven ambiguity from the planner pipeline.

