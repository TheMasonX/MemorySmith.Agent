# MemorySmith.Agent Deep Audit Delta
**Branch:** `sprint-35-llm-first`  
**Commit:** `d2ef16ab86d433cc62912939c213cde088dcaf05`  
**Date:** 2026-06-26  
**Purpose:** Corrections and new recommendations only, relative to the prior audit.

## What changed in this follow-up

This delta tightens the context-carry assessment and adds one important correction: the new dispatcher-side context merge is not just “broad”; it is incompatible with the dispatcher’s own schema validator unless every carried context key is declared in the target tool schema. `ToolDispatcher.ValidateAgainstSchema` rejects any unexpected property, and `MoveToTool` only accepts `x/y/z`. That means the current merge can fail dispatch as soon as a plan carries any extra context metadata. fileciteturn40file0 fileciteturn41file0 fileciteturn27file0

I also verified that the intended producer side of the feature is still not present in code. `SearchMemoryTool` returns search results, a best page id, and counts, but no coordinates; the only concrete `Context` writer in the reviewed code is the dispatcher-side mutation itself, plus the data model definition. So the end-to-end “SearchMemory writes coordinates → MoveTo reads them” flow remains aspirational, not implemented. fileciteturn30file0 fileciteturn28file0 fileciteturn7file0

## Corrections to earlier claims

The earlier audit treated the context-carry path as a broadly useful but incomplete wiring step. The correction is sharper: the current implementation is brittle because it silently turns ambient context into tool inputs, while the tool dispatcher explicitly forbids undeclared properties. In other words, the merge is not merely “too permissive”; it can become a hard failure mode for any tool call whose context includes metadata outside the schema. fileciteturn40file0turn41file0turn7file0

The earlier audit also implied the “MoveTo from context” pathway was a near-term implementation target with a clear producer. The task card shows the desired design, but it is still marked `Status: Backlog`, and the code path that would actually produce `nearestX/Y/Z` is not visible in the reviewed tree. That should be treated as an open design item, not an almost-finished feature. fileciteturn18file0

## New recommendations

### 1) Replace global context merging with explicit context hydration
Keep `ActionData.Context` as the plan-local memory, but stop copying the whole bag into `Arguments`. Instead, hydrate only the keys a tool explicitly opts into, or have the tool read `Context` directly.

Practical shape:
- define a per-tool allowlist such as `MoveTo: ["nearestWoodX","nearestWoodY","nearestWoodZ"]`
- or add a small `IContextAwareTool` contract that receives both `arguments` and `context`
- or have `MoveToTool.ExecuteAsync` pull from `Context` only after `x/y/z` are absent

Why this is safer:
- it avoids schema-validation failures from unrelated context keys
- it preserves clear tool contracts
- it makes future refactors easier because context stays ambient and arguments stay explicit

Confidence: **97%** that this is the right direction. The current validation code makes the failure mode real, not theoretical. fileciteturn40file0turn41file0

### 2) Add a focused regression test for both the happy path and the failure path
You need two tests, not one.

Happy path:
- `SearchMemory` (or a synthetic upstream tool) writes only `nearestWoodX/Y/Z`
- `MoveToTool` succeeds with no explicit coordinates in `Arguments`

Failure path:
- the same context also includes an unrelated key such as `bestPageId` or `query`
- verify whether dispatch fails today
- if it fails, the test proves why the hydration mechanism must be scoped

That second test is valuable because it captures the current fragility and prevents a future “it works on the sample case” trap. Confidence: **95%**. fileciteturn30file0turn40file0turn41file0turn27file0

### 3) Make the producer explicit before closing TSK-0004
Before closing the task, decide which component owns coordinate synthesis:
- `SearchMemoryTool` may derive it from a structured memory record
- the planner may infer it from prior observations
- a dedicated adapter may normalize it from search snippets

Right now the repo does not show a concrete producer, so the task should stay open until the source of truth is implemented and tested. Confidence: **98%**. fileciteturn30file0turn18file0

### 4) Keep the SQLite sink, but add an explicit failure boundary
The sink is wired in `Program.cs`, enabled by default in `appsettings.json`, and the project suppresses `NU1903` globally. That means the feature should be treated as an operational dependency, not just a package addition. Add a test or startup probe proving the agent still boots when the DB path is locked or unwritable, and define retention/rotation before telemetry grows without bound. Confidence: **90%**. fileciteturn8file0turn12file0turn13file0turn23file0

### 5) Reclassify the metadata status drift as an implementation risk
The task cards for both TSK-0004 and TSK-0014 are still marked open even though the handoff describes Wave D as complete. That drift is not cosmetic; it will mislead the next implementer about whether they are extending existing behavior or finishing it. Update status fields or explicitly annotate them as “code landed, follow-up tests pending.” Confidence: **99%**. fileciteturn24file0turn18file0turn23file0

## Implementation guidance for the agent

The lowest-risk path is:

1. Keep the current dispatcher merge out of the mainline contract unless the context keys are strictly allowlisted.
2. Add a single `MoveToTool`-centric hydration path first.
3. Make upstream coordinate generation explicit, then wire the producer.
4. Only after the happy path and failure path tests are green should TSK-0004 be closed.
5. Treat the SQLite sink as a separate operational change and verify it under bad I/O conditions before calling it production-safe.

That sequence keeps the codebase from accumulating another ambient fallback system under the banner of “convenience.”

## Bottom line

The new implementation is directionally useful, but the contract is still too implicit. The most important correction is that the current context merge can conflict with schema validation and does not yet have a visible producer for the coordinates it is supposed to carry. That is the next thing to fix before this branch can be considered cleanly consolidated.
