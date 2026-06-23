
# MemorySmith.Agent audit — sprint-35-llm-first build / intent interpreter review

Generated: 2026-06-23 13:27:25

## Executive summary

This codebase has made real progress: the repo now claims an HTN planner with goal decomposers, dual memory gateways, a deterministic fast-path chat interpreter with LLM fallback, a replan governor, a world model, JSON-schema tool validation, and an inventory freshness gate. citeturn637814view0

The biggest remaining risk is the same one you called out: the build path still looks more like “emit placement actions and then ask for status” than “prove the blueprint exists in the world and keep proving it stays that way.” In the current build goal, `Verify` is documented as a `GetStatus` step, and completion is still keyed off a fact (`goal:Build:{blueprintId}:complete`) rather than a concrete world reconciliation against the blueprint. citeturn426420view0turn426420view3

I also did not find a dedicated build-integrity observer in the reviewed event loop. The service correlates `PlaceBlock` through the generic action pipeline, but the explicit event handling I found covers `StatusEvent`, `BlockMinedEvent`, `CraftCompleteEvent`, `SmeltCompleteEvent`, `MoveEvent`, `Wander*`, and `FlatAreaFoundEvent`; I found no `BlockPlacedEvent` handling in the service page I searched. citeturn426420view1turn426420view2turn986425view1

So the core recommendation is: add a first-class build reconciliation layer that records expected placements, confirms actual placements, and marks missing/mismatched/broken blocks as failures or replanning triggers. That belongs in the world-model/reconciliation architecture you already claim in the README, not as another ad hoc check bolted onto `GetStatus`. citeturn637814view0

## Scope and assumptions

I reviewed the current public repo state I could access, the visible sprint/PR history, and the planning / build / interpreter code paths that are most relevant to the user-visible brittleness. I could not directly verify the exact head commit for a branch named `sprint-35-llm-first`; the visible PR metadata I found points to an older sprint branch, so I treated the latest accessible repo state as the audit target and avoided assuming the branch name was perfect. citeturn801019view0

I also checked for overlap with already-documented work. The README already advertises the planner/router split, replan governor, inventory freshness gate, and tool validation, so I avoided recommending another pass that simply re-implements those same controls under a different name. citeturn637814view0

## What looks solid already

The chat layer now has a deterministic parse path for `build`, `gather`, `craft`, `cancel`, `status`, navigation, and help, with optional build coordinates carried all the way into goal parameters. That reduces LLM dependence for common commands and makes the intent layer testable. The LLM interpreter also falls back to the pattern matcher when unavailable, rate-limited, or parse-broken. citeturn986425view4turn986425view5

The build planner already uses a blueprint parser plus a blueprint executor that emits ordered `PlaceBlock` actions from parsed blueprint blocks, and the HTN task library does a real gather/craft prepass before placing. That is a decent separation of concerns. citeturn426420view3turn986425view5

The background service is not “fire and forget” anymore in the naive sense; it does action correlation, failure routing, stale-inventory suppression, and a damage interrupt path. Those are the right primitives for a more reliable agent loop. citeturn426420view1turn426420view2

## Findings

### 1) Build completion is still fact-based, not blueprint-verified

`BuildGoal` currently says `Verify` is `GetStatus`, and `IsComplete`/`HasFailed` read world-state facts. That means the goal can only ever be as trustworthy as whatever downstream code chooses to set those facts to; the goal itself does not prove the structure matches the blueprint. citeturn426420view0

The task library’s build decomposition follows the same pattern: gather resources, move to origin, emit all `PlaceBlock` actions, then add a single `GetStatus`. I did not find a step that compares the world to the blueprint after placement. citeturn426420view3

**Why this matters:** if the bot places the wrong block, places into an occupied cell, misses a block, or a block gets broken after placement, the current flow has no obvious built-in mechanism to notice except indirect failure symptoms. That is exactly the kind of “silent success” that turns into brittle agent behavior.

**Confidence: 94%**

### 2) The observation loop does not appear to validate placed blocks against expected blocks

The event loop handles `StatusEvent` by completing correlated `GetStatus`/`Status`, and it handles `BlockMinedEvent`, crafting, smelting, movement, wandering, and flat-area scanning. I found a mapping for Node’s lowercase `"place"` action name to the C# tool name `PlaceBlock`, but I did not find explicit `BlockPlacedEvent` handling in the service page I searched. citeturn426420view2turn986425view1

That is a strong signal that placement success is currently inferred, not verified. Correlation can tell you “the action was acknowledged,” but not “the right block now exists at the right coordinate and stayed there.”

**Why this matters:** the bug class you described — trying to place into dirt without digging, not noticing a block failed to place, or breaking a block later — is fundamentally an observation problem. The system needs to observe both the immediate placement result and the lasting world state.

**Confidence: 90%**

### 3) The build pipeline duplicates the same concept in multiple places

The build path is split between `HtnPlanner.CreateCreativeBuildActions`, `HtnTaskLibrary.DecomposeBuild`, `BlueprintExecutor.Execute`, and the build-specific pieces of `AgentBackgroundService`. That is not automatically wrong, but it does increase the odds that one path gets a verification improvement while the other stays stale. citeturn426420view3turn986425view5

The current shape suggests that the planner owns “what to do,” the executor owns “emit `PlaceBlock` actions,” and the background service owns “watch the world.” That is fine conceptually, but the “watch the world” side is not yet rich enough for build integrity. A dedicated build reconciler would reduce the spread of build-specific logic across those layers.

**Confidence: 78%**

### 4) The repo already has the architectural language for a better solution, but not the implementation

The README already frames the system around a world model with observe/predict/reconcile/uncertainty, plus tool validation and plan recovery. That is the right vocabulary for a build-verification loop. citeturn637814view0

What is missing is the concrete world-model implementation for structures: a “blueprint reconciliation” step that turns expected placements into verifiable state, not just into actions. Right now, build verification looks materially weaker than the architecture claims.

**Confidence: 86%**

### 5) Tests appear to cover facts and shape, but not the actual build-integrity contract

In the reviewed code, the tests I inspected for `BuildGoal` check name/description/phases and fact-key behavior, but not world reconciliation, missing blocks, mismatched blocks, or post-placement breakage. That leaves the exact failure mode you care about under-tested.

**Confidence: 81%**

## Concrete recommendations

### Highest priority

Add a `BuildVerifier` or `BlueprintReconciler` that can answer three questions:

1. Did the bot place the expected block at the expected coordinate?
2. Did that block remain there long enough to count as complete?
3. If not, what is the exact mismatch or missing cell?

A practical first version can be event-driven: each expected placement gets a pending confirmation record; each placement event or post-place scan resolves one record; any unresolved record after a timeout or a world mismatch becomes a build failure or replan trigger.

### Next priority

Extend the world-state / fact model with per-blueprint build-integrity facts such as:

- expected block count
- confirmed block count
- missing block count
- mismatched block count
- last verified timestamp
- last mismatch reason

That would let `BuildGoal.IsComplete` stop being a plain yes/no fact check and start being a real “blueprint is present and intact” check.

### Also worth doing

Add explicit regression tests for:

- partial placement where the plan finishes but some blocks never appear
- placement into an obstructed cell
- a placed block being broken after confirmation
- blueprint mismatch at a coordinate
- replan/failure path when verification disagrees with the plan

### Codebase health improvement

Centralize build lifecycle logic so the planner emits the plan, the background service watches events, and a single reconciler owns build-integrity state. That will reduce drift between creative and survival build paths and prevent future “one path got the fix, the other did not” regressions.

## Open questions

- What exact branch/commit is `sprint-35-llm-first` in your local workflow? I could not directly verify that branch name from the accessible GitHub metadata, so I audited the latest visible repo state instead. citeturn801019view0
- Is `BlockPlacedEvent` emitted reliably by the Mineflayer adapter today, or does placement success only show up indirectly through other events?
- Do you want build completion to require only “all expected blocks were placed,” or also “the structure still exists after a stabilization window”?
- Should broken/missing blocks after completion reopen the goal automatically, or merely mark the build as degraded and notify the operator?

## Confidence summary

- Build verification is currently too weak: **94%**
- A dedicated placement-to-world reconciliation layer is needed: **90%**
- Existing architecture already points toward this solution: **86%**
- Current tests do not fully cover the build-integrity contract: **81%**
- The build path is more brittle because logic is spread across several layers: **78%**

## Bottom line

The repo is moving in the right direction, but the build path still trusts action dispatch too much and world observation too little. The next meaningful leap is not another parser tweak; it is making build success a verified property of the world, not just a fact written after a `GetStatus`. citeturn426420view0turn426420view3turn637814view0
