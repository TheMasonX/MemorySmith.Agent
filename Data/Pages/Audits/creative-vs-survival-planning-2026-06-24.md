# Creative / Survival Execution Architecture

## Corrected assumptions

The right model is **not** “creative mode is survival with a few shortcuts.” It is a different execution profile with different invariants:

Creative mode may directly provision items, place blocks, and fly. Survival mode must obey tool tiers, acquisition costs, inventory pressure, and physical travel constraints.

That means the split should live in **policy selection**, not in scattered `if (creative)` checks. In the current code, creative behavior already leaks into planning via `HtnPlanner`’s `state.IsCreativeMode` build branch, while build-origin logic is separately owned by `BuildGoalDecomposer`, and inventory-staleness recovery is handled in `AgentBackgroundService`. `WorldStateProjector` is the canonical place where `gameMode` is ingested from status updates, and `ToolDispatcher` remains the proper safety boundary for validating and executing tool calls.

## Design goal

Make creative and survival paths **first-class execution profiles** with shared goal semantics and separate policy implementations, so that:

* goals stay stable,
* execution strategy becomes swappable,
* special cases disappear over time,
* new modes or server rules can be added without rewriting planners.

---

## Proposed architecture

### 1) Canonical execution profile

Create one resolved object from `WorldState`:

```csharp
public enum ExecutionMode
{
    Creative,
    Survival,
    Adventure,
    Spectator
}

public sealed record ExecutionProfile(
    ExecutionMode Mode,
    bool CanFly,
    bool CanGrantItems,
    bool CanBypassToolRequirements,
    bool CanBypassInventoryCapacity,
    bool CanDirectPlaceWithoutMaterials,
    HarvestPolicy HarvestPolicy,
    BuildPolicy BuildPolicy,
    MobilityPolicy MobilityPolicy,
    InventoryPolicy InventoryPolicy,
    ResourceProvisionPolicy ResourceProvisionPolicy);
```

`WorldState` already has the game mode coming from adapter status and projector updates, so `ExecutionModeResolver` can be a pure translation layer. The adapter already emits `gameMode` in status, and `WorldStateProjector.ApplyStatus` stores it.

### 2) One resolver, zero distributed mode checks

Add:

```csharp
public interface IExecutionModeResolver
{
    ExecutionProfile Resolve(WorldState state);
}
```

This resolver is the only place that looks at game mode and turns it into capabilities.

Goals, decomposers, and planners never ask “am I creative?” directly. They ask:

* can I fly?
* can I provision items?
* can I ignore tool tiers?
* can I place directly?
* must I stash before continuing?

That removes the mode split from domain logic and makes it a capability query instead of an ownership problem.

### 3) Policy-based execution

Replace mode-specific branches with policies.

#### Resource provision

```csharp
public interface IResourceProvisionPolicy
{
    Task<ActionPlan> EnsureResourcesAsync(ResourceRequest request, WorldState state);
}
```

Creative implementation:

* grant required items directly,
* batch items if inventory pressure is high,
* optionally spawn into a nearby container if that is more reliable for large volumes.

Survival implementation:

* gather or craft the items,
* respect tool tiers,
* respect inventory capacity,
* optionally route to storage first.

#### Harvest policy

```csharp
public interface IHarvestPolicy
{
    Task<ActionPlan> HarvestAsync(HarvestRequest request, WorldState state);
}
```

Creative implementation:

* no mining required for resource acquisition,
* may skip block hardness checks,
* may still validate if a block needs to exist for placement semantics.

Survival implementation:

* validate required tool tier,
* mine only when tool tier is sufficient,
* otherwise emit subgoals for tool acquisition or crafting.

#### Mobility policy

```csharp
public interface IMobilityPolicy
{
    Task<ActionPlan> MoveToAsync(Position target, WorldState state);
}
```

Creative implementation:

* choose flight or direct vertical mobility,
* optionally prefer `FlyTo` over pathing.

Survival implementation:

* pathfind normally,
* climb, bridge, or detour as needed,
* never assume flight.

#### Inventory policy

```csharp
public interface IInventoryPolicy
{
    Task<ActionPlan> MakeRoomAsync(WorldState state, int estimatedSlotsNeeded);
}
```

Creative implementation:

* usually no-op unless inventory is genuinely blocked.

Survival implementation:

* deposit into storage,
* craft intermediate blocks,
* drop garbage if permitted by goal policy,
* never silently overflow.

---

## Goal flow

The planner should stay goal-centric:

```text
Goal
 → Strategy selection
 → Policy consultation
 → Action plan
 → Tool dispatch
 → Adapter execution
 → Projection / reconciliation
```

### Gather flow

#### Creative

`GatherItemGoal` becomes:

1. `ResourceProvisionPolicy.EnsureResourcesAsync(...)`
2. optional `InventoryPolicy.MakeRoomAsync(...)`
3. `VerifyInventory`

No mining, no tool checks, no pathing unless the request itself requires movement.

#### Survival

`GatherItemGoal` becomes:

1. `InventoryPolicy.MakeRoomAsync(...)` if needed
2. `HarvestPolicy.HarvestAsync(...)`
3. `VerifyInventory`

If the target requires a tool tier, the harvest policy should not “try and see.” It should return a structured requirement failure that the planner can turn into a subgoal.

---

## Tool-tier rule engine

Tool gating should be data-driven, not encoded in goal logic.

```csharp
public interface IHarvestRequirementResolver
{
    HarvestRequirement Resolve(string blockId);
}

public sealed record HarvestRequirement(
    string BlockId,
    string MinimumToolTier,   // e.g. "pickaxe:wood", "pickaxe:iron"
    bool RequiresTool,
    bool AllowsCreativeBypass);
```

Examples:

* stone → pickaxe required
* diamond ore → iron pickaxe minimum
* obsidian → diamond pickaxe minimum

This rule set should live in one place and be used by:

* gather planning,
* build material acquisition,
* mining tools,
* future autocomplete / assistance.

If you later add modded blocks, only the resolver changes.

---

## Build flow

Build is the clearest place to separate creative and survival.

### Creative build

* provision materials directly,
* fly to the origin,
* place blocks,
* verify structure.

### Survival build

* resolve origin,
* acquire materials through gather/craft/storage,
* navigate normally,
* place blocks,
* verify structure.

`BuildGoalDecomposer` currently handles origin selection and passes a `requireOrigin` flag into `DecomposeBuild`, while `HtnPlanner` also contains a creative branch for build behavior. That is exactly the split that should be removed.

The new rule should be:

* **BuildGoal** expresses what is needed.
* **BuildPolicy** decides how the origin is handled.
* **ResourceProvisionPolicy** decides how materials are acquired.
* **MobilityPolicy** decides how to move.
* **ToolDispatcher** executes the resulting actions.

No more build-mode branching inside planner code.

---

## Concrete type map

Here is the minimum type set that makes this clean and extensible:

```csharp
public interface IExecutionModeResolver
{
    ExecutionProfile Resolve(WorldState state);
}

public interface IResourceProvisionPolicy
{
    Task<ActionPlan> EnsureResourcesAsync(ResourceRequest request, WorldState state, CancellationToken ct = default);
}

public interface IHarvestPolicy
{
    Task<ActionPlan> HarvestAsync(HarvestRequest request, WorldState state, CancellationToken ct = default);
}

public interface IMobilityPolicy
{
    Task<ActionPlan> MoveToAsync(Position target, WorldState state, CancellationToken ct = default);
}

public interface IInventoryPolicy
{
    Task<ActionPlan> MakeRoomAsync(WorldState state, int estimatedSlotsNeeded, CancellationToken ct = default);
}

public interface IBuildPolicy
{
    Task<ActionPlan> PlanBuildAsync(BuildRequest request, WorldState state, CancellationToken ct = default);
}
```

And a shared request model:

```csharp
public sealed record ResourceRequest(
    string ItemId,
    int Count,
    string Reason,
    bool AllowProvision,
    bool AllowCrafting,
    bool AllowStorageUse);
```

The important part is that these are **requests**, not “creative-specific” or “survival-specific” methods. The profile determines which policy implementation runs.

---

## What gets removed eventually

The end state should delete or flatten these legacy patterns:

* planner branches like `if (state.IsCreativeMode) ...`
* goal code that knows whether creative mode exists
* survival-only mining logic embedded in goal decomposers
* build origin fallback logic that changes behavior by hidden mode assumptions
* ad hoc inventory bypasses in orchestration code
* any direct game-mode branching outside the resolver/policy layer

`AgentBackgroundService` should remain orchestration-only, because it already has enough responsibilities with action lifecycle, damage interruption, recovery, and inventory freshness. It should not become the place where mode semantics accumulate further.

---

# Phased implementation plan

## Phase 0 — Put the contracts in place

**Goal:** add the new abstractions without changing behavior yet.

Deliverables:

* `ExecutionMode`
* `ExecutionProfile`
* `IExecutionModeResolver`
* policy interfaces
* request/response DTOs
* tests for resolver behavior from `WorldState.GameMode`

Acceptance:

* no planner behavior changes yet
* all current tests still pass
* game mode is resolved in one place only

Confidence: **95%**

---

## Phase 1 — Mirror current behavior behind the new API

**Goal:** create a compatibility layer so the old behavior is expressed through the new policies.

Deliverables:

* `CreativeExecutionModeResolver`
* `SurvivalExecutionModeResolver`
* default policy implementations that internally mirror current logic
* `PlannerContext` or similar object passed into decomposers/planners

Acceptance:

* the system behaves the same as before
* planner and decomposer code can ask policy questions instead of checking `IsCreativeMode`

Confidence: **92%**

---

## Phase 2 — Migrate gather and resource acquisition

**Goal:** make gather mode-specific behavior live in policies, not goals.

Deliverables:

* move gather planning into `IResourceProvisionPolicy`
* creative gather provisions items directly
* survival gather resolves tool requirements and mines/crafts/storage as needed
* add tool-tier validation data source

Acceptance:

* creative gather never mines
* survival gather mines only when requirements are satisfied
* invalid tool tier produces a structured failure and a subgoal opportunity

Confidence: **90%**

---

## Phase 3 — Migrate build and mobility

**Goal:** remove the `HtnPlanner` creative build branch and centralize build strategy.

Deliverables:

* `IBuildPolicy`
* `IMobilityPolicy`
* `BuildGoal` stops owning mode-sensitive behavior
* creative build uses flight + direct provisioning
* survival build uses gather/craft/storage + normal travel

Acceptance:

* no `IsCreativeMode` branch remains in planner build logic
* creative build can fly and provision items on demand
* survival build respects material and tool constraints

Confidence: **93%**

---

## Phase 4 — Remove legacy handling

**Goal:** delete old paths, not just hide them.

Deliverables:

* remove planner-level creative branches
* remove old mode-specific fallbacks
* delete dead helper methods and duplicate special cases
* move any remaining legacy behavior into the policy implementations or remove it entirely

Acceptance:

* one mode resolver
* one policy path per concern
* zero scattered creative/survival `if` statements in goals/planners/orchestration

Confidence: **88%**

---

## Phase 5 — Harden and extend

**Goal:** make the system extensible for future modes and server rules.

Deliverables:

* support Adventure / Spectator capability sets
* optional server-rule overrides
* richer inventory/storage policy
* richer creative grant policy
* metrics around policy selection and failure reasons

Acceptance:

* adding a new mode does not require touching goal logic
* new rules are introduced through policy composition or data, not branches

Confidence: **84%**

---

# Concrete migration order

Start here:

1. Add `ExecutionProfile` and resolver.
2. Thread profile into planner/decomposer entry points.
3. Extract gather into resource policy.
4. Extract build into build policy.
5. Extract movement into mobility policy.
6. Extract inventory cleanup/storage into inventory policy.
7. Delete old creative/survival branches.

That order minimizes risk because it preserves current behavior until policy implementations are complete.

---

# Design rules

These should be treated as invariants:

1. **Goals are mode-agnostic.**
   A goal says what the player wants, not whether the agent is in creative or survival.

2. **Policies decide how.**
   Creative/survival differences belong in policies, not planners.

3. **Adapters execute, they do not decide.**
   `ToolDispatcher` remains the safety boundary and the adapter stays low-level.

4. **The mode resolver is the only place that reads game mode.**
   `WorldStateProjector` owns canonical ingestion of `gameMode`, and everything else consumes the resolved profile.

5. **No silent fallback from survival constraints.**
   If the bot needs a tool, space, storage, or materials, the policy must say so explicitly.

---

# Example end-state behaviors

## Creative gather

Input: “gather 32 oak logs”

Output:

* `ResourceProvisionPolicy` grants 32 oak logs
* if inventory is tight, split into batches or deposit first
* verify inventory

## Survival gather

Input: “gather 32 oak logs”

Output:

* check capacity
* ensure axe or equivalent if your rules require it
* mine logs
* collect drops
* verify inventory
* if inventory fills, use storage policy

## Creative build

Input: “build a small house”

Output:

* provision materials
* fly to origin
* place blocks
* verify

## Survival build

Input: “build a small house”

Output:

* resolve origin
* gather/craft materials
* manage inventory
* path normally
* place blocks
* verify

---

# Definition of done

The refactor is complete when:

* creative and survival are represented as execution profiles
* no planner or goal has direct creative/survival branching
* resource acquisition is policy-driven
* movement is policy-driven
* storage/inventory handling is policy-driven
* tool-tier validation is centralized
* `ToolDispatcher` remains the only execution safety boundary
* legacy handling is deleted, not just bypassed

If helpful, the next step is to turn this into a repo-specific file tree and class-by-class refactor map tied to the exact projects in the solution.
