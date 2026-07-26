# MemorySmith.Agent Next Slice Delta Audit — Second-Order Architecture Review

Repository: TheMasonX/MemorySmith.Agent
Branch: dev/round-3
Commit reviewed: 0f27af1befb72e7534421a1bd5550aee1e077d96

Scope:

* Delta findings only
* Follow-up after prior architecture convergence audits
* Focus:

  * second-order risks
  * refactoring traps
  * maintainability
  * hidden complexity
  * long-term evolution

Confidence overall: 89%

---

# Executive Summary

Previous audits identified the major architectural problem:

> Too many components know partial truths about the same concepts.

This slice focuses on what happens next.

The risk is that the cleanup effort itself creates another layer:

```
Old duplicated systems

        ↓

New abstraction layer

        ↓

Old systems remain underneath
```

The next phase must avoid creating "architecture archaeology."

The most important principles:

1. Replace old concepts, do not wrap them forever.
2. Prefer deletion over translation layers.
3. Keep the domain model smaller than the infrastructure model.
4. Make illegal states impossible where practical.
5. Treat complexity growth as a regression.

---

# Finding 80 — Avoid creating a "Universal Agent Framework" abstraction

Severity: High

Confidence: 93%

## Problem

As consolidation begins, there is a temptation to create extremely generic abstractions:

Examples:

```text
IAgentComponent
ISystemNode
IProcessor
IContextProvider
IActionHandler
```

These look flexible but often become empty abstractions.

Risk:

The codebase moves from:

```
Many specific concepts
```

to:

```
One giant generic concept
```

This only hides complexity.

Recommendation:

Prefer domain-specific boundaries:

Good:

```csharp
IWorldCapability
IActionExecutor
IMemoryStore
IRecoveryPolicy
```

Avoid:

```csharp
IAgentService
```

Confidence: 94%

---

# Finding 81 — Consolidation needs explicit deletion checkpoints

Severity: High

Confidence: 97%

## Problem

A common failure mode:

Sprint N:

```
Add new ActionCatalog
```

Sprint N+1:

```
Keep old ToolRegistry "temporarily"
```

Sprint N+5:

```
Both are production paths
```

Recommendation:

Every replacement task must include:

```yaml
removed:
  - OldComponent
  - OldInterface
  - OldConfiguration
  - OldTests
```

A migration is incomplete until old paths disappear.

Confidence: 98%

---

# Finding 82 — Beware abstraction inversion during cleanup

Severity: High

Confidence: 91%

## Problem

When extracting interfaces, ownership can accidentally reverse.

Example:

Before:

```
Runtime
 |
WorldAdapter
```

After poorly designed cleanup:

```
Runtime
 |
IWorldService
 |
AdapterManager
 |
WorldAdapter
```

The adapter still owns the design.

Recommendation:

The highest-level layer defines the contract.

Correct:

```
Agent Domain

IWorldCapability

        ↑

Minecraft Adapter
```

Confidence: 94%

---

# Finding 83 — Separate "knowledge" from "authority"

Severity: High

Confidence: 92%

## Problem

Agent systems often confuse:

"What do we know?"

with:

"What are we allowed to do?"

Examples:

Memory may know:

```
A block exists
```

but not imply:

```
The agent should modify it
```

World state may know:

```
A location is available
```

but not imply:

```
It is safe to build there
```

Recommendation:

Separate:

```
Observation
Knowledge
Intent
Permission
Execution
```

Confidence: 95%

---

# Finding 84 — Add provenance tracking to important state

Severity: Medium

Confidence: 90%

## Problem

Autonomous systems need to answer:

"Why does the agent believe this?"

Without provenance:

```
Memory says X
```

is ambiguous.

Was it from:

* user input?
* sensor observation?
* LLM inference?
* stale cache?
* previous action?

Recommendation:

Important facts should carry:

```csharp
Provenance
{
    Source,
    Timestamp,
    Confidence,
    Version
}
```

Confidence: 91%

---

# Finding 85 — Version domain contracts explicitly

Severity: High

Confidence: 94%

## Problem

The system will evolve.

Without versioned contracts:

A change to:

* action schema
* memory format
* event payload
* adapter protocol

can silently break consumers.

Recommendation:

Version externalized contracts:

Examples:

```
ActionSchemaV1
MemoryRecordV2
ExecutionEventV1
```

Internal code can migrate gradually.

Confidence: 95%

---

# Finding 86 — Avoid hiding important behavior in extension methods

Severity: Medium

Confidence: 87%

## Problem

Extension methods improve readability but can hide architecture.

Risk:

A critical behavior appears as:

```csharp
agent.DoSomething()
```

but actually performs:

* validation
* retries
* logging
* mutation

Recommendation:

Reserve extensions for:

* convenience
* pure transformations

Avoid hiding domain workflows.

Confidence: 90%

---

# Finding 87 — Add dependency-count budgets

Severity: Medium

Confidence: 95%

## Problem

Classes that accumulate dependencies become hidden orchestrators.

Example smell:

```csharp
Constructor(
 A,
 B,
 C,
 D,
 E,
 F,
 G,
 H
)
```

Usually indicates:

* unclear ownership
* missing aggregate
* orchestration leakage

Recommendation:

Track:

* constructor dependency count
* class size
* method complexity

Use thresholds.

Confidence: 96%

---

# Finding 88 — Introduce "change amplification" reviews

Severity: Medium

Confidence: 92%

## Problem

A healthy architecture minimizes:

"How many files change for one concept?"

Measure examples:

Adding a tool should ideally modify:

1. Tool implementation
2. Registration metadata
3. Tests

Not:

1. Tool
2. Planner
3. Adapter
4. UI
5. Config
6. Registry
7. Dispatcher
8. Parser

Recommendation:

Use change amplification as an architectural metric.

Confidence: 94%

---

# Finding 89 — Treat LLM prompts as versioned software artifacts

Severity: High

Confidence: 93%

## Problem

LLM-first systems often treat prompts as strings.

They are actually behavior definitions.

Risk:

Small prompt edits silently change:

* planning behavior
* tool usage
* safety behavior
* reasoning style

Recommendation:

Version:

* prompts
* schemas
* tool descriptions
* evaluation cases

Example:

```
PlannerPromptV3
ToolSchemaV2
```

Confidence: 95%

---

# Finding 90 — Add regression scenarios, not only unit tests

Severity: High

Confidence: 94%

## Problem

Agent failures are often emergent.

Unit tests may pass while workflows fail.

Add scenario tests:

Examples:

```
Goal:
Build structure

Given:
World changes mid-plan

Expected:
Replan safely
```

```
Goal:
Collect resource

Given:
Tool unavailable

Expected:
Choose recovery path
```

Confidence: 95%

---

# Updated Strategic Priorities

## Priority 0

Prevent further architectural spread:

* no new duplicate registries
* no new compatibility branches
* no new generic managers

---

## Priority 1

Strengthen contracts:

* typed IDs
* versioned schemas
* provenance
* capability boundaries

---

## Priority 2

Improve evolution speed:

* scenario tests
* architecture tests
* deletion milestones
* change amplification tracking

---

# Final Assessment

The project has moved beyond simple refactoring needs.

The next maturity step is architectural discipline:

* fewer concepts
* clearer ownership
* stronger contracts
* deliberate deletion

The biggest future risk is not bad code.

It is good code accumulating in too many places.

Overall confidence: 89%
