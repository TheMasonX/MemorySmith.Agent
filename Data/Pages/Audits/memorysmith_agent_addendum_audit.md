**Suggested file name:** `memorysmith_agent_addendum_audit.md`

---

# MemorySmith.Agent Audit Addendum

## Additional Architectural Findings and Improvement Opportunities

**Branch:** `dev/round-3`

This report captures additional findings after reviewing the creative-mode regression and `AgentBackgroundService` decomposition work. The focus is on long-term architecture, eliminating technical debt, and reducing future regression risk.

---

# Executive Summary

The project is continuing to move in the correct architectural direction.

The decomposition work (PlannerRouter, decomposers, modular Mineflayer adapter, RecoveryManager abstraction, etc.) is all pointing toward a much cleaner architecture than the original prototype.

However, there are still several places where the runtime effectively has **multiple sources of truth**, and these are becoming the primary source of regressions.

The largest architectural risks are now:

1. Inventory state is reconstructed from several partially-authoritative sources.
2. Creative vs Survival behavior is still implemented as scattered special cases rather than a first-class execution policy.
3. `AgentBackgroundService` still owns too much mutable runtime state.
4. Recovery logic exists behind an abstraction but is still implemented inside the god class.
5. Planner compatibility layers are beginning to outlive their intended purpose.

None of these require major rewrites.

They require consolidation.

---

# Finding 1 — Inventory Still Has No Single Source of Truth

**Severity:** Critical

**Confidence:** **96%**

This is becoming the root cause behind multiple seemingly unrelated bugs.

Current inventory information is reconstructed from combinations of:

* ItemCollected events
* BlockMined events
* Status snapshots
* Planned actions
* Internal assumptions
* Creative provisioning

Each one is "mostly right."

None of them is actually authoritative.

That means every subsystem eventually accumulates tiny disagreements.

Those disagreements eventually become bugs like:

* planner thinks item exists
* adapter disagrees
* recovery thinks item is missing
* planner starts gathering
* creative mode tries mining

All of these stem from inventory disagreement.

## Recommendation

Create one service whose entire job is:

```
InventoryStateService
```

Responsibilities:

* consume slot updates
* consume reconciliation/status snapshots
* expose immutable inventory snapshots
* publish inventory changed events
* detect drift

Nothing else should own inventory.

Everything else should query it.

---

# Finding 2 — Creative Should Become an Execution Capability

**Severity:** Critical

**Confidence:** **95%**

Currently code frequently asks

```
if (IsCreativeMode)
```

throughout the system.

That is backwards.

Creative is **not** simply a world property.

Creative changes what actions are legal.

Instead create something conceptually like

```
ExecutionCapabilities
```

Example

```
CanMineBlocks

CanInstantBreak

CanSpawnItems

NeedsGathering

ConsumesDurability

ConsumesBlocks

NeedsFood

NeedsFuel
```

Now planners don't ask

```
Am I creative?
```

They ask

```
CanSpawnItems?
```

or

```
NeedsGathering?
```

That scales much better if later you support

* operator mode
* custom servers
* adventure
* spectator
* mods
* command permissions

without scattering more booleans.

---

# Finding 3 — Runtime State Ownership Is Still Fuzzy

**Severity:** High

**Confidence:** **93%**

AgentBackgroundService still owns dozens of mutable fields.

Examples include

* current goal
* active plan
* action correlation
* retry counts
* planner state
* recovery state
* build context
* timeout tracking
* UI status

Those are all different domains.

The problem isn't the number of fields.

The problem is that ownership is unclear.

Recommendation:

Split runtime state into immutable domain objects.

For example

```
ExecutionState

RecoveryState

PlanningState

ConnectionState

InventoryState

BuildState
```

Each service owns exactly one.

Everyone else receives snapshots.

---

# Finding 4 — Recovery Still Exists As Behavior Instead Of Policy

**Severity:** High

**Confidence:** **95%**

RecoveryManager currently exists as an abstraction.

However:

actual recovery decisions still live elsewhere.

That means recovery behavior cannot evolve independently.

Recovery should become

```
Current failure

↓

Recovery policy

↓

Recovery plan

↓

Planner executes
```

instead of

```
failure

↓

AgentBackgroundService

↓

special cases

↓

planner
```

Recovery should never directly create gameplay actions.

Recovery should only decide

* retry
* abandon
* replan
* gather
* move
* wait
* fail

The planner decides how.

---

# Finding 5 — Planner Is Becoming Correct Faster Than HTN Compatibility

**Severity:** Medium

**Confidence:** **89%**

PlannerRouter is clearly becoming the canonical planner.

The HTN planner increasingly exists only for fallback.

This is healthy.

However.

Every compatibility branch left inside HTN increases maintenance cost.

Recommendation:

Instrument every HTN fallback.

Example

```
PlannerFallbackUsed

Goal

Reason

Missing decomposer

Timestamp
```

Once metrics show zero usage,

delete the fallback.

Don't leave compatibility forever.

---

# Finding 6 — Event Processing Should Become Pure

**Severity:** High

**Confidence:** **91%**

Current event processing both

interprets events

and

changes runtime state.

Instead,

events should become pure transformations.

Example

```
Mineflayer

↓

Raw Event

↓

Translator

↓

Domain Event

↓

Reducer

↓

New immutable state

↓

Subscribers
```

That dramatically reduces hidden side effects.

---

# Finding 7 — Build Pipeline Still Owns Too Much Domain Knowledge

Current build execution understands

* materials
* placement
* retries
* failures
* progress
* completion

Those are separate concerns.

Instead think

```
Build Intent

↓

Material Resolver

↓

Placement Planner

↓

Execution Scheduler

↓

Placement Executor

↓

Progress Observer
```

Each piece becomes independently testable.

---

# Finding 8 — State Projection And Reality Are Mixed Together

There are two kinds of state.

## Reality

Actual server state.

Inventory.

Position.

Game mode.

Health.

Chunks.

## Projection

Planner expectations.

Expected inventory.

Expected build progress.

Expected completed actions.

These should never live in the same object.

Instead

```
ObservedWorldState

ExpectedWorldState
```

Then reconciliation becomes explicit instead of hidden.

---

# Finding 9 — Replace Flags With Capabilities

Several booleans are beginning to appear throughout the runtime.

Instead of

```
IsCreative

IsFlying

IsHungry

IsBuilding
```

prefer

```
AgentCapabilities

AgentStatus

AgentExecutionMode
```

Those naturally evolve.

Flags multiply.

Capabilities compose.

---

# Finding 10 — Runtime Services Should Become Mostly Stateless

Long-term architecture should resemble

```
AgentRuntime

├── Planner
├── Recovery
├── Execution
├── Inventory
├── Memory
├── Navigation
├── Build
├── Crafting
├── Mining
├── Communication
└── WorldProjection
```

Each service

takes

```
State

Input

↓

Decision

↓

Output
```

Very little mutable state.

Mostly deterministic.

That dramatically simplifies debugging.

---

# Suggested Refactor Order

## Phase 1

Inventory truth

* implement updateSlot
* authoritative inventory service
* reconciliation metrics

Highest priority.

---

## Phase 2

Creative policy

Replace scattered checks with

```
ExecutionCapabilities
```

---

## Phase 3

Recovery extraction

Move all recovery decisions into `RecoveryManager`.

AgentBackgroundService becomes caller only.

---

## Phase 4

Execution state

Extract

* planning
* execution
* recovery
* connection
* inventory

into dedicated runtime state services.

---

## Phase 5

Event pipeline

Convert event processing into

```
Raw Event

↓

Translator

↓

Reducer

↓

Immutable State

↓

Observers
```

---

## Phase 6

HTN retirement

Add telemetry.

Delete compatibility branches once unused.

---

# New Task Recommendations

These do not appear to duplicate the current backlog and would complement the existing work:

| Priority | Task                                                    | Rationale                                                                                                                       |
| -------- | ------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| P0       | Introduce `ExecutionCapabilities` model                 | Unifies creative/survival behavior and removes scattered mode checks.                                                           |
| P0       | Create authoritative `InventoryStateService`            | Establishes a single source of truth for inventory across planner, adapter, and recovery.                                       |
| P1       | Separate `ObservedWorldState` from `ExpectedWorldState` | Makes planner assumptions explicit and simplifies reconciliation.                                                               |
| P1       | Instrument planner fallback usage                       | Provides objective evidence for safely removing HTN compatibility code.                                                         |
| P2       | Introduce immutable runtime state containers            | Clarifies ownership and reduces synchronization complexity.                                                                     |
| P2       | Refactor event processing into translators and reducers | Makes event handling deterministic and easier to test.                                                                          |
| P3       | Create architecture fitness tests                       | Automatically detect new dependencies on `AgentBackgroundService`, duplicate creative logic, or new sources of inventory truth. |

---

# Overall Assessment

My confidence has increased that the project is converging on a strong architecture. The major regressions I'm seeing are less about implementation bugs and more about **architectural drift**, where behavior is split across multiple partially-complete systems during the transition away from legacy code.

If I had one overarching recommendation, it would be this:

> **Every important concept in the runtime should have exactly one owner.**

Right now, concepts like **inventory**, **creative behavior**, **recovery policy**, and **runtime state** each have multiple owners. Consolidating those ownership boundaries will eliminate an entire class of regressions and make future feature work substantially safer.
