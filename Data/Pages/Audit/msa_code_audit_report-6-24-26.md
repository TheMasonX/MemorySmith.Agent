# MemorySmith.Agent Code Audit Report
**Repository:** TheMasonX/MemorySmith.Agent  
**Scope:** Current verified `main` branch plus sprint-35 implementation work reflected in the merged sprint line and roadmap docs.  
**Audit focus:** Whole codebase, with emphasis on in-progress areas, planning, tool dispatch, world-state projection/modeling, memory repositories, and recent sprint changes.  
**Date:** 2026-06-24

## Executive summary

The codebase has made substantial progress in safety, planner routing, world-state modeling, and observability. The strongest trend is positive: more behavior is being pushed into typed goals, decomposers, and bounded runtime boundaries instead of ad hoc string handling.

The main risks now are not “missing features” so much as hidden contracts that can quietly break under edge cases:

1. **Crafting decomposition does not scale prerequisite gathering by target count for iron and stone items.** A `CraftItemGoal` for 2+ items will under-gather ore/coal/cobblestone and can fail or stall. Confidence: **96%**.
2. **Build origin handling is split across multiple layers, and the direct `HtnPlanner` path still ignores explicit build origin fields.** That makes build behavior inconsistent between router/decomposer paths and fallback/direct-call paths. Confidence: **91%**.
3. **The world projection and prediction layers treat mined block names as the inventory item result in places where Minecraft drops differ.** That creates a structural mismatch between plans, predicted state, and completion checks. Confidence: **88%**.
4. **Tool execution errors are intentionally collapsed into `ToolResult(false, ...)`, but stack traces and failure structure are lost.** This makes recovery friendlier but hides programmer bugs and complicates diagnosis. Confidence: **93%**.
5. **The action queue’s “atomic clear + enqueue” guarantee is weaker than its comments claim because regular `Enqueue` and `Clear` remain lock-free.** That is a subtle concurrency-contract bug. Confidence: **84%**.
6. **Blueprint lookup now falls back to local filesystem pages at runtime, which is convenient but couples runtime behavior to checkout layout and can create duplicated/stale search results.** Confidence: **78%**.

A key positive note: several issues already on the roadmap should **not** be duplicated here. In particular, the `IBuildGoal` marker interface, semantic build locations, and world KB deployment are already explicitly tracked in the handoff/roadmap and are not re-raised as new findings.

Overall confidence in the audit: **87%**. This is a static review of the current code text and docs, not a live execution trace.

## What is working well

The sprint work has improved the codebase in several important ways:

- Tool dispatch now validates schema at the boundary and catches tool exceptions instead of letting them crash the loop.
- Planner routing is moving from type-switches to decomposer registration, which is a cleaner architectural seam.
- World-state projection now normalizes inventory keys on status refresh and marks inventory freshness explicitly.
- Memory and blueprint access now have clearer fallback paths and stronger operational logging.
- The repo includes a substantial test surface around the newer planner and craft/build behaviors.

Those are all real improvements. The remaining problems are mostly around edge cases, consistency between layers, and hidden assumptions.

## Findings

### 1) CraftItem planning under-gathers prerequisites for `count > 1`
**Severity:** High  
**Confidence:** 96%  
**Why it matters:** Multi-craft requests can silently plan too little mining/smelting/cobble gathering, which means the later craft step will fail even though the plan looked plausible.

**Evidence**
- `Agent.Planning/HtnTaskLibrary.cs` `DecomposeCraftItem(string itemId, int count, WorldState state)` computes iron and stone prerequisites from the *single-item* recipe requirement only:
  - iron: `needIngots = ingotCount - haveIngots`
  - stone: `needCobble = cobbleCount - haveCobble`
- The `count` parameter is passed through to the final `CraftItem` action, but not to the prerequisite math for iron/stone.
- Plank handling already scales with count correctly (`logsNeeded = (count + PlanksPerLog - 1) / PlanksPerLog`), which makes the inconsistency more obvious.
- Existing tests exercise `count = 1` for the prerequisite-heavy recipes; there is no visible test covering `count > 1`.

**Impact**
- `CraftItemGoal("iron_pickaxe", 2)` can mine/smelt only enough for one pickaxe.
- `CraftItemGoal("stone_sword", 3)` can gather too few cobblestone.
- This is likely to show up as repeated replanning, failed crafts, or “mystery” stalls.

**Recommendation**
- Multiply prerequisite needs by `count` for all count-sensitive branches, not just planks.
- Add explicit tests for `count > 1` on at least one iron tool and one stone tool.
- Consider refactoring the recipe logic into a small recipe model so “ingredients per craft” and “craft count” are composed once, not hand-coded in several branches.

---

### 2) Build origin handling is inconsistent across the router/decomposer/fallback stack
**Severity:** High  
**Confidence:** 91%  
**Why it matters:** Build behavior should be deterministic regardless of whether planning goes through the router, a decomposer, or the fallback HTN planner. Right now the same goal can behave differently depending on the path.

**Evidence**
- `Agent.Planning/Goals/BuildGoal.cs` stores explicit origin fields (`OriginX/Y/Z`) and `HasExplicitOrigin`.
- `Agent.Planning/Decomposition/BuildGoalDecomposer.cs` honors explicit origin and then delegates to `HtnTaskLibrary.DecomposeBuild`.
- `Agent.Planning/HtnPlanner.cs`, however, still reads build origin from world-state facts only and does not consume `BuildGoal.OriginX/Y/Z` directly.
- The sprint handoff and roadmap explicitly note that `HtnPlanner` still uses `goal is BuildGoal` and that `IBuildGoal` is an upcoming improvement. That means the design is knowingly unfinished, not accidental.
- The build decomposition logic now uses a scan/fallback story, but the direct planner path is not aligned with that story.

**Impact**
- A direct `HtnPlanner` call, a test that bypasses the router, or an unexpected fallback route can ignore explicit chat coordinates.
- That creates “works in one path, fails in another” behavior, which is especially hard to debug in agent loops.

**Recommendation**
- Make build-origin resolution a single shared service or helper that both the decomposer and fallback planner call.
- Ensure explicit origin is honored wherever `BuildGoal` is consumed, not only in the router path.
- Add a regression test that exercises a build goal with explicit coordinates through the direct fallback planner path.

---

### 3) World-state projection and prediction still assume mined block name == inventory item result
**Severity:** High  
**Confidence:** 88%  
**Why it matters:** Planning, prediction, and completion checks need to agree on what a mining action produces. If they disagree, the agent will misread its own world.

**Evidence**
- `Agent.Core/WorldStateProjector.cs` `ApplyBlockMined` strips namespace and adds `e.Block` as the inventory item key.
- `Agent.Core/Models/WorldModel.cs` `PredictMine` does the same thing.
- This is fine for `oak_log -> oak_log`, but it is not correct for common ore-to-drop cases like `stone -> cobblestone`.
- The codebase already contains comments and tests showing that stone-related gather logic expects `cobblestone` in some places, which means the model must understand drop semantics rather than only block identity.

**Impact**
- Gather completion can fail to trigger when the inventory receives the wrong key.
- The world model’s predicted inventory can diverge from actual inventory, which undermines uncertainty tracking and replanning quality.
- Downstream planner logic may “think” it has mined the right thing but still see the wrong inventory state.

**Recommendation**
- Introduce a single drop-resolution table or resolver used by both projection and prediction.
- Treat block name, mined result, and gathered target as distinct concepts.
- Add tests for at least one block with a non-identical drop result and verify both projection and prediction.

---

### 4) ToolDispatcher hides programmer errors behind friendly `ToolResult` failures
**Severity:** Medium-High  
**Confidence:** 93%  
**Why it matters:** A safety boundary is good, but it should not erase diagnosis. Right now genuine bugs and transient tool failures are collapsed into the same opaque failure path.

**Evidence**
- `Agent.Tools/ToolDispatcher.cs` validates schema and then wraps `ExecuteAsync` in a broad `catch (Exception ex)`.
- On exception, it logs only `ex.Message` to the journal and returns `ToolResult(false, ...)`.
- Stack traces, exception types, and structured error details are lost.
- The code comment explicitly says `ToolResult` is the “ONLY failure channel,” which is a strong hidden contract.

**Impact**
- Bugs inside a tool become harder to distinguish from legitimate runtime failures.
- Root cause analysis in production becomes slower.
- Silent “failure-as-data” can mask defects during sprint hardening.

**Recommendation**
- Keep the safe `ToolResult` boundary, but preserve structured exception metadata somewhere durable: error type, stack trace, correlation ID, and tool name.
- Consider a separate internal “unexpected exception” journal entry type.
- At minimum, include exception type and stack trace in the journal path, even if user-facing output stays sanitized.

---

### 5) ActionQueue concurrency guarantees are weaker than the comments claim
**Severity:** Medium  
**Confidence:** 84%  
**Why it matters:** The queue now uses `ConcurrentQueue`, which is better than a raw `Queue`, but the public API still mixes lock-free and lock-protected methods in a way that can violate the documented atomicity contract.

**Evidence**
- `Agent.Core/Models/ActionQueue.cs` uses `ConcurrentQueue<ActionData>`.
- `EnqueueAll` is lock-protected.
- `ClearAndEnqueue` is lock-protected.
- But `Enqueue`, `Clear`, `Dequeue`, and `Peek` are still lock-free.
- The method comments claim `ClearAndEnqueue` “cannot be interleaved” with single `Enqueue` calls, but `Enqueue` does not take the same lock.

**Impact**
- The queue can still experience subtle race windows around priority interrupts.
- The “atomic clear + priority action” guarantee is weaker than callers may assume.
- In practice, this can produce out-of-order actions or stale actions surviving an interrupt.

**Recommendation**
- Either enforce one lock for all mutating operations, or narrow the claim in the comment so it only describes a best-effort local atomicity.
- If the queue is meant to model strict priority semantics, use a single synchronization strategy consistently.
- Add a concurrency test that interleaves `Enqueue`, `Clear`, and `ClearAndEnqueue`.

---

### 6) Blueprint repository fallback couples runtime behavior to filesystem layout
**Severity:** Medium  
**Confidence:** 78%  
**Why it matters:** The repo now supports local blueprint file fallback, which is practical for offline/dev use, but it also makes runtime behavior depend on a discoverable source tree and can blur the line between checked-in docs and live wiki content.

**Evidence**
- `Agent.Memory/MemorySmithBlueprintRepository.cs` now tries:
  1. live gateway page lookup,
  2. local filesystem fallback under `Data/Pages/blueprints`,
  3. gateway search fallback.
- `SearchAsync` appends all local blueprints on every call.
- Local page discovery walks from `AppContext.BaseDirectory` upward until it finds a `Data/Pages/blueprints` directory.
- That means runtime behavior depends on deployment shape rather than only configuration.

**Impact**
- Production and dev behavior can diverge.
- Search results can include duplicates or stale local copies.
- The repository now does extra disk work on every search, which may be acceptable at small scale but is a real hidden cost.

**Recommendation**
- Make local-file fallback an explicit, configurable dev mode.
- Cache local blueprint loading if it remains part of runtime.
- Document precedence carefully so operators know whether a local file can override a live page.

## Additional observations

- The planner/router architecture is moving in the right direction. The decomposer registry is a cleaner seam than the old type-switching approach.
- `WorldStateProjector.ApplyStatus` normalizing inventory keys and clearing stale inventory is a good fix.
- `ReplanGovernor` is a reasonable bounded-state design, but it will need careful test coverage around transition timing and recovery because the state machine is small but easy to regress.
- `BuildGoalDecomposer` currently uses explicit origin as a scan center for flat-area detection rather than a literal build origin. That is probably the right gameplay behavior, but it should be documented as such everywhere the goal is described to avoid operator confusion.

## Assumptions

- I could not directly resolve a branch literally named `sprint-35-llm-first`, so this audit is anchored to the verified repository state on `main` plus the merged sprint-line evidence in the handoff and roadmap docs.
- This is a static audit. I did not run the test suite locally or execute the agent in a live Minecraft environment.
- Where Minecraft block drops differ from mined block names, I assumed the code intends drop-aware behavior because other parts of the repo already treat stone/cobblestone and similar relationships distinctly.
- I treated the roadmap and handoff docs as authoritative for “already tracked” work, so I avoided duplicating those items as new findings.

## Open questions

1. Should `CraftItemGoal` prerequisite planning be recipe-driven from a shared recipe model rather than hardcoded per recipe family?
2. Is the explicit build origin meant to be a literal placement origin, or only a search center for flat-ground detection?
3. Should the world model maintain a single source of truth for mined item drops, shared by the projector and predictor?
4. Is the local blueprint file fallback intended for production, or only for offline/dev runs?
5. Do you want `ToolDispatcher` to preserve full exception metadata internally, or keep the current sanitized failure channel?

## Not duplicated because already tracked elsewhere

These are real topics, but they are already explicitly on the roadmap / handoff and were therefore not counted as fresh findings in this report:

- `IBuildGoal` marker interface to replace the `goal is BuildGoal` type-check.
- Semantic build locations / LLM-assisted location resolution.
- Dedicated world KB deployment and verification.

## Recommended next engineering moves

1. Fix count scaling in craft prerequisite planning and add regression tests for `count > 1`.
2. Consolidate build-origin resolution so all planner paths behave the same.
3. Introduce drop-aware mining semantics in the world projector and world model.
4. Preserve structured exception metadata in the tool boundary.
5. Tighten the action-queue synchronization contract.
6. Decide whether local blueprint fallback is a dev-only feature or a supported runtime mode.
