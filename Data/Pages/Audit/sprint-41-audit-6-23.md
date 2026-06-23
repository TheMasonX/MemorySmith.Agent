# MemorySmith.Agent Deep Audit

**Branch:** `sprint-35-llm-first`
**Repository:** [MemorySmith.Agent](https://github.com/TheMasonX/MemorySmith.Agent?utm_source=chatgpt.com)
**Architecture Reference:** [Matt Pocock - Improve Codebase Architecture Skill](https://github.com/mattpocock/skills/blob/main/skills/engineering/improve-codebase-architecture/SKILL.md?utm_source=chatgpt.com)

---

# Executive Summary

After reviewing the sprint-35 LLM-first branch, the most important conclusion is:

> The project is no longer suffering primarily from parser brittleness.
>
> It is now suffering from architectural diffusion of responsibility.

The branch is significantly healthier than the versions you described previously where command parsing, goal creation, decomposition, and execution were all mixed together.

The LLM-first direction is correct.

However, the codebase is currently in what I would call a **transitional architecture state**:

* The old deterministic architecture is not fully removed.
* The new LLM-first architecture is not fully centralized.
* Multiple layers still know too much about planning.
* Build planning logic exists in several places simultaneously.
* Minecraft execution logic still leaks implementation details upward into planning.

As a result:

* adding features remains difficult
* changing behavior remains risky
* bug fixing frequently requires touching multiple layers

This is exactly the type of architectural condition Matt Pocock typically identifies as:

> "The abstraction exists, but responsibility has not fully migrated."

---

# Overall Assessment

| Area                  | Rating |
| --------------------- | ------ |
| LLM Architecture      | 8.5/10 |
| Planning Architecture | 6/10   |
| Tool Architecture     | 7/10   |
| Build System          | 5.5/10 |
| Mineflayer Adapter    | 7/10   |
| Extensibility         | 6/10   |
| Codebase Health       | 7/10   |
| Sprint Direction      | 9/10   |

---

# Biggest Finding

## You Are Solving The Correct Problem

The sprint direction is correct.

Earlier versions attempted:

```text
Player Message
    ↓
Regex
    ↓
Goal
    ↓
Planner
```

Sprint 35 is moving toward:

```text
Player Message
    ↓
LLM
    ↓
IntentDraft
    ↓
IntentManager
    ↓
Goal
    ↓
Planner
```

This is dramatically better.

Why?

Because Minecraft language is inherently fuzzy:

Examples:

```text
Get me some wood
Grab oak logs
Need logs
Can you gather 16 oak?
Let's collect wood
```

Trying to maintain deterministic parsing for all of these becomes impossible.

The LLM should own:

* language understanding
* normalization
* ambiguity resolution

The planner should own:

* execution

Those responsibilities are finally separating.

**Confidence: 95%**

---

# Major Architectural Problem

## Planning Responsibility Exists In Too Many Places

Current architecture roughly looks like:

```text
ChatInterpreter

LlmChatInterpreter

IntentManager

GoalFactory

HtnPlanner

BuildGoalDecomposer
```

All of these contain planning knowledge.

That is the root architectural smell.

---

## Example

Build origin handling appears in:

### IntentManager

Interprets coordinates.

### GoalFactory

Creates BuildGoal.

### HtnPlanner

Reads build coordinates again.

### Decomposer

Potentially resolves placement.

---

This means:

```text
Build Location Logic
```

exists in four different places.

That violates:

> Single Source Of Truth

and creates drift.

---

# Recommended Architecture

Move toward:

```text
IntentDraft
    ↓
GoalRequest
    ↓
GoalFactory
    ↓
Goal
    ↓
Decomposer
    ↓
Primitive Actions
```

Where:

```text
IntentManager
```

knows nothing about Minecraft.

and

```text
GoalFactory
```

knows nothing about execution.

and

```text
HtnPlanner
```

knows nothing about build semantics.

---

# Critical Bug #1

## BuildGoalRequest Origin Validation Bug

I found a real correctness bug.

The build request validation checks:

```csharp
OriginX
OriginZ
OriginZ
```

instead of:

```csharp
OriginX
OriginY
OriginZ
```

Result:

```text
X = 100
Y = null
Z = 50
```

can pass validation.

Downstream:

```text
Build Goal Created
```

with invalid coordinates.

This can create:

* bad placement plans
* fallback behavior
* strange build origin selection

**Severity:** High

**Confidence:** 97%

---

# Critical Bug #2

## Place Action Validation

Mineflayer adapter:

```js
if (x == null || !material)
```

But later:

```js
GoalNear(x, y, z)
```

is called.

Meaning:

```json
{
  "x": 10,
  "material": "dirt"
}
```

passes validation.

Then crashes later.

Should be:

```js
if (
    x == null ||
    y == null ||
    z == null ||
    !material
)
```

**Severity:** High

**Confidence:** 98%

---

# Architectural Smell

## HtnPlanner Knows Too Much

The planner still contains:

```text
BuildGoal
CraftGoal
ItemSpecGoal
```

special handling.

This means:

```text
Planner
```

is not actually generic.

It understands specific goal types.

That creates coupling.

---

Better:

```text
Goal
    ↓
Decomposer Registry
    ↓
Planner
```

Planner shouldn't care what goal it receives.

---

# Mineflayer Findings

This area has improved significantly.

---

## Good Improvements

I found:

### Emergency Stop

Immediate interrupt.

Good.

---

### Authentication Handshake

WebSocket token support.

Good.

---

### Structured Logging

Much improved.

Good.

---

### Mining Retry Exhaustion

Prevents infinite loops.

Good.

---

### Item Pickup Events

Huge improvement.

Previously:

```text
Mined block
```

was assumed to mean:

```text
Inventory increased
```

Now pickup is tracked.

Much better.

---

# Remaining Mineflayer Problems

## Adapter Still Acts Like A Tool Layer

Current architecture:

```text
Planner
   ↓
Mine
   ↓
Adapter decides strategy
```

Example:

```js
findBestBlock()
```

contains:

* mining heuristics
* alias handling
* Y-level preferences
* search policies

This is planning logic.

Not execution logic.

---

Adapter should become:

```text
Minecraft Driver
```

not:

```text
Minecraft Planner
```

---

# The Build System Is Still The Weakest Area

This matches your observations.

---

## Why Building Feels Bad

Current build system still appears to operate like:

```text
BuildGoal
    ↓
Find Area
    ↓
Place Blocks
```

rather than:

```text
BuildGoal
    ↓
Construction Plan
    ↓
Execution
```

Missing layer:

```text
Build Plan
```

---

# Recommended New Build Architecture

Introduce:

```csharp
ConstructionPlan
```

Example:

```csharp
public sealed record PlacementStep(
    string Block,
    int X,
    int Y,
    int Z);
```

Then:

```text
Blueprint
    ↓
ConstructionPlan
    ↓
Executor
```

Instead of:

```text
Blueprint
    ↓
Place Blocks Immediately
```

---

This would dramatically simplify debugging.

---

# Biggest Long-Term Improvement

## Remove Goal Knowledge From Chat Layer

Currently:

```text
Chat
    ↓
Intent
    ↓
Goal Types
```

Still somewhat coupled.

Instead:

```text
Chat
    ↓
IntentDraft
    ↓
GoalRequest DTO
    ↓
GoalFactory
```

The chat layer should never know:

```text
BuildGoal
CraftGoal
MineGoal
```

exist.

Only:

```text
Intent
```

---

# LLM Integration Recommendation

You asked:

> Would fallback to LLM parsing solve much of the brittleness?

Answer:

## Yes

But only if used correctly.

Bad:

```text
Regex
fails
↓
LLM
```

Good:

```text
LLM
↓
Structured Intent
↓
Validation
```

The branch is already moving in this direction.

Continue.

---

# What I Would Prioritize Next

## Priority 1

Fix correctness bugs.

* BuildGoalRequest validation
* Place validation

Effort: 1 hour

Impact: High

---

## Priority 2

Centralize build origin resolution.

Create:

```csharp
IBuildOriginResolver
```

Single source of truth.

Remove origin logic elsewhere.

Impact: Very High

---

## Priority 3

Remove planner knowledge from HtnPlanner.

Move to:

```csharp
IGoalDecomposer
```

registry.

Impact: High

---

## Priority 4

Create ConstructionPlan layer.

Impact: Extremely High

This likely solves many of the "building feels wrong" issues.

---

## Priority 5

Move Mineflayer strategy logic upward.

Adapter becomes:

```text
Driver
```

instead of:

```text
Planner
```

Impact: Medium-High

---

# What I Think Is Actually Causing The Remaining "Agent Feels Dumb" Behavior

Based on the branch and your previous descriptions, I do **not** think the biggest issue is the LLM anymore.

I think the biggest issue is:

```text
Intent understood correctly
        ↓
Goal created correctly
        ↓
Execution layer lacks enough world model
```

Specifically:

* insufficient construction planning
* insufficient environment representation
* execution heuristics embedded in adapter
* build location resolution spread across layers
* action feedback not rich enough

The system appears to understand *what* to do more often than before.

It still struggles with *how* to do it reliably.

That distinction matters because it means continuing to invest heavily in parsing will likely yield diminishing returns, whereas improving planning, construction plans, world representation, and execution feedback will likely produce much larger gains.

## Final Confidence

| Finding                                                         | Confidence |
| --------------------------------------------------------------- | ---------- |
| LLM-first direction is correct                                  | 95%        |
| BuildGoalRequest bug exists                                     | 97%        |
| Place validation bug exists                                     | 98%        |
| Planning responsibility is too distributed                      | 90%        |
| Build system remains primary weakness                           | 88%        |
| Mineflayer still contains planning logic                        | 92%        |
| ConstructionPlan layer would significantly improve architecture | 85%        |
| Remaining failures are more execution/modeling than parsing     | 90%        |

The branch is substantially healthier than the versions you described a few days ago, but it is still in the middle of an architectural migration. The fastest path to a more robust agent is probably **finishing the migration** (centralized intent → goal → decomposer pipeline and a real construction-planning layer) rather than adding more special-case fixes.
