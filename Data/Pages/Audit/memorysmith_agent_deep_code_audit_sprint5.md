# MemorySmith.Agent Sprint 5–6 Deep Code Audit

## Scope

I audited the current sprint branch under PR #1 (`sprint-5-tool-safety`) rather than `main`, because the PR is still open and the branch is the active work-in-progress target. The repository’s own README describes this codebase as a modular autonomous agent framework with Minecraft as the world adapter and MemorySmith as long-term memory, and it explicitly claims work in tool validation, the agent journal, the world model, and planner extensibility. citeturn605006view0turn684229view0

I also used Matt Pocock’s “improve codebase architecture” guidance as a framing lens: look for shallow modules, improve locality, and favor deepening opportunities that improve testability and AI-navigability rather than adding extra abstraction for its own sake. citeturn788512view0

## Executive summary

The branch is directionally good: it separates concerns into bounded contexts, keeps the Minecraft adapter isolated, and has already introduced the right nouns for the next sprint’s work — journal, world model, planner routing, and decomposers. The problem is that several of the new seams are not yet fully enforced in code, so the system currently relies on conventions and comments more than on executable guarantees. citeturn684229view0turn460253view3turn322915view6

The highest-risk gap is tool safety. `ToolDispatcher` documents schema validation as the intended design, but the actual dispatch path still forwards JSON directly to the tool implementation and contains a `TODO` where validation should occur. That leaves the new “tool safety” sprint partially unimplemented at the exact seam it is meant to protect. citeturn935101view0turn684229view0

The second major risk is snapshot integrity in `WorldState`. The model exposes mutable `Dictionary` and `IReadOnlyList`-backed properties with public `get; init;` access, which means consumers can still mutate the underlying objects after construction. That undermines the observation/belief distinction the sprint is trying to introduce and makes state reasoning less trustworthy. citeturn322915view6

The third major risk is planner context loss. The new router path is promising, but `DecomposerPlanner.ReplanAsync` recreates a minimal `SimpleGoal` shell and drops `failureReason` on the floor. That makes replans less faithful to the actual failure and can erase the very context replanning is supposed to preserve. citeturn460253view3

## Highest-priority findings

### 1) Tool safety is advertised, but the safety check is not actually implemented

**Severity:** Critical  
**Confidence:** 97%

`ToolDispatcher` is now the central execution seam, but its implementation still contains `// TODO: validate arguments against tool.InputSchema before dispatching` and then calls `tool.ExecuteAsync(arguments, cancellationToken)` immediately afterward. In other words, the dispatcher currently enforces registration, not schema safety. citeturn935101view0

Why this matters: the README claims “Tool Validation — JSON Schema (type/required/properties) checked before every tool execution,” and the sprint PR description repeats that this is a P0 gate. The code does not yet match that promise, so malformed or adversarial tool inputs can still reach tool implementations. citeturn684229view0turn605006view0turn935101view0

Recommendation: make `ToolDispatcher` the single place where schema validation, normalized error mapping, and tool execution happen. The deepest version of this change is not another helper interface; it is one enforced seam with a clear contract: unknown tool, invalid args, execution failure, and cancellation should each produce predictable results. That is a strong match for the architecture skill’s “deepening opportunities” approach. citeturn788512view0turn935101view0

### 2) `WorldState` is still mutable enough to break the observation/belief split

**Severity:** High  
**Confidence:** 92%

`WorldState` exposes `Inventory`, `Facts`, and `StructuredFacts` as public `init` properties, but they are still mutable collection instances. The builder copies on write in several places, yet that does not prevent external code from mutating the dictionaries or lists after the record has been constructed. citeturn322915view6

Why this matters: the branch is trying to introduce a clean world-model boundary, but this design lets state leak across that boundary. A planner can hold what looks like a snapshot while another caller mutates the same underlying collection, creating racey or hard-to-reproduce bugs in goal completion, replanning, and journaled debugging. citeturn322915view6turn684229view0

Recommendation: make the snapshot truly immutable at the API boundary. The simplest path is to store immutable collections or defensively copy on both set and expose. That would improve locality and make tests around world-state transitions much stronger. citeturn322915view6turn788512view0

### 3) Replanning loses failure context

**Severity:** High  
**Confidence:** 90%

`PlannerRouter.Select` returns a `DecomposerPlanner` when a decomposer exists, and that adapter’s `ReplanAsync` reconstructs a `SimpleGoal` from only the current plan’s name and phases. The `failureReason` parameter is accepted but not used, and the reconstructed shell uses an always-false predicate. citeturn460253view3

Why this matters: the replanner is now acting as a lossy translator. When a goal fails because of missing resources, pathing, damage, or world-state drift, that reason often matters more than the original goal label. Dropping it makes replans less adaptive and can cause the same dead-end to repeat. citeturn460253view3turn684229view0

Recommendation: preserve failure context in the planner seam. At minimum, pass the failure reason into the decomposer or encode it in the temporary goal shell; better still, make `ReplanAsync` accept a richer replanning context instead of a string-only reason. citeturn460253view3turn788512view0

### 4) Build origin fallback silently collapses invalid data to `(0,0,0)`

**Severity:** High  
**Confidence:** 88%

`BuildGoalDecomposer.ReadOriginFact` returns `0` when the origin fact is missing or unparseable, and the build decomposition then proceeds using those values. That means a malformed origin fact does not fail fast; it degrades into a build at the world origin. citeturn122887view0

Why this matters: this is the kind of bug that looks like “the bot built in the wrong place” instead of “the goal data was invalid.” Silent fallback is especially dangerous in autonomous systems because the failure surface is far away from the cause. citeturn122887view0

Recommendation: treat missing or invalid origin facts as a validation failure, not as a zero default. If a fallback is truly required, log it as an explicit degraded mode with a journal event so the problem is visible. citeturn268272view0turn122887view0

### 5) The journal is bounded, but its trim semantics are best-effort rather than strict

**Severity:** Medium  
**Confidence:** 87%

`AgentJournal.Log` enqueues, increments `_count`, and if the count exceeds the max it dequeues one oldest item. The in-code comment explicitly says this is a “best-effort trim” and that the queue may transiently exceed capacity under concurrency. `All` also materializes by reversing the queue on each read. citeturn268272view0

Why this matters: the design is acceptable for a debug journal, but it is not yet a crisp bounded buffer with predictable read costs. Under heavy event pressure, the journal may exceed the intended size briefly, and repeated reads pay an O(n) reversal cost. citeturn268272view0

Recommendation: decide whether the journal is a diagnostic store or an operational data structure. If it is diagnostic, keep the current implementation but document the best-effort semantics. If it is operational, move to a stricter ring-buffer implementation with atomic trim behavior and a stable snapshot API. citeturn268272view0turn788512view0

### 6) Goal decomposer routing is order-dependent

**Severity:** Medium  
**Confidence:** 84%

`DecomposerRegistry.Find` returns the first decomposer whose `CanHandle` returns true, and `PlannerRouter.Select` uses that result as the first routing decision before falling back to HTN. That means registration order becomes part of behavior. citeturn460253view0turn460253view3

Why this matters: once there are overlapping handlers, the runtime choice is no longer explicit. A future decomposer can silently shadow an older one, which is the kind of routing bug that usually appears as “planner weirdness” rather than a direct exception. citeturn460253view0turn460253view3

Recommendation: either make handler priority explicit in the registry or enforce disjoint handler predicates through tests. That keeps the seam understandable and makes planner selection easier to reason about. citeturn788512view0turn460253view0

### 7) Blueprint lookup is broader than the documented intent

**Severity:** Medium  
**Confidence:** 81%

`MemorySmithBlueprintRepository` normalizes blueprint IDs, then searches the wiki and accepts any page whose `PageId` merely contains `"blueprints"` and whose kind is `"page"`. It also supplements search results with local blueprints. That is useful for resilience, but the matching rule is broader than the contract suggested by “blueprints/{blueprintId}” in the comments. citeturn454426view4

Why this matters: broad containment matching can accidentally resolve a sibling or similarly named page. In a memory-backed system, a mistaken page match can cascade into the wrong build plan, wrong block list, or wrong raw markdown being cached into the goal pipeline. citeturn454426view4

Recommendation: narrow the lookup contract to exact slugs where possible, and treat search fallback as a separate recovery path with explicit ambiguity handling. citeturn454426view4

## Architecture and codebase-health assessment

The repo already has a sensible high-level split: Agent.Core, Agent.Planning, Agent.Tools, Agent.Memory, Agent.World.Minecraft, plus the Mineflayer adapter and Blazor UI. That is a good foundation for deepening modules rather than flattening everything into one service layer. citeturn684229view0

The best next architectural move is to strengthen the seams that already exist:
- keep `ToolDispatcher` as the only tool-execution seam, but make it fully own validation and error normalization;
- keep `WorldState` as the state snapshot boundary, but make the state immutable at the edges;
- keep `PlannerRouter` as the dispatch point, but make planning context explicit and non-lossy;
- keep the journal small and cheap, but make its semantics precise. citeturn935101view0turn322915view6turn460253view3turn268272view0

That direction aligns well with the architecture skill’s guidance: deepen modules, improve locality, and avoid introducing interfaces that are shallower than the implementation they wrap. citeturn788512view0

## Assumptions

I assumed the active target of review is the open PR #1 branch `sprint-5-tool-safety`, not `main`, because the PR and branch metadata explicitly show that branch as the merge source. citeturn605006view0turn684229view0

I assumed the README statements reflect intended design goals, not already-guaranteed runtime behavior. Where the code and README diverged, I treated the code as source of truth. citeturn684229view0turn935101view0

I did not assume any hidden test coverage beyond what is visible in the repo pages I inspected. The audit therefore focuses on code and documented behavior, not on unobserved local test runs. citeturn684229view0turn231632view1

## Open questions

Is tool validation supposed to reject at the dispatcher layer only, or also within each tool for defense in depth? The current code makes the dispatcher the natural seam, but the final contract is not yet encoded. citeturn935101view0turn684229view0

Should `failureReason` become first-class replanning data, or is the current string parameter intended to remain informational only? The current adapter discards it. citeturn460253view3

Do you want `WorldState` to be an immutable snapshot type, or is controlled mutability acceptable for performance? The current design still allows outside mutation of the underlying collections. citeturn322915view6

## Recommended next refactor order

1. Finish tool validation at the dispatcher seam.  
2. Make `WorldState` truly immutable at the boundary.  
3. Preserve replanning context in `PlannerRouter`.  
4. Convert build-origin fallback into an explicit validation error.  
5. Decide whether the journal is diagnostic-only or operationally bounded.  
6. Tighten blueprint lookup specificity. citeturn935101view0turn322915view6turn460253view3turn122887view0turn268272view0turn454426view4
