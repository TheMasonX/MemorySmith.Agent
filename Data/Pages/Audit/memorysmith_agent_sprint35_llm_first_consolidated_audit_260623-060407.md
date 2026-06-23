# MemorySmith.Agent — Consolidated Sprint 35 LLM-First Audit

**Repository:** `TheMasonX/MemorySmith.Agent`  
**Primary target:** `sprint-35-llm-first` branch / head commit `073175b03387e05592780016db345f7ae48217c0`  
**Artifacts synthesized:** earlier internal audits, the user-provided sprint-41 notes, the sprint-35 LLM-first delta audit, and the build screenshot showing the hill excavation / incomplete structure.  
**Timestamp:** 2026-06-23 06:04:07Z

## Executive summary

The branch is moving in the right direction, but the codebase is still mid-migration rather than fully aligned with the sprint-35 LLM-first architecture.

The main conclusion is not “the parser is broken.” The main conclusion is that **responsibility is still spread across too many layers**. The chat layer, goal factory, planner router, Htn planner, and build decomposer each carry part of the same domain knowledge. That creates drift, makes the build path harder to reason about, and leaves multiple sources of truth for origin resolution, intent interpretation, and execution semantics. The earlier internal audits and the user-provided notes agree on that core diagnosis. fileciteturn68file13L1-L40 fileciteturn68file15L1-L25 fileciteturn68file11L1-L20

The most urgent correctness issues are:

1. **`smelt` is routed through the wrong execution model.** The current path still maps `smelt` into a `CraftItem`-based flow, which is not furnace smelting. That is a high-confidence runtime mismatch.  
   **Confidence: 98%**

2. **Build origin handling is split across old and new code paths.** The new `BuildGoalDecomposer` honors explicit origin properties, but the legacy `HtnPlanner` build branch still reads only world-state facts. That means direct callers can bypass the newer precedence model.  
   **Confidence: 93%**

3. **The flat-area fallback contract is incomplete.** The event model still lacks searched-radius metadata, so the build system cannot precisely distinguish “small scan” from “scan 48 and still failed.”  
   **Confidence: 94%**

4. **The branch still does not fully implement the sprint-35 LLM-first intent model.** The code remains goal-centric (`GoalName`, `GoalParameters`) instead of switching to a pure `IntentDraft` pipeline.  
   **Confidence: 96%**

5. **The build system remains the weakest user-visible area.** The screenshot shows exactly the kind of terrain-aware problem the current code struggles with: the bot is building into an excavated hillside, not into a fully modeled construction envelope.  
   **Confidence: 90%**

Overall, the branch is healthier than earlier versions, but the remaining failures are now more about **architecture and world modeling** than raw parsing.

## What the repo is doing well

The strongest improvements are real:

- The planner/router split is better than before, especially with typed goal decomposers.
- World state and journaling are more observable.
- The agent loop has better rate limiting, health interrupt handling, and action correlation than earlier versions.
- Build-origin support is at least being explicitly modeled instead of being guessed everywhere.
- The codebase is clearly trying to reduce the old “regex → goal → planner” brittleness in favor of more structured reasoning. fileciteturn68file11L1-L20 fileciteturn68file15L1-L25

That direction is correct.

## Consolidated findings

### 1) LLM-first intent handling is not fully centralized yet
**Severity:** High  
**Confidence:** 96%

The sprint-35 handoff says parsers should stop creating goals and instead produce intent for the planner layer. The branch still uses `ChatInterpretation -> GoalName -> GoalParameters -> GoalFactory`, which means the interpreter still knows too much about goal construction. The previous and delta audits both point to the same gap. fileciteturn68file15L1-L25 fileciteturn68file13L1-L40

**Why it matters:**  
This is not just a naming issue. It makes the chat layer responsible for Minecraft-specific goal conventions, which is exactly the kind of coupling the new architecture is trying to remove.

**Recommendation:**  
Finish the migration to `IntentDraft` / `GoalRequest` / `GoalFactory`, and keep goal naming in the planner boundary rather than the interpreter boundary.

---

### 2) `smelt` is still implemented as craft-adjacent behavior
**Severity:** Critical  
**Confidence:** 98%

`smelt` currently goes through the `CraftItem` path. The craft tool only dispatches a crafting action from inventory; it does not represent furnace smelting. That means the user can say “smelt iron ore” and get a plan that is semantically wrong even if it passes through the runtime cleanly. The delta audit already called this out directly. fileciteturn68file15L1-L25

**Why it matters:**  
This is the kind of bug that looks small in code but is large in behavior. It can produce false confidence because the system “does something” while doing the wrong thing.

**Recommendation:**  
Split smelting into a distinct goal/tool path, or explicitly map it to a furnace workflow. Do not let `smelt` share the same execution path as `craft`.

---

### 3) Build-origin logic is duplicated and therefore brittle
**Severity:** High  
**Confidence:** 93%

The newer build decomposer respects explicit origin properties, but the older `HtnPlanner` path still reads origin facts directly. The two paths do not share a single origin-resolution abstraction. The final user notes reinforce this as one of the core sources of “the agent feels dumb” during building. fileciteturn68file15L1-L25 fileciteturn68file11L1-L20

**Why it matters:**  
If a build origin can be known in one layer and ignored in another, then the system will sometimes build correctly and sometimes “forget” why it chose a location.

**Recommendation:**  
Create a single `IBuildOriginResolver` or equivalent, and make both the decomposer and any fallback planner path consume the same resolved result. Treat partial origin data as invalid unless all three coordinates are present.

---

### 4) The build system still lacks a proper terrain-aware construction layer
**Severity:** High  
**Confidence:** 90%

The screenshot shows the agent building on a site that has clearly been excavated into a hillside, with exposed dirt walls, uneven ground, and a partially enclosed structure. That is a strong real-world sign that the current build path does not have enough world understanding to decide when to dig, when to stop, or when to schedule a repair/finish pass.

The earlier external audit argued for a `ConstructionPlan` layer, and that recommendation still looks right. The current structure is still too close to “blueprint → immediate place blocks” and not close enough to “terrain-aware construction plan → staged execution.” fileciteturn68file1L1-L40 fileciteturn68file6L1-L40

**Why it matters:**  
The agent needs to know not just *what* to place, but whether the space is actually ready, reachable, and complete.

**Recommendation:**  
Add a `ConstructionPlan` / `PlacementStep` layer that can represent:
- dig/clear steps,
- reachability checks,
- ceiling/roof completion,
- repair passes for missed areas,
- and post-build verification that compares the intended envelope to the observed terrain.

---

### 5) Flat-area fallback is still heuristic rather than fully event-driven
**Severity:** Medium-High  
**Confidence:** 94%

The handoff wanted searched-radius to be explicit so build retries can be reasoned about precisely. The current event and build logic still use a weaker heuristic. That is enough for some scenarios, but not enough for reliable auto-origin recovery in terrain-constrained builds. fileciteturn68file15L1-L25

**Why it matters:**  
A retry system that cannot distinguish “insufficient search” from “nothing found” will either retry too often or give up too early.

**Recommendation:**  
Carry searched-radius through the event model and make retry policy depend on the actual search history, not only `Area == 0`.

---

### 6) The planner is still not generic enough
**Severity:** Medium  
**Confidence:** 91%

The Htn planner still contains typed special handling and is not a pure “phase-by-phase fallback.” The router helps, but the legacy branch is still part of the active architecture. The result is that planner knowledge is spread across the chat layer, factory, router, decomposer registry, and fallback planner. fileciteturn68file13L1-L40 fileciteturn68file15L1-L25

**Why it matters:**  
Every extra layer that knows about a goal type is another place where behavior can drift.

**Recommendation:**  
Prefer the decomposer registry as the sole source of typed goal handling. Make the fallback planner as generic as possible.

---

### 7) Minor but real code health issues remain
**Severity:** Low-Medium  
**Confidence:** 57%–68%

The earlier audits noted a few lower-severity items that are worth cleaning up while the architecture is still in motion:

- `GetStatusTool` is registered twice as separate instances.
- `HasExplicitOrigin` treats any one coordinate as explicit, which is awkward for future callers.
- Some doc/handoff language still describes architecture that the code has not yet fully implemented. fileciteturn68file15L1-L25 fileciteturn68file11L1-L20

These are not the biggest problems, but they are good cleanup targets because they reduce maintenance friction.

## What I would prioritize next

### Priority 1 — Finish the intent-layer migration
Replace goal-creating chat interpretation with a real intent object, and keep goal creation in the planner boundary.

### Priority 2 — Separate smelting from crafting
Create a furnace-aware path so `smelt` cannot accidentally become `craft`.

### Priority 3 — Centralize build-origin resolution
One resolver, one precedence model, one validation rule for explicit coordinates.

### Priority 4 — Add a real construction planning layer
This is the biggest leverage point for the “building feels dumb” problem.

### Priority 5 — Make terrain completion observable
Add post-build verification that can detect missing roof segments, inaccessible ceiling placements, and unfilled hillside gaps.

## Why the build still feels “dumb”

The image makes the issue concrete: the agent is not just placing blocks; it is trying to do construction in an environment that requires terrain reasoning. When the world model does not know that the hill must be cut back, or that a roof slab is unreachable from the current position, the planner can only keep trying the same kind of place/build steps.

That means the remaining problem is less “can it understand the request?” and more “can it represent the environment well enough to finish the job?”

The likely answer is to deepen the build architecture, not to add more regexes.

## Assumptions

- I treated the repository state at `073175b03387e05592780016db345f7ae48217c0` as the source-of-truth code state for the sprint-35 review.
- I treated the user-provided sprint notes as the architecture target, even when they describe a future state rather than current code.
- I treated the screenshot as a concrete build example that validates the terrain-planning concern.

## Open questions

- Should `IntentDraft` become the only chat output shape before the next sprint, or is there a planned intermediate compatibility layer?
- Should `smelt` be a first-class goal type, or should it be folded into a separate furnace workflow module?
- Should explicit build origin be rejected unless all three coordinates are present?
- Do you want build completion to require a terrain-shape check, not just a block-count check?

## Final confidence summary

- LLM-first migration is incomplete: **96%**
- `smelt` routing is wrong: **98%**
- Build-origin logic is duplicated: **93%**
- Flat-area retry contract is incomplete: **94%**
- Build system needs a terrain-aware construction layer: **90%**
- Minor code-health cleanup items: **57%–68%**

## Bottom line

The branch is meaningfully better than the earlier versions, but it is still in transition. The most important next step is not more surface-level parsing work; it is finishing the architecture migration so intent, goals, build planning, and execution each have one clear owner.