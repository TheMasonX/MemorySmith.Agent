# MemorySmith.Agent Extended Audit

**Repository:** TheMasonX/MemorySmith.Agent
**Audit Target:** `ad2215edf6a51b8b452ab5946551012bdffe1db5`
**Branch Context:** `sprint-35-llm-first` lineage, but repository appears materially ahead of Sprint 35 in several areas.
**Audit Date:** 2026-06-25
**Scope:** Full architectural review with emphasis on planning, intent interpretation, execution, building, mining, runtime reliability, documentation alignment, codebase health, and maintainability.

---

# Executive Summary

After reviewing the current repository state, prior sprint handoffs, roadmap material, planner architecture, intent interpretation components, execution flow, runtime hosting layer, and supporting infrastructure, my assessment is:

**MemorySmith is no longer suffering primarily from missing features.**

The major problems now are:

1. **Architectural drift**
2. **Contract drift**
3. **Observability gaps**
4. **Implicit state assumptions**
5. **Partial migration between deterministic and LLM-driven systems**

The codebase has clearly improved compared to the earlier "vibe-coded slop" stage.

Many systems have:

* tests
* abstractions
* planners
* typed models
* separation of concerns

However, many of the current failures appear to be caused by:

> components behaving correctly in isolation but incorrectly together.

This is much harder to diagnose than simple bugs.

---

# Overall Health Assessment

| Category               | Score    |
| ---------------------- | -------- |
| Architecture           | 7.5 / 10 |
| Maintainability        | 7 / 10   |
| Reliability            | 6 / 10   |
| Planner Design         | 8 / 10   |
| Observability          | 5 / 10   |
| Runtime Recovery       | 5.5 / 10 |
| Test Coverage          | 8 / 10   |
| Documentation Accuracy | 4 / 10   |
| Future Scalability     | 8 / 10   |

---

# Highest Priority Findings

---

# Finding #1

## Documentation Has Drifted Beyond Safe Levels

### Evidence

README:

* Sprint 35 Complete
* v0.35

Roadmap:

* Sprint 41 in progress
* v0.40

These cannot both be true.

### Risk

High

### Confidence

98%

### Impact

Developers:

* implement already-completed work
* reopen solved problems
* miss active architectural decisions

### Recommendation

Single source of truth.

Generate:

* README version
* sprint status
* roadmap version

from build metadata.

---

# Finding #2

## Planner Architecture Is Stronger Than Runtime Architecture

The planner side is becoming increasingly well-structured:

* HTN planning
* goal decomposition
* planner routing
* replan results

Meanwhile execution remains heavily stateful.

Current failure pattern:

> Planner believes action succeeded.
>
> Runtime believes action started.
>
> Minecraft world disagrees.

This is the root cause of many:

* mining failures
* building failures
* inventory inconsistencies

---

# Finding #3

## Missing Observation Layer

This remains the largest architectural gap.

Current architecture:

```text
Intent
  ->
Plan
  ->
Execute
```

Missing:

```text
Intent
  ->
Plan
  ->
Execute
  ->
Observe
  ->
Verify
  ->
Replan
```

---

# Why This Matters

Current failures:

### Example 1

Agent:

> Place oak plank

Mineflayer:

* cannot reach block

Planner:

* assumes success

Execution:

* continues

Result:

build corrupted

---

### Example 2

Agent:

> Mine stone

Tool:

* path failed

Planner:

* unaware

Result:

inventory never updates

---

### Example 3

Agent:

> Build house

First block placement fails.

Planner:

* continues entire blueprint

Result:

massive cascading failure

---

# Recommendation

Introduce:

```csharp
ActionOutcome
{
    Success,
    Failed,
    PartialSuccess,
    WorldMismatch,
    InventoryMismatch,
    PathFailure
}
```

Every action should produce an outcome.

---

# Finding #4

## Current Planner Still Relies On Hidden String Contracts

Examples:

```text
GatherItem:oak_log
Build:house
CraftItem:stick
```

and:

```text
build:{blueprint}:origin:x
```

These are hidden APIs.

---

# Why This Is Dangerous

Refactor one side:

```csharp
GatherItem
```

into

```csharp
Gather
```

and planner silently breaks.

---

# Recommendation

Convert immediately after parsing:

```csharp
GoalRequest
```

↓

```csharp
GatherGoal
```

↓

Typed execution

---

# Confidence

92%

---

# Finding #5

## Build Origin Is Still Architecturally Fragile

Current design allows:

```text
originX
originY
originZ
```

independently.

A caller can accidentally provide:

```text
originX
originY
```

without:

```text
originZ
```

and still trigger explicit-origin logic.

---

# Better Design

```csharp
record BuildOrigin(
    int X,
    int Y,
    int Z);
```

Either:

* valid
* absent

No partial states.

---

# Confidence

84%

---

# Finding #6

## Chat Routing Has Become Internally Inconsistent

Observed pattern:

Configuration exposes:

```text
MaxResponseDistanceBlocks
```

Interpreter no longer appears to honor it.

Behavior instead depends on:

* bot name
* recent conversation
* solo-player detection

---

# Risk

Operators tune a setting that does nothing.

---

# Recommendation

Either:

### Option A

Re-enable distance gating.

or

### Option B

Delete setting.

---

# Confidence

96%

---

# Finding #7

## Bot Name Detection Is Too Permissive

Current approach resembles:

```csharp
Contains(botName)
```

This causes false positives.

Example:

Bot:

```text
Leo
```

Message:

```text
helios is cool
```

Bot may think:

```text
leo
```

appears.

---

# Better

Word boundaries.

```regex
\bLeo\b
```

---

# Confidence

92%

---

# Finding #8

## Runtime Is Fail-Soft But Not Diagnostic

Several LLM/provider paths:

```text
catch
log warning
return null
```

This is acceptable for uptime.

Not acceptable for diagnosis.

---

# Better

```csharp
Result<T>
```

or

```csharp
FailureReason
```

---

Example:

```csharp
Timeout
```

vs

```csharp
ProviderUnavailable
```

vs

```csharp
InvalidResponse
```

---

# Confidence

85%

---

# Critical Architectural Opportunity

## Introduce Execution Verification

This would likely solve more user-visible issues than any planner rewrite.

---

Current:

```text
Planner
  ->
Action
```

Proposed:

```text
Planner
  ->
Action
  ->
Verification
```

---

Example

### Place Block

Verify:

```text
Expected:
oak_planks
```

Check world.

If mismatch:

```text
PlacementFailed
```

---

### Mine Block

Verify:

Inventory increased.

If not:

```text
MiningFailed
```

---

### Move To

Verify:

Distance threshold reached.

If not:

```text
PathFailed
```

---

# Confidence

99%

This is likely the single highest ROI architectural improvement.

---

# Critical Architectural Opportunity

## Replace Action Completion With Goal Satisfaction

Current architecture appears action-centric.

Example:

```text
Mine oak log
```

↓

Action complete.

But what matters is:

```text
Do we have oak log?
```

---

Goal-centric systems are more resilient.

---

Example

Need:

```text
16 cobblestone
```

Mine action fails.

System checks:

```text
Inventory?
```

If already has 16:

Goal satisfied.

Continue.

No failure.

---

This dramatically reduces brittleness.

---

# Mining System Assessment

Based on:

* historical handoffs
* sprint plans
* architecture

Mining failures appear to originate from:

### 1

World state stale.

### 2

Inventory stale.

### 3

Tool completion assumptions.

### 4

Pathfinding success assumptions.

### 5

No post-action verification.

---

# Building System Assessment

Most likely root causes:

### 1

Origin selection ambiguity

### 2

Block placement verification missing

### 3

Chunk loading assumptions

### 4

Reachability assumptions

### 5

Blueprint execution assumes previous step succeeded

---

# Intent System Assessment

The move toward LLM-first interpretation is correct.

Confidence:

95%

---

Reason:

Minecraft commands are naturally fuzzy.

Examples:

```text
make a hut
```

```text
build a bigger one
```

```text
go get wood
```

Rigid regex systems become exponential.

---

Recommended Architecture

```text
Chat
  ->
LLM Intent
  ->
Structured Intent
  ->
Validation
  ->
Planner
```

NOT

```text
Chat
  ->
Regex
  ->
Regex
  ->
Regex
  ->
Planner
```

---

# Matt Pocock Style Architecture Recommendations

If optimizing for long-term maintainability:

---

## Introduce Domain Layer

Current:

```text
Planner
Execution
Minecraft
UI
```

intermixed.

Create:

```text
Domain
```

containing:

```csharp
Goal
Plan
Action
Observation
Outcome
WorldState
InventoryState
```

Everything else becomes adapters.

---

## Introduce Result Types

Replace:

```csharp
null
```

with:

```csharp
Result<T>
```

---

## Introduce State Snapshots

Current systems frequently ask:

```text
What is inventory?
```

Use immutable snapshots.

```csharp
WorldSnapshot
InventorySnapshot
AgentSnapshot
```

---

## Introduce Event Stream

Instead of:

```text
Planner directly mutates state
```

Use:

```text
ActionStarted
ActionSucceeded
ActionFailed
ObservationReceived
GoalSatisfied
GoalFailed
```

This would massively improve debugging.

---

# Top 10 Improvements By ROI

## 1

Execution verification layer

Confidence: 99%

---

## 2

Goal satisfaction checks

Confidence: 97%

---

## 3

Typed BuildOrigin

Confidence: 95%

---

## 4

Result<T> instead of null

Confidence: 94%

---

## 5

Remove dead configuration

Confidence: 96%

---

## 6

Observation-driven replanning

Confidence: 95%

---

## 7

Single source of truth for docs

Confidence: 98%

---

## 8

Replace string contracts

Confidence: 92%

---

## 9

Event stream for execution

Confidence: 90%

---

## 10

Whole-word bot detection

Confidence: 92%

---

# Final Assessment

The repository is no longer in the category of "start over."

I would strongly advise **against a rewrite**.

The underlying architecture is actually becoming fairly solid.

The major issue now is that the system is transitioning from:

```text
deterministic scripted bot
```

to

```text
goal-driven autonomous agent
```

and the observation/recovery layer has not fully caught up.

If I had to choose only **one** improvement to prioritize next, it would be:

> Build a formal Observation → Verification → Replan loop and make every action prove it succeeded.

That single change would likely eliminate a large percentage of the mining failures, building failures, stale inventory issues, incorrect plan continuation, and silent runtime divergence currently reported throughout the sprint handoffs and audit documents.
