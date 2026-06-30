# Delta Report: God Class Decomposition + Creative vs Survival Policy

Branch: `dev/round-3`  
Focus: close the remaining open questions and turn the current architecture into a safe decomposition plan.

## What is now safe to assume

1. **`AgentBackgroundService` is still the runtime orchestrator and still too broad.** It owns connection lifecycle, event processing, recovery, status pushes, memory loading, sequence advancement, emergency stop, and planner-adjacent state. That is visible directly in the current class shape. fileciteturn12file0 fileciteturn14file0 fileciteturn40file0
2. **The repo already intends a layered split, not a monolith rewrite.** `RecoveryManagerImpl` exists as a stub, and the task backlog explicitly tracks service extraction, event-bus refactoring, and synchronization cleanup rather than a full rewrite. fileciteturn34file0 fileciteturn42file0
3. **Creative mode should not rely on survival gathering as a fallback.** The repo already documents creative-mode guards and a creative build path, but the gather decomposer still emits survival-style mining actions, which is the architectural gap behind the current regression. fileciteturn41file0 fileciteturn47file0 fileciteturn49file0
4. **The canonical policy is already encoded in repo guidance: parsers do not create goals, and the runtime pipeline is supposed to flow through intent → planner → goal.** That matters because creative handling should be a policy decision in one layer, not an ad hoc patch in three layers. fileciteturn44file0

## Closed open questions

### Should creative gather exist?
Recommendation: **no survival mining in creative**. Either:
- creative gather short-circuits to a deterministic no-op / complete-with-explanation path, or
- creative gather is converted to a direct provisioning request that never emits mining actions.

The repo already treats creative build as a special case and already has creative recovery guards. Extending that to creative gather is consistent with existing direction. fileciteturn47file0 fileciteturn41file0

### Should creative provisioning stay in `AgentBackgroundService`?
Recommendation: **no**. The current host-side `/give` fallback is goal-type specific and should be removed or reduced to a thin compatibility shim. The adapter already has a creative inventory fallback, and the host-side fallback is now a second policy surface. fileciteturn17file0 fileciteturn36file0

### Should creative state be inferred only from `WorldState`?
Recommendation: **no**. `WorldState.IsCreativeMode` is useful, but it is still a projected state, not a direct capability signal. Creative handling should prefer an adapter-confirmed mode signal when making immediate dispatch/recovery decisions. The current code mixes state projection and transport-level behavior. fileciteturn9file0 fileciteturn41file0

## Decomposition plan for `AgentBackgroundService`

### Goal
Reduce `AgentBackgroundService` to a thin orchestrator that coordinates services instead of owning domain policy.

### Safe extraction order

#### Phase 1 — isolate pure policy
Extract logic that has no transport side effects:
- failure-to-recovery classification
- creative vs survival policy selection
- goal abandonment / retry thresholds
- build-fact cleanup rules
- sequence advancement decisions

Likely destination services:
- `IRecoveryPolicy` or `IRecoveryManager`
- `IGoalLifecyclePolicy`
- `ICreativeModePolicy`
- `IBuildProgressPolicy`

Why first: these are easiest to test in isolation and easiest to migrate without changing wire behavior. The code already has signs of this split: `RecoveryManagerImpl` exists, but is still a stub. fileciteturn34file0

#### Phase 2 — extract event handling
Split `ProcessEventsAsync` into event handlers by concern:
- `StatusEventHandler`
- `GameModeEventHandler`
- `InventoryEventHandler`
- `DamageEventHandler`
- `BuildEventHandler`
- `ChatEventHandler`

`AgentBackgroundService` should subscribe and forward, not interpret every event inline. The current file shows these branches all living together, which makes regression risk high. fileciteturn14file0 fileciteturn38file0 fileciteturn39file0

#### Phase 3 — extract dispatch bookkeeping
Move action correlation, timeout sweeps, and progress counters into an execution coordinator:
- correlation lifecycle
- timeout sweep
- per-tool in-flight limits
- progress detection
- terminal abandonment logging

This isolates a large chunk of brittle shared state such as `_correlatedActions`, `_placeBlockContexts`, `_consecutiveFailures`, `_cycleOutcomes`, and `_blockTimeoutCounts`. Those are currently spread across the host class and are the main source of accidental coupling. fileciteturn38file0 fileciteturn39file0

#### Phase 4 — extract transport and dashboard concerns
Move:
- SignalR pushes
- chat logging
- status push formatting
- build identity logging
- memory load/remember operations

These are infrastructure concerns and should not sit beside goal policy. `AgentBackgroundService` currently mixes all of them. fileciteturn40file0

### What stays in `AgentBackgroundService`
Keep only:
- host startup/shutdown
- dependency wiring
- orchestration calls
- cancellation wiring
- a single loop that delegates to the extracted services

That makes it a coordinator, not a policy engine.

## Creative vs survival handling plan

### Rule 1 — one policy owner
Creative mode handling should live in one place only. Current behavior is split across:
- host-side `/give` provisioning
- planner creative special-casing
- adapter creative inventory fallback
- recovery guards

That split is the root of the regression risk. fileciteturn17file0 fileciteturn41file0 fileciteturn47file0 fileciteturn36file0

### Rule 2 — survival path remains the default
All gather/build/craft/smelt logic should continue to work in survival exactly as it does now. Creative should be an explicit override path, not an implicit “maybe skip” flag.

### Rule 3 — creative should never mine as a fallback
For creative:
- `place` should use creative selection or authoritative provisioning
- `gather` should not emit `MineBlock`
- recovery for “not in inventory” should not turn into gather
- `/give` should not be the hidden second fallback unless the team intentionally decides to keep it as a compatibility path

### Rule 4 — verify with mode-specific tests
Add tests at three layers:
- planner tests: creative gather returns the expected creative policy result and never emits mining
- host tests: creative recovery does not route to gather
- adapter tests: creative place behavior succeeds without depending on survival inventory semantics

The current tests only prove that creative gather no longer auto-completes; they do not prove that the downstream action plan is creative-safe. fileciteturn43file0

## Recommended implementation sequence

1. **Decide the creative policy contract first.**
   - Gather in creative: no-op, completion, or direct provision?
   - Place in creative: adapter inventory selection only, or host `/give` allowed as fallback?
   - Recovery in creative: suppress gather entirely.

2. **Move creative policy behind one interface.**
   - A small `ICreativeModePolicy` is enough.
   - `AgentBackgroundService` and the planner call that policy; they do not duplicate logic.

3. **Extract recovery and dispatch state from `AgentBackgroundService`.**
   - Put recovery decisions in one service.
   - Put action correlation/timeouts in one service.
   - Put dashboard pushes and memory persistence behind thin adapters.

4. **Remove duplicate creative fallback paths.**
   - Keep only one authoritative creative provisioning path.
   - Eliminate host-side `/give` if the adapter is authoritative.
   - Otherwise, make host-side provisioning the only path and simplify the adapter’s creative fallback.

5. **Add regression tests before deleting legacy branches.**
   - This prevents recreating the current bug under a new name.

## Grounded confidence

- **Creative/survival policy split exists:** 95%
- **`AgentBackgroundService` should be decomposed:** 98%
- **One-policy-owner is the right fix shape:** 90%
- **Creative gather should never mine:** 92%
- **Adapter-level creative verification is brittle today:** 80%

## Assumptions used in this plan

- The project is intentionally greenfield and should favor clarity over compatibility bridges.
- Existing “done” task pages may describe partial fixes rather than fully enforced invariants.
- The current regression is a policy-routing bug, not a single-line typo.

## Remaining open question to resolve during implementation

The only unresolved product decision is whether creative gather should:
1. complete immediately,
2. become a direct provisioning request,
3. or fail explicitly as unsupported.

Everything else can be implemented safely once that decision is made.
