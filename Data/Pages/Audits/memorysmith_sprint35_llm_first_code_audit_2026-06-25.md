# MemorySmith.Agent — Sprint 35 LLM-first Code Audit

**Audit date:** 2026-06-25  
**Target:** `sprint-35-llm-first` branch, anchored by commit `18648691d8abd5ad84ee255795b76ffdc0aca131` context  
**Scope:** whole codebase, with emphasis on in-progress planning, runtime, world-state, memory, and bridge layers

## Executive summary

The branch is moving toward a better architecture: typed world events, structured tool outcomes, a planner/router split, and an intent pipeline that is increasingly semantic rather than regex-driven. The important caveat is that several legacy assumptions still leak through the new model. That creates silent fallback behavior, duplicated resolution rules, and lossy wrappers that discard the richer semantics the codebase is already trying to model.

The highest-risk theme is **split-brain between modeled policy and executed policy**. Several seams look consolidated on paper but still behave as if the old contracts are in force.

## Highest-risk issues

1. **Build origin handling still degrades partial input silently.** A partially specified origin becomes `null`, which then becomes auto-detect rather than a validation error or explicit clarification.  
2. **The old `(0,0,0)` sentinel still exists in build planning.** The new `BuildOrigin` type exists, but build decomposition still treats zeroes as “missing origin.”  
3. **Mining prediction and mining projection disagree on drop semantics.** The runtime projector maps blocks to items, but the world model predictor still adds the raw block name.  
4. **`ToolDispatcher.CallWithOutcomeAsync` flattens structured outcomes back into binary success/failure.** This loses the semantics that `ActionOutcome` was introduced to preserve.  
5. **WebSocket clean shutdown is asymmetric.** The inbound channel is only completed after retry exhaustion, not on a normal disconnect.  
6. **Chat interpretation still has overlapping semantics.** The old direct-goal path is retired, but the two interpreter layers still encode different heuristics and responsibilities.

## Findings

### 1) Build origin is still silently degraded instead of being validated
**Confidence:** 97%

`IntentManager` converts build coordinates into `BuildOrigin.FromNullable(...)`, and that helper returns `null` if any one axis is missing. `GoalFactory` then treats `null` as “auto-detect.” That means a user who supplies two coordinates does not get a correction or clarification; the system silently changes the command’s meaning. fileciteturn24file0turn17file0turn18file0turn16file0

**Impact**
- user intent can be rewritten without warning
- partial coordinate mistakes are hard to diagnose
- the new value object is present, but the validation boundary is still implicit

This belongs in the build-origin consolidation task family already tracked in TSK-0103. fileciteturn14file0

---

### 2) Legacy zero-sentinel logic still survives in build planning
**Confidence:** 95%

Even with `BuildOrigin`, the planner still treats `(0,0,0)` as “missing origin” in the build decomposition path. That means a legitimate origin at world zero is indistinguishable from auto-detect or unresolved input. fileciteturn21file0turn20file0turn17file0

**Impact**
- building at the world origin is ambiguous or impossible
- missing-origin and actual-zero-origin remain conflated
- the new value object is not yet the single source of truth

This also belongs under TSK-0103. fileciteturn14file0

---

### 3) Mining prediction and mined-state projection disagree about what drops
**Confidence:** 98%

The projector and the world model do not agree about block drops. The projector maps known blocks to their actual item drops, while the predictor still adds the raw block name directly. fileciteturn39file0turn10file0

**Impact**
- prediction quality is degraded
- replanning can use the wrong inventory hypothesis
- gather completion / state reasoning can diverge from reality

This is already tracked as TSK-0113, so it should not be duplicated as a new task. fileciteturn38file0

---

### 4) Structured tool outcomes are being collapsed back into binary success/failure
**Confidence:** 95%

`ActionOutcome` already exposes rich status (`Completed`, `NoProgress`, `Blocked`, `Unreachable`, `TimedOut`). But `CallWithOutcomeAsync` maps only `ToolResult.Success` to success and everything else to `Failed`, throwing away the richer semantics. fileciteturn23file0turn28file0

**Impact**
- recovery/replanning cannot distinguish blocked from unreachable from timed out
- the runtime pays for richer modeling but does not preserve the benefit
- the architecture becomes deeper on paper than in execution

This is adjacent to TSK-0114, but it is a separate semantic-loss issue. fileciteturn35file0

---

### 5) WebSocket clean disconnect can leave the consumer waiting
**Confidence:** 88%

The background receive loop exits cleanly when the websocket closes normally, but the inbound channel is only completed after all reconnect retries fail. That makes shutdown semantics asymmetric. fileciteturn29file0turn30file0

**Impact**
- clean disconnects can leave readers suspended
- termination behavior is less predictable than it appears
- normal shutdown and permanent failure do not share the same lifecycle contract

Recommendation: complete the inbound channel on all terminal paths, not only retry exhaustion.

---

### 6) Chat interpretation has split semantics, even though the direct-goal path is gone
**Confidence:** 82%

The current task description for TSK-0118 is slightly stale. `ChatInterpreter` no longer creates goals directly; it returns `IntentDraft`. The real issue is that the two interpreter layers still overlap in responsibility and heuristics. fileciteturn25file0turn27file0

**Impact**
- the architecture still has two adjacent interpretation surfaces
- debugging intent behavior requires understanding both paths
- the “parsers never create goals” contract is only partly unified

This task should be reworded around semantic split-brain, not direct goal creation. fileciteturn36file0

---

### 7) Memory page update path can blur real failures into upserts
**Confidence:** 74%

`RestMemoryGateway.UpdatePageAsync` catches any exception while fetching the existing page and then proceeds as though the page were absent. That can hide genuine network or parsing failures. fileciteturn34file0

**Impact**
- absent-page and fetch-failure conditions are conflated
- diagnostics lose precision
- an unintended upsert path becomes more likely

Recommendation: narrow the catch logic and preserve “not found” versus “failed to fetch” as separate outcomes.

---

### 8) ActionQueue still has race-prone surface area
**Confidence:** 80%

`ActionQueue` uses `ConcurrentQueue`, but only some operations are lock-protected. `Enqueue`, `Dequeue`, `Peek`, `Clear`, `Count`, and `IsEmpty` are all independently callable, so higher-level callers can still observe stale or inconsistent queue state around clears and bulk enqueues. fileciteturn48file0

**Impact**
- queue state can look inconsistent across concurrent readers
- the “atomic” behavior only exists for a subset of operations
- the abstraction is safer than plain `Queue<T>`, but not fully coherent

This looks like a real architecture seam worth tightening, especially if the queue is part of replanning or stop/interrupt flows.

## Architecture and refactoring opportunities

### Deepen the build-origin seam
There should be one explicit origin value object, one validation boundary, one auto-scan mode, and one resolution path. Right now the contract is split across parser, factory, decomposer, and task library.

### Deepen the mining semantics seam
Drop resolution should be a shared service, not duplicated knowledge spread across runtime projection and prediction. Once that exists, mining, gathering, and planning can all agree on the same item semantics.

### Keep the rich outcome model rich
`ActionOutcome` is already the right direction. The next step is to stop collapsing its semantics at the wrapper boundary.

### Unify intent semantics
The current architecture is closer to the target shape, but there are still two interpreter layers with different heuristics. The end-state should be a single pipeline:
`IntentDraft → IntentManager → GoalRequest → GoalFactory → Planner`

Deterministic shortcuts should remain only for truly safe commands.

## What I intentionally did not duplicate

I did not re-report tasks the repo already marks as implemented in the council sweep. I also treated these as active backlog items rather than new discoveries:

- TSK-0103 — build-origin consolidation
- TSK-0113 — mining drop-resolution table
- TSK-0116 — creative-mode build decomposition
- TSK-0118 — chat interpretation split-brain
- TSK-0105 — documentation drift

Where current code still belongs under one of those tasks, I called that out explicitly instead of inventing duplicate work.

## Assumptions

- I treated the accessible `sprint-35-llm-first` branch contents as the code under review.
- I assumed `(0,0,0)` is a valid coordinate and should not be overloaded as a missing-value sentinel.
- I assumed the goal is to remove legacy fallbacks rather than preserve them indefinitely for compatibility.

## Open questions

- Should partial build coordinates trigger clarification, auto-scan, or hard rejection?
- Should auto-scan be a first-class `BuildOriginSource` value rather than implied by absence?
- Should tool outcomes preserve semantic failure categories at the `ToolResult` layer?
- Should `ChatInterpreter` remain as a pattern-only fallback or be retired after the semantic pipeline stabilizes?
- Should clean websocket disconnect complete the inbound channel immediately?

## Top recommendation

Start with the build-origin seam. It has the highest leverage because it is already partially modeled, still leaks sentinel logic, and touches parsing, goal creation, planning, and build execution.
