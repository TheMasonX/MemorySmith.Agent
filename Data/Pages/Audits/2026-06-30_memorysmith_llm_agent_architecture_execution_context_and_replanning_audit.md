# MemorySmith.Agent — LLM Agent Architecture, Execution Context, and Replanning Audit

**Suggested filename**

`2026-06-30_memorysmith_llm_agent_architecture_execution_context_and_replanning_audit.md`

**Repository:** MemorySmith.Agent

**Commit reviewed:** `5af0d749d316587d8f32c805e310de84e62a5fa1`

---

# Executive Summary

This review focused on answering the remaining architectural questions after the recent planning and sequence work and validating the runtime call chains. The latest commit meaningfully improves the architecture by fixing sequence progression and improving LLM evaluation, but it also highlights where the next architectural investment should occur.

The major conclusion from this review is that **the project has reached the point where adding additional planner features will produce diminishing returns until execution state is consolidated.**

## Executive decision

**ExecutionContext has been explicitly approved by the human reviewer and selected as the next major architectural investment.**

This should become the central execution object shared across the entire runtime.

Everything else in this report supports that direction.

---

# Executive Assessment

Current maturity:

| Area                    | Assessment                        |
| ----------------------- | --------------------------------- |
| Deterministic execution | Excellent foundation              |
| Tool architecture       | Good and improving                |
| Typed goal system       | Good                              |
| Planner architecture    | Good                              |
| LLM integration         | Good foundation                   |
| Adaptive replanning     | Partial                           |
| Long horizon autonomy   | Limited                           |
| Legacy technical debt   | Moderate                          |
| Architectural cohesion  | Good, but ready for consolidation |

Overall confidence:

**94%**

---

# Open Questions — Closed

## Can the agent perform multiple tasks?

Yes.

The current implementation genuinely supports bounded multi-step execution.

The runtime now supports

* Intent
* GoalRequest
* GoalFactory
* TaskSequenceGoal
* sequential execution

rather than simply executing isolated commands.

The recent TaskSequenceGoal fix is significant because it removes the completion bug that previously prevented proper advancement.

Confidence:

**96%**

---

## Does "Next Steps" actually work?

Yes.

The call chain is now complete.

The flow is:

```
LLM

↓

IntentDraft.NextSteps

↓

IntentManager

↓

GoalRequests

↓

GoalFactory

↓

TaskSequenceGoal

↓

Action Queue

↓

Execution
```

Previously the sequence completion logic prevented reliable advancement.

The latest commit fixes that.

Confidence:

**95%**

---

## Does the agent actually adapt?

Yes—

**but only within bounded conditions.**

The runtime now performs:

```
Execute

↓

Observe

↓

Update WorldState

↓

Compare

↓

Evaluate

↓

Potential Replan
```

This is genuine adaptive behaviour.

However...

The current implementation primarily replans after

* stalls

* failures

* recovery conditions

rather than continuously evaluating every observation.

This is the correct direction but is still conservative.

Confidence:

**91%**

---

## Is the new LLM evaluator behaviour actually used?

Mostly.

The evaluator improvements are real.

However the new structured parse status currently improves observability more than behaviour.

The runtime still primarily follows

```
ShouldReplan
```

rather than richer structured reasoning based upon failure metadata.

This is a good incremental step but not the final architecture.

Confidence:

**83%**

---

# New Findings

## 1. The architecture has outgrown AgentBackgroundService

This was the strongest finding from the runtime call chain review.

AgentBackgroundService currently owns too many concerns.

It currently coordinates:

* planning
* execution
* replanning
* creative provisioning
* dispatch
* recovery
* inventory synchronization
* world updates
* UI state
* queue management
* evaluation

It has become the de facto runtime.

That is no longer sustainable.

This should become an orchestration layer only.

---

## 2. Execution state still has multiple owners

This is now the largest architectural issue.

Execution state currently exists in pieces across:

* planner
* world state
* goal
* queue
* recovery
* evaluator
* dashboard
* adapter

Every one of these reconstructs part of "what is happening."

That duplication is now the largest source of complexity.

---

## 3. The project is ready for ExecutionContext

### **This recommendation has been explicitly approved by the human reviewer.**

ExecutionContext should become the canonical runtime object.

It should replace scattered execution state.

Recommended ownership:

```
ExecutionContext

Intent

Goal

Plan

Current Step

Action Queue

World Snapshot

Inventory Snapshot

Recent Observations

Tool Results

Action Outcomes

Memory Retrievals

Correlation IDs

Cycle Metadata

Planner Metadata

Safety Policy

Creative / Survival Mode

Replanning History
```

Every subsystem should receive ExecutionContext.

Every subsystem should update ExecutionContext.

No subsystem should reconstruct execution state independently.

This becomes the single source of truth.

---

# Recommended Future Runtime

```
User

↓

Intent

↓

Goal

↓

Planner

↓

PlanGraph

↓

ExecutionContext

↓

Tool Invocation

↓

Observation

↓

World Diff

↓

Evaluation

↓

Replan

↓

Continue
```

ExecutionContext remains alive throughout.

Everything references it.

Nothing duplicates it.

---

# Additional Major Architectural Recommendation

## Introduce PlanGraph

TaskSequenceGoal is an excellent stepping stone.

It should not become the permanent abstraction.

Instead introduce

```
PlanGraph
```

A PlanGraph would consist of

Nodes

Edges

Success transitions

Failure transitions

Optional branches

Parallel branches

Cancellation

Recovery nodes

Completion nodes

This would allow

Skip step

Retry step

Insert step

Replace branch

Collapse branch

Resume

without inventing more special cases.

---

# ObservationEnvelope

Today observations are spread across

WorldState

Errors

Tool Results

Logs

Recovery

Instead introduce

```
ObservationEnvelope
```

Containing

Observed world changes

Tool outputs

Failures

Warnings

Structured adapter events

Inventory deltas

World diffs

Unexpected conditions

Planner annotations

Evaluation consumes ObservationEnvelope.

Not dozens of unrelated values.

---

# Deterministic Tool Philosophy

The future agent should continue moving toward

LLM reasons.

Tools know facts.

Examples of deterministic tools:

Inventory

Nearby blocks

Nearby entities

Recipes

Craft graph

Path availability

Reachability

Biome

Dimension

Structures

Memory

Tasks

Known failures

Recent observations

Goal history

Everything observable should become a deterministic tool.

The LLM should synthesize—not invent.

---

# Remove Legacy Shims

The following should be actively removed.

## Interpreter fallback

IntentManager should become mandatory.

Remove local fallback parsing.

---

## Regex command expansion

Eventually replace

```
ParseCommandString()
```

with structured NextStep objects.

---

## Recovery parsing

Replace string parsing with structured adapter error types.

---

## Creative provisioning split

Creative should use exactly one provisioning system.

No duplicated ownership.

---

## Compatibility layers

Every compatibility bridge should have one of three labels

Temporary

Permanent

Scheduled for deletion

Anything marked Temporary should have a removal task.

---

# New Bug / Improvement Opportunities

## WorldStateDiff is underutilized

The runtime computes useful execution differences.

These should become first-class planner inputs.

Instead of asking

"What happened?"

the evaluator should receive

"What changed?"

---

## Execution history should become structured

Instead of scattered logs:

ExecutionContext should contain

Previous attempts

Failures

Recovered failures

Replans

Completed actions

Cancelled actions

Timeouts

This dramatically improves replanning quality.

---

## Correlation IDs should become universal

Everything should carry

ExecutionId

PlanId

GoalId

ActionId

ObservationId

ToolInvocationId

MemoryLookupId

This makes debugging dramatically easier.

---

# Architectural Ownership

Every major concept should have exactly one owner.

| Concept             | Owner               |
| ------------------- | ------------------- |
| Intent              | IntentManager       |
| Goal creation       | GoalFactory         |
| Planner selection   | PlannerRouter       |
| Planning            | Planner             |
| Plan representation | PlanGraph (future)  |
| Runtime state       | ExecutionContext    |
| Observations        | ObservationEnvelope |
| Execution           | Execution Engine    |
| Evaluation          | LlmEvaluator        |
| Memory              | Memory subsystem    |
| World model         | WorldState          |
| Tool registry       | Capability Registry |

If ownership cannot be answered in one sentence, it probably needs consolidation.

---

# Cross-reference Against Existing TSKs

This review does **not** recommend duplicating work already tracked.

Existing work already covers:

* Multi-step chaining (`TSK-0205`)
* Sequence completion fixes
* LLM evaluator improvements
* Safety configuration
* Legacy HTN cleanup
* Queue synchronization
* Compatibility bridge review

These tasks should continue to completion.

## Recommended New Architecture Tasks

The following appear to be sufficiently distinct to justify new architecture tasks:

### New TSK — Introduce ExecutionContext

Highest priority architectural task.

### New TSK — Introduce PlanGraph

Replace long-term reliance on TaskSequenceGoal.

### New TSK — Introduce ObservationEnvelope

Centralize execution observations.

### New TSK — Introduce Capability Registry

Single authoritative registry for deterministic tools.

### New TSK — Runtime Ownership Consolidation

Reduce AgentBackgroundService into a pure orchestration layer.

---

# Final Recommendation

The project has reached an inflection point.

The next generation of improvements should focus less on adding isolated planner features and more on strengthening the execution model itself.

The recommended direction is:

1. **ExecutionContext** becomes the single synchronized runtime state and has been **explicitly approved by the human reviewer for implementation**.
2. Introduce a **PlanGraph** to evolve beyond bounded linear sequences.
3. Consolidate observations into an **ObservationEnvelope** for deterministic replanning.
4. Expand deterministic capabilities through a **Capability Registry** so the LLM synthesizes from authoritative data rather than inferred state.
5. Continue eliminating compatibility layers, parser fallbacks, duplicated ownership, and other legacy constructs as soon as a canonical implementation exists.

This path preserves the strengths of the current deterministic architecture while enabling a significantly more capable autonomous agent that can plan, observe, adapt, and recover over extended task horizons without accumulating technical debt.
