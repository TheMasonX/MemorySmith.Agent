# MemorySmith.Agent Deep Dive Code Audit
Repository: `TheMasonX/MemorySmith.Agent`  
Scope: latest commit on PR #1 (`sprint-5-tool-safety`, head `6392007ab35efe0f460006b7f46233375b36187a`) against `main` (`6fef0c36589b33e3b263e881b084a62bb01fd5f8`). The PR body describes Sprint 5–6 goals and explicitly lists the planned tasks to avoid duplication. fileciteturn0file0

## Executive summary

This branch is doing a lot of the right architectural things: the repo is organized into bounded contexts, the dispatcher is becoming a single safety boundary, and the roadmap documents already separate completed work from upcoming Sprint 24 items. The clearest evidence of the architecture is the bounded-context split in `Data/Pages/architecture.md`, the dispatcher routing model, and the sprint tracker. fileciteturn16file0turn14file0turn15file0

The biggest concern is a probable interface contract mismatch in the tool layer. `ITool` requires an `InputSchema` property and an `ExecuteAsync(JsonElement, ...)` signature, but `SearchMemoryTool` and `CreatePageTool` in the current branch are implemented with `ExecuteAsync(ActionData, ...)` and, in the fetched files, do not expose `InputSchema`. That is a likely compile-time or integration-time break unless there is unseen generated/partial code elsewhere. fileciteturn19file0turn36file0turn38file0

The next most important gap is testability around time-dependent logic. The branch already introduces `ITimeProvider`, and the roadmap explicitly lists a `TimeProvider` abstraction as an upcoming Sprint 24 task. However, `ReplanGovernor` still uses `DateTimeOffset.UtcNow` directly for stall detection and auto-recovery, which means timing behavior is still harder to test deterministically than the architecture intends. This is a known gap, not a duplication risk. fileciteturn31file0turn23file0turn14file0

Overall assessment: the direction is strong, but the codebase still has a few “seams” where the documented architecture and the actual implementation may diverge. Confidence in the overall architecture is high; confidence that the current branch is build-clean is lower because of the tool interface mismatch. The findings below use grounded confidence values and separate confirmed issues from open questions.

## What the branch is already doing well

The project is clearly moving toward a cleaner architecture:
- `ToolDispatcher` is intended to be the single consolidated dispatcher, replacing `ToolEngine` and `ToolRegistry`, and the docs say dispatch now performs name lookup, schema validation, execution, and journaling at one boundary. fileciteturn17file0
- The architecture doc clearly separates Agent Core, MemorySmith knowledge, and world adapters, which is the right shape for future adapter growth. fileciteturn16file0
- The sprint tracker shows that Sprint 5 and Sprint 6 tasks are already encoded as historical work, while Sprint 24 priorities are tracked separately, so there is a real attempt to avoid duplicate work. fileciteturn14file0turn15file0
- `WorldState` now carries `StructuredFacts` with provenance and a bounded cap, which is a meaningful step away from an unbounded bag of ad-hoc facts. fileciteturn24file0turn25file0
- `LocalKnowledgeResolver` is explicitly deterministic-first, keeps the LLM out of the hot path, and uses a priority-ordered retrieval pipeline with confidence scoring. That is a good architectural decision for maintainability and reproducibility. fileciteturn34file0

## Prioritized findings

### 1) Tool interface contract mismatch in `SearchMemoryTool` and `CreatePageTool`
**Severity:** High  
**Confidence:** 95%

`ITool` requires `InputSchema` and `ExecuteAsync(JsonElement, ...)`. In the fetched branch, `SearchMemoryTool` and `CreatePageTool` are written with `ExecuteAsync(ActionData, ...)`, and the files shown do not define `InputSchema`. That is inconsistent with the interface definition and with the dispatcher/docs that describe schema validation before execution. fileciteturn19file0turn36file0turn38file0turn17file0

**Why this matters:**  
If this is exactly the committed code, the repo either fails to build or the tool layer is relying on hidden/generated code that is not visible in the branch. Either way, the contract is brittle. If these tools bypass `InputSchema`, they also bypass the safety story that Sprint 5 claims to implement. fileciteturn17file0turn0file0

**Architecture implication:**  
This is more than a local bug. It suggests the repository may still have two competing tool styles: one tool API for `ITool`/schema-driven dispatch and one older `ActionData`-driven pattern. That kind of split is exactly what the branch is supposed to eliminate. fileciteturn17file0turn19file0

**Suggested fix:**  
Normalize every tool to one interface contract only. Prefer a single `ITool` shape with `JsonElement` input and an explicit `InputSchema` on every implementation. If `ActionData` is still needed for adapters, isolate it behind a translation layer instead of letting it leak into tool implementations.

---

### 2) Time-dependent control flow is still not fully injectable
**Severity:** Medium  
**Confidence:** 84%

The branch introduces `ITimeProvider`, and the roadmap explicitly lists a `TimeProvider` abstraction as an upcoming priority. But `ReplanGovernor` still uses `DateTimeOffset.UtcNow` directly for stall timing and timeout recovery. fileciteturn31file0turn14file0turn23file0

**Why this matters:**  
This makes stall/recovery behavior harder to test deterministically and increases the chance of flaky timing edge cases. The branch is already acknowledging that this needs to be abstracted, so this is a real architectural gap, not a duplication issue. fileciteturn14file0turn31file0

**Suggested fix:**  
Inject `ITimeProvider` into `ReplanGovernor` and any other timing-sensitive paths. This should also reduce the number of hidden `UtcNow` dependencies in the codebase and make the sprint tests more stable.

---

### 3) `WorldState.SetFact(string, object?)` updates only the legacy map, not `StructuredFacts`
**Severity:** Medium  
**Confidence:** 67%

`WorldState.Builder.SetFact(string key, object? value)` writes to the legacy `Facts` dictionary only. The provenance-tracked overload writes to both `Facts` and `StructuredFacts`. fileciteturn24file0

**Why this matters:**  
That split is easy to misuse. A caller that uses the legacy overload can silently bypass provenance, ordering, and the `MaxFacts` trimming behavior. That weakens the newer world-model semantics and can reintroduce “mystery facts” with no source. fileciteturn24file0turn25file0

**Suggested fix:**  
Either deprecate the legacy overload or make the intent explicit with names that distinguish “diagnostic-only” from “structured/provenanced” facts. If the legacy path must remain, add a guardrail test and document exactly which call sites are still allowed to use it.

---

### 4) World-fact retrieval may be noisier than intended
**Severity:** Medium  
**Confidence:** 63%

`LocalKnowledgeResolver` scans `StructuredFacts` using `fact.Key.Contains(normalizedId, OrdinalIgnoreCase)`. That is a broad substring match, not a strict semantic lookup. fileciteturn34file0

**Why this matters:**  
Substring matching can surface unrelated facts when keys share fragments, especially once the world-model vocabulary grows. The confidence scoring helps, but it does not solve retrieval precision. fileciteturn34file0

**Suggested fix:**  
Move toward typed world-fact keys or an index structure with exact-key and prefix-aware matching, then use a separate semantic layer only when exact lookup fails. This would fit the repo’s deterministic-first approach better than raw substring scan.

---

### 5) Alias/compatibility drift is a continuing maintenance risk
**Severity:** Low to Medium  
**Confidence:** 74%

The branch intentionally supports aliasing, such as `GetStatusTool` forwarding to `ActionProtocol.Status`, while the docs also note the deletion of the old duplicate status tool. This is sensible as a compatibility bridge, but it creates a permanent risk of name drift if aliases proliferate without a registry of canonical names. fileciteturn35file0turn9file0turn17file0

**Why this matters:**  
The architecture is already trying to reduce the surface area from multiple tool classes to a consolidated dispatcher. Alias growth can quietly undo that simplification if the project does not define a canonical naming policy. fileciteturn17file0

**Suggested fix:**  
Maintain a single canonical tool name map plus an explicit alias table with tests that lock down intended synonyms and disallow accidental duplicates.

## Architecture review through a “codebase health” lens

### Strong moves already made
The repo is converging on a deeper-module design:
- one dispatcher,
- one world adapter boundary,
- a separate knowledge boundary,
- a richer world model with provenance,
- and a deterministic-first resolver path. fileciteturn16file0turn17file0turn34file0

That is exactly the direction you want if you are trying to reduce incidental complexity and avoid a “many small services, no clear seams” architecture.

### Remaining architectural risks
The main risks are seam leaks:
- older `ActionData`-style tool implementations surviving alongside `ITool`-style schema-driven tools,
- time remaining implicit instead of injectable,
- broad string-based fact lookup instead of a typed/queryable world model,
- and compatibility aliases becoming de facto APIs. fileciteturn19file0turn24file0turn23file0turn34file0turn35file0

### Refactoring opportunities that fit the current architecture
1. Collapse the tool surface to one canonical contract and one adapter layer.  
2. Push all wall-clock usage behind `ITimeProvider`.  
3. Promote `StructuredFacts` to the primary world-fact API and de-emphasize the legacy map.  
4. Make retrieval types explicit instead of string-substring based.  
5. Add “contract tests” around the dispatcher so tool schemas, names, and aliases cannot drift quietly.

## Sprint-plan duplication check

I checked the current roadmap and phase task files before treating anything as a gap. The Sprint 24 list already includes:
- an integration test for `TryInterruptOnDamage`,
- a `GatherGoalDecomposer` TargetCount pass-through fix,
- `TimeProvider` abstraction,
- and an `IWorldObservationGateway` design note. fileciteturn14file0

That means the time abstraction issue is already on the board, and the audit should not duplicate it as a new task. Similarly, the phase task history shows that Sprints 5 and 6, and many later follow-ons, are already tracked as completed or deferred work. fileciteturn15file0


## Continued findings from a second pass

### 2) `SearchMemoryTool` / `CreatePageTool` appear to have drifted away from the `ITool` contract
**Severity:** High  
**Confidence:** 95%

The `ITool` interface requires `InputSchema` and `ExecuteAsync(JsonElement, ...)`. `MoveToTool` follows that contract, but the PR diff shows `SearchMemoryTool` and `CreatePageTool` switched to `ExecuteAsync(ActionData, ...)` while the fetched files do not expose an `InputSchema` property. That is either a build break or a hidden partial/generated contract that the repo does not surface here. Either way, the tool layer is inconsistent with the documented dispatcher boundary. fileciteturn19file0turn48file0turn49file0turn38file0

### 3) Schema construction is inconsistent and sometimes wasteful
**Severity:** Medium  
**Confidence:** 82%

Some tools cache their schema as a `static readonly JsonDocument` (`FindFlatAreaTool`), while others parse JSON inline in the property getter (`MoveToTool`, `GetStatusTool`). That means the codebase has at least two schema patterns in circulation, which raises the risk of accidental allocations, disposal mistakes, and future drift. This is not just style debt; the inconsistency makes tool authors guess which pattern is “correct.” fileciteturn42file0turn48file0turn35file0

### 4) World adapter shutdown is brittle and platform-specific
**Severity:** Medium  
**Confidence:** 79%

`MinecraftAdapter.DisconnectAsync` shells out to `kill -TERM <pid>` and then swallows any failure before falling back to a force-kill. That is fragile for a few reasons: it depends on an external `kill` binary, it is Unix-shaped, and the exception handling can hide shutdown failures. The fallback is good, but the graceful path should not rely on an OS command when the code already has a live `Process` object. fileciteturn52file0

### 5) The world model and resolver still rely on brittle string heuristics
**Severity:** Medium  
**Confidence:** 76%

`LocalKnowledgeResolver` matches `StructuredFacts` with `fact.Key.Contains(normalizedId, StringComparison.OrdinalIgnoreCase)` and declares ambiguity when the top two scores are within `0.05`. Both choices are somewhat arbitrary and can produce surprising matches or false ambiguity on keys that merely share a substring. This is workable for a stub, but it becomes a maintenance trap as the world model grows. fileciteturn34file0

### 6) Legacy fact storage still offers an escape hatch around provenance
**Severity:** Medium  
**Confidence:** 68%

`WorldState` keeps both the legacy `Facts` dictionary and the new `StructuredFacts` list, and `Builder.SetFact(string, object?)` updates only the legacy path. That preserves backwards compatibility, but it also means call sites can bypass provenance and bounded structured storage unless they intentionally use the newer overload. The architecture doc prefers `StructuredFacts`, but the type still exposes a weaker write path. fileciteturn24file0turn25file0

### 7) Replan context preservation is hard-coded to current tool names
**Severity:** Low-Medium  
**Confidence:** 71%

`HtnPlanner.ReplanAsync` preserves only context keys starting with `SearchMemory:`, `CraftItem:`, `FindFlatArea:`, `Build:`, and `MoveTo:`. That makes replay behavior highly dependent on tool naming conventions, so renaming a tool or introducing a new action can silently drop useful context. The method works, but it is a brittle implicit contract rather than a typed one. fileciteturn53file0

### 8) Tool aliases are useful, but they should be treated as migration-only APIs
**Severity:** Low-Medium  
**Confidence:** 73%

The repo intentionally registers compatibility aliases such as `GetStatus` for `Status`, and the docs call that out as a deliberate cleanup. That is fine short-term, but it should be treated as a migration layer, not permanent API surface. Otherwise, every alias becomes another thing that can drift, be documented inconsistently, or be accidentally relied upon by downstream planners. fileciteturn35file0turn17file0


## Assumptions

- The branch snapshot is the one referenced by PR #1 and the files fetched from the `sprint-5-tool-safety` ref represent the audit target. fileciteturn0file0
- The fetched tool files are representative of the actual current code on that branch; if there is unseen generated/partial code elsewhere, it may change the severity of the tool contract issue, but not the fact that the surface is inconsistent as presented. fileciteturn19file0turn36file0turn38file0
- The roadmap and phase task docs are current enough to use as the source of truth for duplication checks. fileciteturn14file0turn15file0

## Open questions

- Is there any hidden partial/generated code that supplies `InputSchema` and the `JsonElement` overload for `SearchMemoryTool` and `CreatePageTool`? The fetched files do not show it.  
- Is `WorldState.SetFact(string, object?)` still used intentionally for diagnostic-only facts, or should it be retired?  
- Should the world-fact resolver move to exact-key or typed lookup before adding more observation sources?  
- Is `ReplanGovernor` expected to stay on direct `UtcNow` until Sprint 24, or should `ITimeProvider` be wired in immediately?  
- Do tool aliases have a canonical registry, or are they intentionally ad hoc?  
- Should `MinecraftAdapter` use a cross-platform managed shutdown path instead of shelling out to `kill`?

## Confidence summary

- Tool interface mismatch: **95%**
- Time injection gap in replan logic: **84%**
- Legacy-only `SetFact` path causing provenance drift: **68%**
- World-fact substring matching being too loose: **76%**
- Alias drift risk: **73%**
- Adapter shutdown brittleness: **79%**
- Schema construction inconsistency: **82%**
- Overall architectural direction is healthy: **88%**

## Bottom line

The branch shows real architectural improvement, not just patchwork fixes. The strongest concern is the tool-layer contract mismatch, because it threatens both build integrity and the new safety boundary. After that, the next most valuable work is to finish the time abstraction and tighten the world-model/query seams so the implementation matches the architecture the repo already says it wants. fileciteturn17file0turn19file0turn23file0turn34file0turn14file0
