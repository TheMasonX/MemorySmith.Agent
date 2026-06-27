# Combined Audit Report
## MemorySmith.Agent — Current Applicable Findings

**Repository:** TheMasonX/MemorySmith.Agent  
**Scope:** Current codebase review with focus on the Mineflayer adapter, smelt goal handling, build/gather execution, observation gaps, and sprint transition risks.

---

## Executive summary

The repo is moving in a better direction. The latest work adds a real `SmeltGoal`, removes `SearchMemory` from the HTN plan paths, and continues hardening the Mineflayer adapter. Those are all genuine improvements.

The core risk is still the same: the system is getting better at issuing actions, but it is not yet equally good at proving that the world matches the plan. That applies most obviously to build completion and block placement, but the same pattern shows up in smelting, gather discovery, and recovery logging.

The most important current themes are:

- build/placement verification is still optimistic rather than truth-based
- `SmeltGoal` is a good architectural step, but its failure semantics and fuel abstraction are still thin
- removing `SearchMemory` simplified planning, but it may also have removed explicit discovery without a clearly equivalent replacement
- logging still needs stronger causal context
- GoalFactory is starting to look like a scaling risk as more prefix-based goal types are added

---

## Current findings that still apply

### 1) Build verification is still too optimistic

The adapter can emit `blockPlaced`, but the reviewed code does not verify that the world actually contains the expected block after placement. The build flow still ends in a status refresh rather than a blueprint/world reconciliation step.

That means the system can still report progress while:

- the wrong block was placed
- a placement silently failed
- the block was later broken
- or the world drifted away from the blueprint without being noticed

This is still the highest-value reliability gap.

---

### 2) Observation is weaker than execution

The planner and executor are improving faster than the observation layer. The system often knows that an action ran, but not whether the observed outcome matches the intended result.

This is the fundamental architectural bottleneck.

The next useful abstraction is still a richer action/result model that separates:

- action attempted
- tool succeeded
- goal satisfied
- world verified

Those are not the same thing.

---

### 3) The Mineflayer adapter is materially better than earlier versions

The adapter now has several real hardening improvements:

- mining searches near and far
- mining retries pathfinding
- per-block mining deltas are emitted
- stop commands bypass the queue
- system messages are filtered before they hit the LLM/chat pipeline
- chunk-load waiting improves flat-area scanning
- game mode normalization reduces startup inconsistency

These are real wins and should be kept.

---

### 4) Placement still needs better truth and diagnostics

The `place` action path is optimistic: it pathfinds, equips, places, and then emits success. The current logic does not clearly re-read the target position afterward and prove that the intended material exists at the intended coordinates.

That is the exact kind of thing that creates “silent success.”

Recommendation: place should be followed by a world readback and a verified outcome, not just an exception-free call.

---

### 5) SmeltGoal is a real improvement, but the semantics need more hardening

The new `SmeltGoal` is structurally the right move. It makes smelting a first-class goal rather than forcing it through crafting.

What is good:

- `SmeltGoal` exists as a distinct goal type
- `SmeltGoalDecomposer` emits `SmeltItem` instead of `CraftItem`
- the tests confirm the main routing behavior
- common outputs like iron/gold/copper/netherite-scrap are mapped

What still needs attention:

- the fallback semantics for unknown inputs are easy to misuse
- failure state is checked through a fact key, but a corresponding writer was not clearly visible in the reviewed path
- coal is hardcoded as the fuel strategy
- the full live furnace interaction path is not yet proven by the tests

So this is a good refactor, but not yet a fully robust smelting subsystem.

---

### 6) Removing SearchMemory simplified plans, but may have removed explicit discovery too aggressively

The current task library no longer emits `SearchMemory` in the reviewed gather/build/craft decompositions.

That is clean, but it also means the planner now relies more heavily on wander/mine logic for discovery. If discovery was moved elsewhere, that replacement should be explicit and testable. If not, the behavior may become less reliable in sparse or unfamiliar environments.

The removal looks intentional and probably sensible, but the replacement story is still not clearly visible in the reviewed code.

---

### 7) GoalFactory is trending toward a scaling risk

GoalFactory now has more prefix-based routing for special goal types. That works for now, but it is the kind of place that can become a monolith over time.

The more prefixes and special cases it accumulates, the harder it will be to maintain without a more declarative registration pattern.

---

### 8) Logging still lacks enough causal context

Most current logging explains what happened, but not enough about why it happened or how the planner should recover.

Useful future log shape:

- goal
- prerequisite
- action
- observation
- outcome
- recovery suggestion

Without that chain, debugging and self-repair get harder as the system grows.

---

### 9) Correlation should eventually span the whole chain, not just action/result pairs

The repo has improved correlation considerably, but the ideal is still a single root operation ID that spans:

- intent
- goal
- task
- action
- observation
- outcome

That becomes important as multiple goals or retries exist at once.

---

### 10) Smelting fuel handling needs an abstraction

Coal-first smelting is okay for now, but it will become awkward once the agent needs to support alternative fuels.

A small fuel resolver abstraction would keep the code from hardcoding a single strategy too deeply.

---

## Recommended improvements

### P0
Add blueprint/world reconciliation after placement.

### P0
Add a stronger post-action observation layer that separates:
- action completion
- tool success
- goal satisfaction
- world verification

### P0
Add explicit smelt failure classification and confirm the failure fact is written somewhere reachable from the new smelt flow.

### P1
Introduce a fuel abstraction for smelting.

### P1
Make discovery explicit after SearchMemory removal, or document that wander/mine now owns it.

### P1
Improve causal logging so the agent can explain why it chose a prerequisite or why a recovery path was selected.

### P2
Consider moving GoalFactory toward a more declarative registration model before the prefix list grows much more.

---

## SmeltGoal-specific observations

The smelt work is a real gain, but there are still a few implementation gotchas to watch for:

- `SmeltGoal.OutputItem` falls back to the input when the input is unrecognized
- that is acceptable for safety, but it can hide an invalid or unsupported smelt request
- the decomposer pre-gathers coal and possibly the input item, but the semantics should be documented very clearly
- the tests prove routing shape, not live furnace behavior

This is good enough to merge as a direction, but not yet good enough to treat as fully hardened.

---

## Final assessment

The codebase is moving in the right direction, but the next step should not be more parser cleanup or more one-off goal types.

The next step should be:

- better truth checks
- better verification
- better failure explanation
- better causal logging

The most important question is still:

> How does the agent know the world now matches the plan?

Until that is answered more reliably, the system will continue to feel brittle even as individual components improve.
