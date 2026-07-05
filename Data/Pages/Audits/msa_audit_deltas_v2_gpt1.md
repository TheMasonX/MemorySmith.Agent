# MemorySmith.Agent — Delta Audit Addendum #3

Scope: only new findings beyond the previous two audit passes.

## Executive summary

The build pipeline still has a stale-state leak: build facts are being cleared with the wrong identifier and the cleanup does not remove origin facts, so old build origins can survive across goals and influence later scans. The place-block event path also still relies on tool-name matching for completion, even though the event already carries a correlation ID, which makes multi-in-flight placement brittle and potentially misattributed. Finally, the explicit-build-origin flow is semantically inconsistent across comments and implementation: one layer says “scan near the coordinates,” while the actual decomposer can build directly at those coordinates and labels the origin provenance in a way that does not always match the source of the coordinates. fileciteturn104file0turn107file0turn109file0turn58file0turn60file0

## New findings

### 1) Build fact cleanup is using the wrong identity and leaves origin facts behind
**Confidence:** 98%

`CancelGoal()` passes `previousBuildGoal.Blueprint.Name` into `ClearBuildFacts`, but the fact keys are written and read using the blueprint id / slug form (`build:{blueprintId}:...`). `ClearBuildFacts(string blueprintId)` only removes per-block status facts, legacy progress facts, and `total`; it does **not** clear `build:{blueprintId}:origin:*`. That means the old origin can survive a canceled or replaced build and be reused by later build runs. fileciteturn110file0turn104file0turn111file0turn107file0

**Why this matters:** a stale origin is a hidden state leak. It can make later builds silently reuse coordinates from a prior job, especially when the user changes blueprints or re-runs the same one after a cancel.

**Correction:** clear all build-scoped facts by blueprint id, including origin keys. Treat cleanup as a single prefix operation over `build:{blueprintId}:`.

---

### 2) Build origin provenance is inconsistent between the request path and the planner path
**Confidence:** 87%

`BuildGoalRequest` constructs `BuildOrigin` via `BuildOrigin.FromNullable(draft.X, draft.Y, draft.Z)`; that factory defaults the source to `AutoScanned`. Later, `BuildGoalDecomposer` treats any non-null origin as explicit and rewrites the source to `Explicit`. The result is that the same coordinate origin can be described as auto-scanned in one layer and explicit in another. `BuildGoal.Description` uses `Origin.Source`, so the provenance text can disagree with the actual source of the coordinates. fileciteturn36file0turn55file0turn57file0turn58file0

**Why this matters:** provenance is not just decoration here. The code is already using source to separate explicit, player-position, and auto-scanned flows, so inconsistent source labeling makes debugging and future policy decisions harder.

**Correction:** have the request builder set `BuildOriginSource.Explicit` when the coordinates come from player intent, or move provenance assignment into a single canonical layer.

---

### 3) Explicit build-origin semantics are described one way and executed another way
**Confidence:** 84%

`BuildGoalDecomposer` documents explicit coordinates as a scan center for `FindFlatArea`, but `HtnTaskLibrary.DecomposeBuild` treats an explicit origin as coordinates to use as-is and only emits `FindFlatArea` when the origin is missing or all-zero and not explicit. In practice, the comment says “scan near this location,” while the implementation can build directly at that location. fileciteturn58file0turn60file0

**Why this matters:** this is a semantics drift bug, not just a comment nit. The user-facing behavior of “build at X,Y,Z” vs “find flat ground near X,Y,Z” needs to be unambiguous or the agent will appear arbitrary.

**Correction:** choose one contract and encode it in both the goal model and the decomposer. If explicit coordinates mean a target scan center, then `DecomposeBuild` should emit the scan step even for explicit origins. If they mean a hard build origin, then the comments should stop claiming a scan-centered fallback.

---

### 4) PlaceBlock completion still relies on first-match tool scanning instead of event correlation
**Confidence:** 91%

`BlockPlacedEvent` includes `CorrelationId`, and the dispatch path stores a per-action correlation ID plus per-block context for each `place` action. But completion still goes through `CompleteCorrelatedActionByTool("place")`, which simply finds the first dispatched `place` action and transitions it, ignoring the event’s correlation ID. The checkpoint code already uses the event correlation ID, so the data needed to complete the right action is present; it is just not used consistently. fileciteturn109file0turn100file0turn104file0

**Why this matters:** once multiple place actions are in flight, first-match completion can misattribute the wrong action, especially if events arrive late or out of order. That makes the lifecycle state less trustworthy than the checkpoint logic already assumes.

**Correction:** transition `place` correlation state from the event’s `CorrelationId` directly, and reserve tool-name scanning only as a last-resort legacy fallback.

---

## Consolidation opportunities

- Collapse build cleanup into a single `ClearBuildFactsByBlueprintId` routine that deletes origin, progress, total, and per-block facts together. The current split cleanup makes it too easy to miss a related namespace. fileciteturn104file0turn107file0
- Make place-action lifecycle handling fully correlation-driven. The current hybrid of `CorrelationId` for checkpointing and tool-name scanning for completion is a hidden contract that will keep producing edge-case bugs. fileciteturn109file0turn100file0turn104file0
- Move build-origin provenance assignment into one place. Right now the request layer, goal layer, and decomposer layer are each making partial decisions about source and meaning. fileciteturn36file0turn55file0turn58file0

## Task duplication check

I checked the nearby task surface around placement and checkpointing before adding these findings. `TSK-0123` covers skipping `PlaceBlock` at the bot’s current position, and `TSK-0125` covers per-block checkpointing, but neither one explicitly covers stale build-origin cleanup or correlation-ID-based completion. fileciteturn79file0turn106file2turn112file8

## Assumptions

- This is a static code review of the current repository state, not a runtime execution trace.
- I treated the current branch as the source of truth and used the code comments only as supporting evidence, not as proof.
- I assumed the build-origin fact namespace is intended to remain blueprint-id scoped, because the writer/reader paths already use `build:{blueprintId}:...` keys. fileciteturn107file0turn110file0turn111file0

## Open questions

- Should explicit build coordinates mean “build here” or “scan nearby and pick the nearest flat origin”? The code currently mixes both ideas.
- Should place completion be entirely correlation-driven now that `BlockPlacedEvent` already carries a correlation ID?
- Are build origin facts supposed to survive cancels by design, or is that retention accidental?