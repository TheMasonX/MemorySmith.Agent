# Supplemental Audit Addendum

This addendum extends the earlier audit with additional findings from the current branch code.

## New findings

### 1) Gather failure state collides across different counts for the same item
**Severity:** Medium-High  
**Confidence:** 93%

`GenericGatherGoal.Name` is only `Gather:{itemId}`, while `HasFailed` checks `goal:{Name}:failed`. That means a failure while gathering `oak_log x1` will also poison `oak_log x32` because both share the same failure key. The name does not include `targetCount`, so distinct goal instances can overwrite each other’s failure state. citeturn214260view0turn921720view0

**Why this matters:** a short or failed subgoal can suppress later, unrelated gather attempts for the same item.

**Fix direction:** include `targetCount` in the failure key or introduce a unique goal-instance identifier.

---

### 2) `ExploreDecompose` hardcodes a two-pass exploration loop
**Severity:** Medium  
**Confidence:** 89%

`ExploreDecompose` emits `SearchMemory → Wander → GetStatus → Wander → GetStatus` every time, with no parameterized retry count, no state-dependent branching, and no backoff. That is a very brittle exploration policy disguised as a generic task. citeturn690488view1

**Why this matters:** exploration depth is arbitrary and fixed. For some worlds this is too shallow; for others it is wasteful. It also duplicates the same actions twice instead of reusing a loop or planner-level retry policy.

**Fix direction:** move the retry count or exploration budget into a named policy, and let the planner or state machine decide when to repeat.

---

### 3) `GatherItemDecompose` still encodes a fixed wander radius and spawn limit
**Severity:** Medium  
**Confidence:** 90%

When the branch decides to wander, it uses `radius = 40` and `maxDistanceFromSpawn = 200` directly in the decomposer. Those numbers are not self-tuning and appear to be chosen by convention rather than by a policy object or configuration source. citeturn690488view0

**Why this matters:** the same values may be too aggressive in one world and too conservative in another. They also make testing and tuning harder because behavior is embedded in code.

**Fix direction:** extract the wander policy into named constants or a planner config.

---

### 4) `MineWoodDecompose` duplicates a gather pattern instead of delegating to the generic gather path
**Severity:** Medium  
**Confidence:** 86%

`MineWoodDecompose` independently emits two `MineBlock` actions for oak and birch logs with the same default count behavior, instead of reusing the generic gather machinery. `GatherWoodDecompose` already delegates to `GatherItemDecompose(OakLogSpec, ...)`, so the wood-mining path is split across two different abstractions with different maintenance costs. citeturn830635view0turn830635view4turn690488view0

**Why this matters:** logic duplication makes later changes easy to apply in one path and forget in the other. It also leaves the codebase with two separate “wood gathering” stories that can drift apart.

**Fix direction:** either remove the `MineWood` special case or make it a thin alias over the same generic gather decomposition.

---

### 5) `GoalFactory.GetInt` silently truncates `long` values to `int`
**Severity:** Medium  
**Confidence:** 84%

`GetInt` accepts `long l => (int)l` with no range check. That means oversized numeric input can silently wrap or truncate instead of failing fast. citeturn690488view2

**Why this matters:** tool and goal parameters coming from JSON can be malformed or adversarial. A large count can become a negative or incorrect value, which can lead to bizarre plans rather than a clear validation error.

**Fix direction:** validate range explicitly and reject out-of-range inputs with a clear diagnostic.

---

### 6) Dynamic goal discovery is still a sync/async footgun
**Severity:** Medium  
**Confidence:** 76%

`GoalFactory.RegisteredGoals` exposes dynamic prefixes like `GatherItem:{itemId}`, `Build:{blueprintId}`, and `CraftItem:{itemId}`, but the synchronous `Create` path only handles static entries. That is fine only if every caller knows to use `CreateAsync`; otherwise, discovery and creation can diverge in a very confusing way. citeturn852975view2turn293137view4

**Why this matters:** a UI or API layer that lists registered goals from `RegisteredGoals` may still call the sync factory and get `null` for dynamic names. That is an API design trap.

**Fix direction:** either remove dynamic names from the sync-discovery surface or make the factory interface explicitly async-first for all dynamic goals.

### Consolidation opportunities

The codebase still has several places where policy could be centralized:

- `GatherItemDecompose`, `MineWoodDecompose`, `ExploreDecompose`, and `WanderDecompose` all hardcode movement / retry / count heuristics in different ways. citeturn690488view0turn690488view1
- `GoalFactory` and `HtnTaskLibrary` both encode item- and block-specific knowledge that could be moved into a shared policy layer. citeturn293137view1turn830635view1
- `GenericGatherGoal` and `GoalFactory` both care about gather counts, but they do not share a single source of truth for failure scoping or count semantics. citeturn214260view0turn690488view0

### Stronger assumptions to make explicit

These assumptions would make the system more robust if they were codified:

1. “Failure” should probably be scoped to a goal instance, not just an item id. citeturn214260view0turn921720view0
2. Exploration should have an explicit budget or retry policy instead of a fixed two-pass script. citeturn690488view1
3. All user-supplied numeric parameters should be range-checked at the factory boundary. citeturn690488view2
4. Gather, mine, and explore heuristics should be config-driven rather than repeated as magic numbers. citeturn690488view0turn690488view1
