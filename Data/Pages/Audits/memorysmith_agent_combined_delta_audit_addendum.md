# MemorySmith.Agent Combined Delta Audit Addendum

**Repo:** `TheMasonX/MemorySmith.Agent`
**Branch:** `dev/round-3`
**Commit:** `eceb01e2a226e3a3445f2e02d019cb3b86b97e7d`

## Scope

This report combines the confirmed deltas from the outstanding addenda. It excludes items already covered in the earlier main audit and focuses on newly confirmed brittleness, hidden contracts, and task coverage gaps.

## Findings

### 1) Build checkpoint identity is inconsistent: `Blueprint.Name` vs `Blueprint.Id`

**Severity:** High
**Confidence:** 94%

`BuildGoal` uses `Blueprint.Id` as the canonical identifier for the goal name and its completion/failure fact keys, but the build checkpoint logic in `BuildGoalDecomposer` and the fallback build path in `HtnPlanner` use `buildGoal.Blueprint.Name` for progress facts and per-block context. That creates a split-brain identity contract: if `Name` and `Id` ever diverge, build progress can silently resume from the wrong fact key or fail to resume at all. The goal model itself is already centered on `Blueprint.Id`, so the checkpoint path should match it.

This is not covered by the explicit origin tasks (`TSK-0095` / `TSK-0103`), which are about coordinate completeness and `BuildOrigin`, not build identifier consistency.

**Recommendation:** normalize all build checkpoint keys to one canonical build identifier, almost certainly `Blueprint.Id`.

---

### 2) Creative provisioning is still live in `SetGoal`, and it can race failure detection

**Severity:** High
**Confidence:** 92%

The live `SetGoal` path still starts creative provisioning when the world state reports creative mode. That directly conflicts with `TSK-0190`, whose completed description says the `/give` provisioning path was removed and creative inventory is handled by the adapter. The same path creates a linked `CancellationTokenSource` and stores it in `_goalProvisioningCts`, but the linked CTS is not disposed in the connected case, which leaks a disposable per goal start.

There is also a timing problem: `PlaceBlockGoal.HasFailed()` can still fail before provisioning finishes, because it only checks inventory state and freshness state, not whether provisioning is actively underway. That makes the goal look broken even while the provisioning loop is still in flight.

**Recommendation:** either remove the creative provisioning path entirely or make the goal failure logic aware of provisioning-in-flight state. Dispose the linked CTS as part of the connected path.

---

### 3) `MemorySmithItemRegistry` now has a wider fallback surface, but whitespace-only content still behaves like a parse path rather than a clean miss

**Severity:** Medium
**Confidence:** 78%

`MemorySmithItemRegistry` now falls back to a local checked-in page when the remote fetch returns whitespace. That is useful, but the search fallback still only runs when `content is null`. If the local file is whitespace-only or otherwise unusable, the code proceeds to parsing instead of treating the page as a miss that should still trigger search. The result is a narrower fallback surface than the implementation suggests.

I did not find a task in the current ledger that explicitly covers this local-file fallback branch. The existing item-registry tasks are about the registry and cache behavior, not this whitespace-vs-null distinction.

**Recommendation:** normalize any unusable page body into a single fallback state so that search still runs when the remote and local sources are empty or malformed.

---

### 4) Replan preserved-context merging is order-dependent and first-write-wins

**Severity:** Medium
**Confidence:** 80%

`ReplanAsync` preserves selected context keys by prefix and then merges them with `TryAdd`. That means the first action in the old plan to carry a matching key wins, and later values are silently ignored. The effective replanned state therefore depends on historical action ordering rather than an explicit precedence rule.

`TSK-0104` covers the broader replan problem — null result ambiguity and loss of typed goal data — but not this merge-precedence issue. This is a separate brittleness point.

**Recommendation:** make the precedence rule explicit. Last-write-wins is easier to reason about here, or move the carry-forward state into a typed object instead of a key-prefix merge.

## Task coverage notes

The following existing tasks are related but do not fully cover the deltas above:

* `TSK-0190` covers creative-mode recovery guards, but not the fact that creative provisioning is still live or the CTS disposal/race issue.
* `TSK-0095` / `TSK-0103` cover build-origin completeness, but not the `Blueprint.Name` versus `Blueprint.Id` checkpoint-key mismatch.
* The item-registry tasks cover the registry/cache design, but not the whitespace-vs-null fallback distinction.
* `TSK-0104` covers replan result semantics and original-goal preservation, but not preserved-context merge precedence.

## Net new action list

1. Unify build checkpoint identity on one canonical blueprint key.
2. Remove or gate creative provisioning so it cannot race failure detection.
3. Normalize registry miss handling so whitespace/malformed content still reaches search.
4. Make replan preserved-context precedence explicit.

# MemorySmith.Agent Delta Audit Addendum — Continued Deep Dive

**Repo:** `TheMasonX/MemorySmith.Agent`
**Branch:** `dev/round-3`
**Commit:** `eceb01e2a226e3a3445f2e02d019cb3b86b97e7d`

## New deltas from this pass

This addendum only includes findings that were not already called out in the earlier reports. The focus here is on transport/telemetry gaps, brittle parser assumptions, and stop-path semantics that still leave the system with hidden failure modes.

## Findings

### 1) `handleStop()` emits `stopComplete` before place/move navigation has actually finished

**Severity:** High
**Confidence:** 93%

The Node-side stop path sets `_stopRequested = true`, clears the queue, and then sends `stopComplete` immediately, even when `_dispatchingAction` is `place` or `move` and pathfinder cancellation is intentionally suppressed. In that branch, the code explicitly allows the navigation to keep running so it avoids the Mineflayer “goal changed” error. That means the `stopComplete` acknowledgment can mean “stop was requested and queued actions were cleared,” not “the bot has actually stopped moving.”

That is a semantic mismatch, not just a logging choice. The C# side can reasonably interpret `StopCompleteEvent` as “the stop has fully completed,” especially because the event type and comment both say it acknowledges full processing. This is a concrete follow-up to `TSK-0061`: the event exists, but the meaning is looser than the event name implies.

**Recommendation:** either delay `stopComplete` until the active navigation has truly quiesced, or rename/reshape the event to reflect “stop requested / queue drained” rather than “stop fully complete.” The current semantics are too strong for the implementation.

---

### 2) Unknown adapter events are silently dropped by `WebSocketBridge.ParseEvent`

**Severity:** Medium
**Confidence:** 88%

`WebSocketBridge.ParseEvent` ends with `_ => null, // unknown event type — ignored`. That means any new, renamed, malformed, or partially migrated adapter event is discarded without a structured log, metric, or fallback event. The receive loop then just skips it.

This is a brittle contract in a system that is actively evolving its event schema. It makes adapter/C# drift much harder to diagnose, because the bridge does not distinguish between “unknown but harmless” and “new event we forgot to wire.” The event system already has several typed event families; quietly ignoring unknowns makes those families harder to evolve safely.

This is not covered by `TSK-0165`, which focuses on action lifecycle telemetry, or by `TSK-0061`, which focuses on stop-event wiring. It is a separate observability gap in the parser itself.

**Recommendation:** log unknown event types at least at debug/warning level, including the `event` value and a truncated payload summary. If unknown events are expected, make that explicit in a metric rather than burying them as `null`.

---

### 3) `LlmEvaluatorImpl.ExtractJson()` is a fragile substring heuristic

**Severity:** Medium
**Confidence:** 84%

`ExtractJson()` slices the response from the first `{` to the last `}` and feeds that directly to `JsonDocument.Parse`. This works only when the LLM response is basically “one JSON object with maybe some prose around it.” Any response that contains braces in quoted text, multiple JSON objects, or code-like examples can cause the extractor to capture the wrong range or a malformed blob.

That makes the evaluator more brittle than the prompt suggests. The code is not actually validating that the model returned a compact, single JSON object; it is heuristically stripping the outermost braces and hoping they enclose the intended payload. The failure mode is especially awkward because parse failures are treated as conservative “no replan,” which can hide the underlying issue rather than surfacing it.

I did not find a specific task in the current ledger covering this extractor heuristic. The evaluator tasks I found focus on the overall replanning loop and structured result semantics, not this particular parsing shortcut.

**Recommendation:** replace the substring heuristic with a stricter response contract, or at least validate that the extracted JSON is a single top-level object with no extra brace pairs outside the payload. If the evaluator is allowed to return prose, it should be sanitized with a real JSON scanner rather than a first/last brace slice.

---

## Task coverage notes

The following existing tasks are related but do not fully cover the deltas above:

* `TSK-0061` covers wiring `mineAborted` / `stopComplete` to C#, but not the semantic mismatch where `stopComplete` can fire before the active move/place navigation has actually ended.
* `TSK-0165` covers action lifecycle telemetry, but not unknown-event observability in the generic parser.
* The evaluator-related work covers the structured response flow, but not the brittle first/last brace JSON extraction heuristic.

## Net new action list

1. Make `stopComplete` mean truly complete, or rename it to match the actual semantics.
2. Log unknown adapter events instead of silently discarding them.
3. Replace the evaluator’s brace-slicing JSON extraction with a stricter parser contract.


# MemorySmith.Agent Delta Audit Addendum — Multi-step chaining hardening

**Repo:** `TheMasonX/MemorySmith.Agent`
**Branch:** `dev/round-3`
**Commit:** `eceb01e2a226e3a3445f2e02d019cb3b86b97e7d`

## New deltas from this pass

This addendum only includes findings that were not already called out in the earlier reports. The focus here is on the multi-step chaining path, especially where the current implementation is still using an ad hoc string mini-language and hardcoded limits.

## Findings

### 1) Multi-step chaining still uses a lossy string mini-language with unguarded numeric parsing

**Severity:** High
**Confidence:** 91%

`IntentDraft.NextSteps` is just a list of strings coming from the LLM prompt, not a typed sequence structure. The prompt explicitly asks the model to emit `"nextSteps": ["<optional subsequent commands the player wants, or empty array>"]`, and the `IntentManager` then parses each string with regexes and `int.Parse` calls. That means malformed chain text can still throw instead of failing cleanly, and semantically valid-but-unexpected phrasing can be dropped on the floor as `null`.

Two specific brittle edges are visible in `ParseCommandString`:

* numeric values are parsed with `int.Parse(...)` with no `TryParse` fallback,
* counts of `0` are accepted by the `\d+` regex even though a zero-count goal is usually a no-op or degenerate request.

This is a real hardening gap, not just a style issue. `TSK-0205` covers adding multi-step chaining, but it does not cover parser robustness for malformed chain items or degenerate numeric values.

**Recommendation:** move `NextSteps` toward a typed sub-goal representation, or at minimum replace `int.Parse` with guarded parsing plus explicit validation that counts are positive and coordinates are sane.
**Implementation note:** if the model is allowed to emit free-form steps, the runtime should treat invalid steps as an explicit chain error, not as a parse exception.

---

### 2) The sequence wrapper has a hardcoded five-step ceiling that is not reflected in the prompt contract

**Severity:** Medium-High
**Confidence:** 88%

`TaskSequenceGoal` enforces `MaxSteps = 5` and throws if a sequence is longer than that. The model-facing prompt and `IntentDraft.NextSteps` description do not advertise that limit, and the intent pipeline does not appear to clamp or prevalidate chain length before constructing the wrapper. That makes the limit feel arbitrary from the outside: the LLM can legitimately emit a longer chain, but the runtime will reject it at construction time.

The limit may be perfectly reasonable, but it is currently a hidden contract. In a greenfield system, hidden limits are brittle because they become accidental behavior rather than intentional policy. The consequence is a goal-construction failure that looks like a parser or planner bug even though the actual cause is “too many steps.”

`TSK-0205` tracks the existence of chained commands, but not the maximum-step policy or how it is communicated to the model. This is a good task extension candidate.

**Recommendation:** either:

* surface the maximum step count in the prompt and prompt post-processing, or
* remove the hardcoded cap and make the chain planner enforce a policy explicitly when necessary.
  If the cap stays, it should be a deliberate, user-visible constraint.

---

### 3) The chain language is duplicated across prompt text, parser regexes, and wrapper semantics

**Severity:** Medium
**Confidence:** 86%

The multi-step feature is effectively defined in three places:

* the prompt text that teaches the LLM to emit `nextSteps`,
* the regex-based `ParseCommandString` mini-parser,
* the `TaskSequenceGoal` wrapper that advances sub-goals in order.

That duplication makes the contract harder to evolve safely. A future change to one command shape can easily update the prompt but forget the parser, or update the parser but forget the wrapper’s limit. The result would be a “working in docs, broken in runtime” mismatch that is hard to diagnose because every layer is nominally correct on its own.

**Recommendation:** consolidate the chain contract into one typed structure rather than maintaining a string DSL in the prompt and a separate regex DSL in code. Even a small typed `SequencedGoalRequest` would be a major reduction in brittleness.

## Task coverage notes

The following existing task is related but does not fully cover the deltas above:

* `TSK-0205` covers the existence of multi-step chaining, but not the parser hardening, zero-count validation, or the hidden five-step ceiling.

## Net new action list

1. Replace free-form `nextSteps` parsing with typed sub-goal data or strict validation.
2. Reject zero-count or malformed numeric chain steps before goal construction.
3. Make the five-step maximum explicit in the prompt and planner policy, or remove the hidden cap.
4. Collapse the chain contract into one place so the prompt, parser, and wrapper cannot drift independently.


# MemorySmith.Agent Delta Audit Addendum — Additional Deep Dive

**Repo:** `TheMasonX/MemorySmith.Agent`
**Branch:** `dev/round-3`
**Commit:** `eceb01e2a226e3a3445f2e02d019cb3b86b97e7d`

## New deltas from this pass

This addendum only includes findings that were not already called out in the earlier reports. The focus here is on provenance drift, lossy fact parsing, and duplicated behavior that can still split over time.

## Findings

### 1) Build-origin provenance is still being flattened into `AutoScanned`

**Severity:** High
**Confidence:** 90%

`BuildGoalDecomposer` reads stored origin facts from world state, but it still constructs the resulting `BuildOrigin` with `BuildOriginSource.AutoScanned` unless the origin was explicitly supplied in chat. That means a build origin recovered from prior state is indistinguishable from an auto-detected origin in both the object model and the goal description. `BuildGoal` then renders that source in the description, so the provenance shown to logs and downstream consumers is wrong even when the coordinates are correct.

This is a good correction target for `TSK-0103` rather than a duplicate. The task explicitly wanted provenance tracked as part of the build-origin model, but the live code still collapses stored-origin recovery into auto-scan semantics.

**Recommendation:** add a distinct provenance value for stored/recovered origin, or thread a resolution note through `BuildOrigin` so logs and downstream policies can distinguish “recovered from facts” from “auto-scanned.”

---

### 2) `TryGetIntFact` is a brittle, lossy conversion helper

**Severity:** Medium-High
**Confidence:** 88%

`HtnTaskLibrary.TryGetIntFact` contains two surprising behaviors:

* it returns `false` when the fact value is exactly `int.MinValue`,
* it silently truncates `long` and `double` values to `int` without any range validation.

That makes the helper more arbitrary than the rest of the fact-reading code suggests. It can misread world-state data without any explicit failure signal, which is especially risky because the helper is used in origin resolution and other planner-side fact reads. This is not the same issue as the earlier origin-sentinel cleanup work; it is a separate conversion-contract problem.

**Recommendation:** replace the sentinel-like `int.MinValue` behavior with explicit `TryParse`/range checks and return `false` only when the value is absent or genuinely unparseable.

---

### 3) Chat-intake still hardcodes `onlinePlayers = 1` and `playerPosition = null`

**Severity:** Medium
**Confidence:** 84%

`IntentManagerImpl` still bridges `ProcessChatAsync` to the interpreter with a default online-player count of `1` and a null player position. That keeps the pipeline moving, but it also means the interpreter’s distance gate and addressing logic are operating on a fabricated single-player world unless something else overrides it later. In a multi-player world, that increases the chance of false positives and misaddressed intents.

I did not find a separate task in the current ledger covering this exact bridge default. It is still a correctness gap rather than just a temporary placeholder, because the interpreter’s behavior changes materially when the count is wrong.

**Recommendation:** wire the live online-player count sooner, or make the default explicitly “unknown” instead of pretending the world is always single-player.

---

### 4) Creative-mode behavior is duplicated in two independent layers

**Severity:** Medium
**Confidence:** 86%

There are now two separate creative-mode shortcuts:

* `AgentBackgroundService` still performs a creative provisioning path when setting a goal,
* `HtnTaskLibrary` also short-circuits gather/craft/smelt decomposition to `/give` when `state.IsCreativeMode` is true.

That duplication makes creative mode a cross-cutting policy with two separate implementations. It is easy for one path to be removed or changed while the other remains, which would produce a confusing split between planning behavior and runtime behavior. This is a consolidation problem, not just a code-style issue. `TSK-0190` covers creative recovery guards, but it does not cover the fact that creative-mode granting logic is still duplicated across layers.

**Recommendation:** centralize creative-mode material granting in one layer or factor it into a shared helper with a single policy source of truth.

## Task coverage notes

The following existing tasks are related but do not fully cover the deltas above:

* `TSK-0103` covers build-origin consolidation, but not the remaining provenance collapse between stored origins and auto-scan origins.
* `TSK-0190` covers creative-mode recovery guards, but not the duplication of creative-mode shortcuts across planner and runtime layers.
* The chat-intake bridge still defaults to a single-player world, and I did not find a dedicated task covering that exact fallback.

## Net new action list

1. Preserve build-origin provenance for recovered/stored origins instead of flattening them into `AutoScanned`.
2. Harden `TryGetIntFact` so it does not silently truncate or reject arbitrary values.
3. Wire live online-player count into the chat intake bridge, or make the default explicitly unknown.
4. Consolidate creative-mode granting into one policy path.
