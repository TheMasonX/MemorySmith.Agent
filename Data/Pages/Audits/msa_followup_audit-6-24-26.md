# MemorySmith.Agent Follow-Up Audit
**Repo:** TheMasonX/MemorySmith.Agent  
**Date:** 2026-06-24  
**Scope:** Additional pass focused on legacy/fallback consolidation, path consistency, and corrections to the prior audit.

## Executive summary

The codebase is still trending in the right direction, but several legacy compatibility seams are now causing behavior drift between planner paths.

The most important new finding is that **creative-mode build planning appears to be split between two paths**: `HtnPlanner` still has a creative-mode special case, but the newer `BuildGoalDecomposer` always delegates to `HtnTaskLibrary.DecomposeBuild`. Since the router prefers decomposers first, production routing likely no longer preserves the legacy creative fast path. Confidence: **90%**.

I also confirmed and strengthened two earlier risk areas:

- **Craft planning still under-scales prerequisite gathering for `count > 1` recipes** in the iron/stone branches. Confidence: **96%**.
- **The action queue’s atomic interrupt contract is over-promised** relative to the public API, because only some methods are locked. Confidence: **84%**.

I corrected one earlier framing: the build-origin issue is less “explicit origin is ignored everywhere” and more **“build behavior is inconsistent across the decomposer, fallback planner, and legacy creative path.”** The production router path now appears more intentional than the fallback path, but the overall system is still carrying too many parallel semantics.

## Updated findings

### 1) Creative-mode build logic is likely bypassed by the new decomposer-first routing
**Severity:** High  
**Confidence:** 90%

`Agent.Planning/HtnPlanner.cs` still contains a creative-mode branch for `BuildGoal`. When `state.IsCreativeMode` is true, it generates `SearchMemory` + `MoveTo` + block placement actions via `CreateCreativeBuildActions(...)`. By contrast, `Agent.Planning/Decomposition/BuildGoalDecomposer.cs` always calls `HtnTaskLibrary.DecomposeBuild(...)` and does not branch on creative mode. Because `PlannerRouter.Select(...)` prefers registered decomposers first, the production route for `BuildGoal` will usually go through `BuildGoalDecomposer`, not the legacy `HtnPlanner` branch.

That means the creative fast path is probably no longer exercised in the default production path. This is a regression risk because creative mode is a different execution model, not just a small optimization.

**Why this matters:**  
In creative mode, the agent should not be forced through the same resource-gathering and crafting constraints as survival mode. If it is, it may waste time or produce nonsensical plans.

**Recommendation:**  
Move creative-mode handling into the decomposer layer or a shared build-plan service so all build routes respect the same semantics. Add a regression test that routes a `BuildGoal` through `PlannerRouter` with creative mode enabled.

---

### 2) CraftItem prerequisite planning still does not scale with `count`
**Severity:** High  
**Confidence:** 96%

`HtnTaskLibrary.DecomposeCraftItem(string itemId, int count, WorldState state)` uses `count` for the final `CraftItem` action, but the upstream ore/cobblestone gathering branches still calculate ingredients as though only one craft is requested.

This is already enough to break larger orders:

- `CraftItemGoal("iron_pickaxe", 2)` can gather only enough iron for one pickaxe.
- `CraftItemGoal("stone_sword", 3)` can gather only enough cobblestone for one sword.

The planks branch already scales by count correctly, which makes the mismatch more likely to be missed in review.

**Recommendation:**  
Derive prerequisite quantities from a per-recipe model and multiply by requested craft count once, centrally. Add tests for `count > 1` on at least one iron tool and one stone tool.

---

### 3) The action queue’s “atomic interrupt” contract is not actually global
**Severity:** Medium  
**Confidence:** 84%

`ActionQueue.ClearAndEnqueue(...)` is lock-protected, but `Enqueue`, `Clear`, `Dequeue`, and `Peek` are still lock-free. The comments describe a stronger guarantee than the implementation actually provides.

This is a classic hidden contract bug: the method name sounds like a strict synchronization primitive, but the rest of the API can still interleave around it.

**Why this matters:**  
The queue is used to manage planning interruptions and priority actions. If callers assume a stronger atomicity guarantee than the code provides, rare race windows can still reorder or preserve stale actions.

**Recommendation:**  
Either unify all mutating queue operations under the same lock or narrow the guarantee in the documentation so it is clearly best-effort. Add a concurrency test that interleaves `Enqueue`, `Clear`, and `ClearAndEnqueue`.

---

### 4) Build-origin behavior is still split across legacy and modern paths
**Severity:** Medium-High  
**Confidence:** 88%

The build-origin system has improved, but it is still semantically split:

- `BuildGoal` now carries explicit origin fields.
- `BuildGoalDecomposer` honors those fields and uses them as a scan center for flat-ground detection.
- `HtnPlanner` still reads build origin from world-state facts and has a separate creative-mode branch.

This is not one clean model; it is two overlapping ones. The result is path-dependent behavior.

**What changed from the prior report:**  
The strongest concern is no longer “the build system ignores explicit origin everywhere.” The stronger and more accurate concern is **cross-path inconsistency**. The router path, the fallback path, and creative mode do not share one authoritative build-origin policy.

**Recommendation:**  
Centralize build-origin resolution into one service that both decomposer and fallback planner call. Make the intended semantics explicit: “origin is scan center” versus “origin is literal build anchor.”

---

### 5) Tool exceptions are sanitized too aggressively for diagnosis
**Severity:** Medium-High  
**Confidence:** 93%

`ToolDispatcher.CallAsync(...)` now catches arbitrary exceptions and converts them to `ToolResult(false, ...)`. That is good for loop stability, but the implementation only preserves `ex.Message` in the journal and in the returned error string.

This hides exception type and stack trace, which are often needed to distinguish tool bugs from expected runtime failures.

**Recommendation:**  
Keep the safe user-facing failure channel, but store structured exception metadata internally: exception type, stack trace, tool name, and a correlation ID. That gives you safety without losing debuggability.

---

### 6) Local blueprint file fallback is convenient but still a hidden runtime dependency
**Severity:** Medium  
**Confidence:** 78%

`MemorySmithBlueprintRepository` now falls back to local filesystem pages if the live gateway lookup fails. That is useful for offline/dev flows, but it also couples runtime behavior to the checkout layout and can quietly change which blueprint source wins.

This makes the repository more resilient in development, but less explicit operationally. Search also appends local blueprints after gateway results, which can produce stale or duplicate coverage unless callers dedupe carefully.

**Recommendation:**  
Treat local fallback as an explicit mode or configuration switch. Cache local blueprint enumeration if it remains part of runtime behavior.

## Corrections to the prior report

The earlier report treated build-origin handling as a broad inconsistency. After this deeper pass, the better framing is:

- The **decomposer-first production route is intentional and largely correct**.
- The **legacy fallback planner path is where semantics drift**.
- The **creative-mode build path is probably the actual regression risk**, because it exists only in `HtnPlanner` and is not mirrored in the decomposer path.

That means the build system issue is still real, but the most actionable fix is to unify build planning semantics rather than just patch one missing branch.

## Assumptions

- This pass is still static-review only; I did not execute the game loop or run the full test suite locally.
- I used the current repository state and sprint docs as the source of truth for “already tracked” work and avoided duplicating roadmap items.
- Where build semantics differ by mode, I assumed the decomposer-first path is the intended production route because `PlannerRouter` explicitly prefers decomposers.

## Open questions

1. Should creative-mode builds be a first-class route in the decomposer layer?
2. Should `ActionQueue` expose stronger synchronization guarantees, or should its docs be softened?
3. Do you want `ToolDispatcher` to preserve stack traces internally while still returning sanitized failures outward?
4. Should local blueprint fallback remain runtime behavior, or be restricted to dev/offline modes only?

## Priority recommendations

1. Unify build planning semantics across creative mode, survival mode, decomposer routing, and fallback routing.
2. Fix craft prerequisite scaling for multi-count craft goals.
3. Tighten queue synchronization guarantees or documentation.
4. Preserve structured exception detail in the tool layer.
5. Make blueprint local fallback explicit and configurable.
