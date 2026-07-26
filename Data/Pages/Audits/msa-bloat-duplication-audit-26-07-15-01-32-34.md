# MemorySmith.Agent — Deep-Dive Bloat, Duplication & Architectural Debt Audit

**Repo:** `TheMasonX/MemorySmith.Agent` · **Branch:** `dev/round-3` · **Commit:** `0f27af1befb72e7534421a1bd5550aee1e077d96` (2026-07-11, "Sprint 60 Wave D/E")
**Method:** Full local clone; `wc`/`grep`/manual read of all 20,609 C# production lines, 14,370 test lines, 2,843 JS lines; `jscpd` (copy-paste detector) on `MineflayerAdapter/`; git blame/history on hot files; cross-referenced all 404 `Data/Tasks/*.json` records + latest handoff/audit docs to avoid re-reporting known findings.
**Scope discipline:** Only new findings, or corrections to task-tracker claims, are reported in depth. Already-tracked, still-open items are cited by TSK-# with one line, not re-derived.

---

## Executive Summary

| # | Finding | Class | Confidence | Impact |
|---|---|---|---|---|
| 1 | **TSK-0292 ("Decompose AgentBackgroundService") is marked Done but is false-complete.** The god class has *grown* every commit since being marked Done (3,826→4,019 lines) and still owns chat parsing, dispatch, recovery, replanning, dashboard push, inventory sync, and build-checkpoint tracking in one 4,019-line file. | God Class / Divergent Change / **false task-completion** | 97% | High — misleads planning; next refactor pass will re-derive scope from scratch |
| 2 | **`AgentRuntime` + all 5 `*ManagerImpl` classes are permanent dead scaffolding.** Registered in DI since Sprint 39 (now Sprint 60 — 20+ sprints), never injected into `AgentBackgroundService`, `AgentRuntime` itself is never resolved anywhere. `RecoveryManagerImpl.TryRecoverAsync` is a hard-coded `return false` stub. All five files' XML docs still say "Sprint 40 target." | Speculative Generality / Dead Code / Middle Man | 96% | Medium — 521 lines + 6 interfaces (177 lines) of pure carrying cost, zero behavior |
| 3 | **TSK-0309 ("wire WorldModel.Predict/Reconcile") is marked Done but only half-true.** `Predict` is called pre-dispatch; `Reconcile` — fully implemented, thread-safe, with an accuracy-score contract — is never called anywhere outside tests. | False task-completion / unwired infrastructure | 95% | Medium — evaluator never gets prediction-accuracy feedback despite the plumbing existing |
| 4 | **Three independent regex-based command parsers for the same grammar**, already diverged: `ChatInterpreter.GoToRegex` (7 verb phrasings) vs. `IntentManager.ParseCommandString`'s `moveMatch` (4 verb phrasings, missing `teleport to`/`tp to`/`goto`) vs. LLM-drafted-intent path in `IntentManager.BuildGoalRequest`. | Duplicated Code / Repeated Switches | 92% | Medium — silent behavioral divergence depending which layer handles a message |
| 5 | **Silent exception swallowing with zero logging in `LlmEvaluatorImpl.ParseEvaluationDirective`.** Any `JsonException` or generic `Exception` while parsing the LLM's evaluation response is swallowed into a generic `Continue` directive — no log line, no metric, no correlation ID. A silently-broken evaluator would be invisible in production. | Silently swallowed error | 90% | High — directly undermines the observation-driven replan loop this team has spent 6+ sprints building |
| 6 | **`MineflayerAdapter/index.js` (2,189 lines) has confirmed internal duplication** (jscpd: 10 clone pairs, 3.09% duplicated tokens) — most notably the `BLOCK_MINING_ALIASES`→`acceptableIds` resolution logic duplicated verbatim in `findBestBlock()` and the post-pathfind re-verify step, and a `goto()+try/catch+classifyError+sendEvent` scaffold hand-copied per action type (move/mine/place). | Duplicated Code | 88% | Medium — three copies is inevitable divergence, already showing in verb-set drift (#4 analog on the C# side) |
| 7 | **Task-tracker self-duplication**: the July-11 handoff already flags TSK-0384/0407, TSK-0407/0408, and TSK-0380–0382 vs. TSK-0387–0389 as duplicate records. Confirmed still both open. | Backlog hygiene | 99% (self-admitted in repo) | Low — pure noise/SNR cost for future audits, but compounds |
| 8 | Confirmed **still open** (no new evidence needed, tracked): `WorldStateProjector`/reconcile duplication of retry-backoff shape across 3 systems (C# `AgentBackgroundService._reconnectDelays`, JS `RECONNECT_BASE_DELAY_MS` exponential formula, `WebSocketBridge` reconnect) — matches prior audit's meta-finding, still unconsolidated. | Duplicated Code (cross-language) | 85% | Low-Medium |

**Net read:** the codebase's *documented* architecture (Managers, AgentRuntime, WorldModel.Reconcile, decomposed ABS) is meaningfully ahead of the *actual* architecture. The gap is large enough that at least two Done-status tasks (TSK-0292, TSK-0309) should be reopened or split into "scaffolding exists" vs. "scaffolding is wired" sub-tasks so status accurately reflects reality. This is the single highest-leverage recommendation in this report — it's a process fix, not just a code fix, and appears to be a recurring failure mode for this repo (see prior audits' TSK-0272 false-completion finding, cited in memory).

---

## Detailed Findings

### F1 — TSK-0292 false completion: AgentBackgroundService god class never decomposed

**Evidence.**
```
git log --format=%H -- WebUI.Blazor/AgentBackgroundService.cs (size at each commit):
2026-06-29 18:57  3,324 lines
2026-06-30 13:17  3,626 lines
2026-07-01 12:46  3,856 lines   <- TSK-0292 marked completedAtUtc 2026-07-05T20:30 falls in this range
2026-07-06 18:43  3,880 lines
2026-07-11 00:08  3,943 lines
2026-07-11 03:03  4,019 lines   <- HEAD
```
TSK-0292 record: `status: Done`, `completedAtUtc: 2026-07-05T20:30:27Z`, **zero comments**, no linked evidence/PR reference — unusual for this repo's otherwise evidence-heavy task records (compare to TSK-0406/0402/0403/0134/0144/0145 in the same handoff, which all cite exact file/line changes).

The file at HEAD is a single `public sealed class AgentBackgroundService` (line 38) with ~55 private/public methods spanning: constructor DI surface (24 optional params — a Data Clump/Primitive-Obsession smell in its own right, see F9), connection lifecycle, chat parsing dispatch (`HandleChatEventAsync`, 417 lines: 1307–1724), action dispatch (`DispatchActionsAsync`, 703 lines: 1874–2577 — the single largest method in the codebase), build-checkpoint bookkeeping (6 methods), LLM-replan-on-stall (3451–3563), dashboard push (3 methods), session-fact persistence, and stop/kick recovery.

**Why this matters beyond LOC:** this is a textbook **Divergent Change** magnet — a change to dashboard SignalR contracts, a change to build-checkpoint semantics, and a change to LLM evaluator directives all touch the same file for unrelated reasons. It is also the reason `git blame` shows nearly every sprint since Sprint 1 touching this one file (confirmed via `git log --oneline -- AgentBackgroundService.cs`, 25+ distinct sprint-tagged commits).

**Recommendation.**
1. Reopen TSK-0292 (or file a new TSK) with an explicit Definition of Done that includes a **line-count budget** for `AgentBackgroundService.cs` (e.g., ≤500 lines, orchestration-only) so "done" is falsifiable next time.
2. Do not attempt a single big-bang extraction. Use the *existing* `IPlanningManager`/`IExecutionManager`/`IRecoveryManager`/`IStateManager`/`IDashboardPublisher` interfaces (Agent.Core/Runtime/IAgentRuntimeComponent.cs) as the target seams — they were already designed with the right boundaries in Sprint 36; the problem is purely that nothing was ever migrated into them (see F2). Migrate one vertical slice at a time (dashboard push is the lowest-risk first slice — `PushStatusToDashboardAsync`/`PushChatToDashboardAsync`/`PushGoalToDashboardAsync`, 3665–3746, is already a clean, side-effect-isolated block with no shared mutable state beyond read access).
3. Add a CI line-count or cyclomatic-complexity gate on this file specifically so regressions are caught mechanically instead of by audit.

**Confidence: 97%.** Directly measured from git history; not inferential.

---

### F2 — AgentRuntime / *ManagerImpl: 20-sprint-old dead scaffolding, registered but never wired

**Evidence.**
- `Agent.Core/Runtime/AgentRuntime.cs` (36 lines): a `sealed record AgentRuntime(IIntentManager, IPlanningManager, IExecutionManager, IRecoveryManager, IStateManager, IDashboardPublisher)`. Doc comment, verbatim: *"Sprint 36 NOTE: this record is a definition target only. AgentBackgroundService is NOT refactored yet (Sprint 37)."* It is now Sprint 60.
- `WebUI.Blazor/Program.cs:403` registers `AgentRuntime` as a DI singleton. `grep -rn "AgentRuntime\b"` across the entire `.cs` tree returns **no other consumer** — nothing resolves or injects it.
- `AgentBackgroundService`'s primary constructor (lines 38–68) has 24 parameters and **none** of them are `AgentRuntime`, `IPlanningManager`, `IExecutionManager`, `IRecoveryManager`, `IStateManager`, or `IDashboardPublisher`.
- All five `WebUI.Blazor/Managers/*Impl.cs` files (521 lines total) carry doc comments stating "Sprint 40 target: AgentBackgroundService.X delegates here" — for `ExecutionManagerImpl`, `IntentManagerImpl`, `PlanningManagerImpl`, `StateManagerImpl`, `DashboardPublisherImpl`.
- `RecoveryManagerImpl.TryRecoverAsync(string, WorldState, CancellationToken)` (line 27) unconditionally `return Task.FromResult(false)` with a log line self-describing as a stub. The `ExecutionContext`-overload variant (line 47) is marginally more real (reads `RecoveryContext.IsExhausted`) but still never called by anything — it's a stub that reasons about state nobody feeds it.

**Root cause pattern:** this matches the "IntentAssessment as dead scaffolding nine sprints post-planning" finding already on record from an earlier audit round (per prior session notes) — the team has now repeated the same failure mode with a *larger* surface (6 interfaces, 5 concrete classes, 1 aggregate record) and a *longer* dormancy (Sprint 36→60, vs. the earlier 9-sprint case).

**Recommendation (greenfield-consistent — delete, don't finish):**
Given the explicit "no legacy, no fallback systems" directive, and that this scaffolding has had 20+ sprints to get wired and hasn't, the ROI calculus favors **deletion over completion**:
- Deleting `AgentRuntime.cs`, `IAgentRuntimeComponent.cs`, and all 5 `*ManagerImpl.cs` files removes 698 lines and 5 DI registrations with **zero behavior change** (nothing depends on them).
- If the team still believes in the decomposition target, keep only the 6 interfaces (as a design doc / contract) and delete the concrete stub implementations + DI registrations, re-creating real implementations only as each vertical slice is actually migrated per the F1 recommendation. Shipping a stub implementation ahead of the thing it stubs has provided no value here across 4 sprints; migrate the code, then write the interface implementation against the real logic in the same PR.
- Either way, this should feed directly into **TSK-0293** ("Remove legacy fallback and shim paths in planning and runtime policy," currently `Ready`/unstarted) — TSK-0293's description already names "duplicated policy surfaces" as in-scope; this is a concrete, fully-specified instance of that scope.

**Confidence: 96%.** Grep-confirmed absence of any consumer; doc comments self-date the staleness.

---

### F3 — TSK-0309 false completion: WorldModel.Predict wired, Reconcile is not

**Evidence.**
- `IWorldModel.Reconcile(PredictionState, ObservationState) → double` (Agent.Core/Interfaces/IWorldModel.cs:34) is fully implemented in `Agent.Core/Models/WorldModel.cs:103` with lock-protected atomic update semantics (per its own doc comment at line 101) and an implied running-average "AccuracyScore."
- `grep -rn "\.Reconcile\(" --include=*.cs | grep -v Tests` returns **zero results**. It is exercised only by unit tests.
- By contrast, `_worldModel?.Predict(...)` **is** called pre-dispatch (`AgentBackgroundService.cs:2269`) and `_worldModel?.ApplyOutcome(outcome)` is called post-outcome (line 2282).
- TSK-0309's own description explicitly names both halves as required: *"Wire WorldModel.Predict before each dispatch **and Reconcile after StatusEvent confirms completion**"* — the task is Done but only satisfies half its own acceptance criteria.

**Impact.** The LLM evaluator (`LlmEvaluatorImpl`) receives raw observation data but never receives the structured expected-vs-actual accuracy signal `Reconcile` was built to produce — exactly the gap TSK-0309 was opened to close. The infrastructure exists; the wiring that was the actual point of the task does not.

**Recommendation.** Reopen TSK-0309 or file a narrow follow-up ("TSK-0309b: wire WorldModel.Reconcile post-completion") scoped to a single call site: in `DispatchActionsAsync`'s outcome-handling path, call `_worldModel?.Reconcile(_preDispatchPrediction, observationFromOutcome)` alongside the existing `ApplyOutcome` call at line 2282, and surface the returned accuracy score into `LlmEvaluatorImpl`'s prompt context or `ExecutionContext`.

**Confidence: 95%.**

---

### F4 — Triple-duplicated navigation/command grammar parsing

**Evidence — three independent implementations of "parse a move/navigate command":**

| Parser | Location | Verb set | Notes |
|---|---|---|---|
| `ChatInterpreter.GoToRegex` | `Agent.Planning/ChatInterpreter.cs:34–36` | `go to \| goto \| move to \| walk to \| navigate to \| teleport to \| tp to` | Deterministic fast-path; requires `to` |
| `IntentManager.ParseCommandString` `moveMatch` | `Agent.Planning/IntentManager.cs:178–180` | `move \| go \| navigate \| walk`, `to` optional | Used for TSK-0205 multi-step chaining (`NextSteps` follow-up commands) |
| `IntentManager.BuildGoalRequest` (`case "navigate"`) | `Agent.Planning/IntentManager.cs:65,69–` | N/A (delegates to `HandleChatEventAsync`'s own coordinate extraction) | Comment at line 65 explicitly acknowledges navigate is "handled directly" elsewhere — a third code path for the same intent |

The `ParseCommandString` copy is **already missing** three of the seven verb phrasings the "canonical" `GoToRegex` supports (`goto` with no space, `teleport to`, `tp to`). A chained follow-up command like `"then tp to 100 64 200"` would silently fail to parse via `ParseCommandString` while the same phrase typed directly in chat would succeed via `ChatInterpreter`. This is a live, user-facing behavioral inconsistency, not a theoretical one.

The same file (`IntentManager.ParseCommandString`, lines 118–200) also independently re-implements craft/gather/build/place/smelt verb-and-default parsing that substantially overlaps `IntentManager.BuildGoalRequest`'s LLM-draft-to-goal mapping in the same class — e.g. gather's default count of `10` is hard-coded in two places in the same file (`draft.Count ?? 10` in `BuildGoalRequest`, `count = ... : 10` in `ParseCommandString`'s `gatherMatch` handling).

**Recommendation.**
1. Extract a single `CommandGrammar` (or similar) static class owning the canonical regex set + verb-synonym lists + numeric defaults, consumed by both `ChatInterpreter` (deterministic fast path) and `IntentManager.ParseCommandString` (chained follow-ups). This is a pure refactor — no behavior change to the *intended* grammar, only elimination of the drift risk.
2. Add a regression test enumerating every verb synonym against both call sites (a parameterized/theory test) so future edits to one can't silently desync from the other — this is the same defensive pattern already used for the origin-typo regression guard (TSK-0097).
3. Note: this is a **new, more specific finding** than the already-fixed TSK-0118 (which removed *dead* Gather/Build/Craft regexes from `ChatInterpreter` proper). This finding is about the still-live, still-diverging `ParseCommandString` layer, which TSK-0118 did not touch.

**Confidence: 92%.** Verb-set diff is a direct, unambiguous text comparison.

---

### F5 — Silent exception swallowing in `LlmEvaluatorImpl.ParseEvaluationDirective`

**Evidence** (`Agent.Planning/LlmEvaluatorImpl.cs:513–520`):
```csharp
catch (JsonException)
{
    return new EvaluationDirective.Continue("invalid JSON");
}
catch (Exception)
{
    return new EvaluationDirective.Continue("unparseable response");
}
```
This is a `static` method with **no `ILogger` in scope at all** — not merely a missed log call, but an architectural gap: the method has no way to log even if someone adds a `catch` body with intent to. No metric increment, no correlation ID, nothing observable. Contrast with the rest of the evaluator pipeline, which is otherwise reasonably well-instrumented (per prior audit rounds' findings about confidence scoring).

**Why this is more than a style nit:** the `reason` string (`"invalid JSON"` / `"unparseable response"`) is attached to the `Continue` directive but — unless something downstream specifically inspects and surfaces that reason string to a human — it is functionally silent. If the LLM provider starts returning malformed JSON on every call (model swap, prompt-template regression, provider outage returning HTML error pages), the evaluator would silently downgrade to "always continue" with **no signal that anything is wrong** — precisely the failure mode that would be hardest to diagnose because the system *appears* to be working (goals still complete, just without evaluator oversight).

**Recommendation.**
1. Thread an `ILogger` into `ParseEvaluationDirective` (make it instance-scoped, or accept a logger parameter) and log at `Warning` for both catch branches, including a truncated snippet of the unparseable response (mind PII/token-cost — 200 chars is plenty).
2. Add a rolling counter (e.g., `consecutiveParseFailures`, mirroring the existing `_consecutiveLlmEvalFailures` pattern already in `AgentBackgroundService`) and trip the same circuit-breaker path added for TSK-0325 if parse failures exceed a threshold — parsing failure is a symptom of the same underlying "evaluator is unreliable" condition that circuit breaker already guards against, and today the two failure modes are handled completely differently (one is invisible, one trips a breaker).

**Confidence: 90%.** Code inspection is unambiguous; the severity assessment (masking provider regressions) is a reasoned inference, not directly observed, hence not 99%.

---

### F6 — `MineflayerAdapter/index.js`: confirmed duplication via jscpd

**Tooling:** `jscpd MineflayerAdapter --min-lines 5 --min-tokens 30` → 10 clones in `index.js` (2,189 lines), 3.09% duplicated tokens, all clones intra-file (this file duplicates itself; no cross-file dupes found in the adapter directory).

**Highest-value clone (verified by hand, not just token-match):**
- `findBestBlock()` (~line 725): computes `acceptableIds` from `C.BLOCK_MINING_ALIASES[shortName]` for block-search matching.
- Post-pathfind re-verification block (~line 921, inside the `mine` action's retry loop): re-computes the **identical** `aliasNames`/`acceptableIds` derivation, with a comment noting it was added as a bug fix ("Sprint 40 P0-C (Fix): Check against all acceptable block IDs... not just the primary blockId").

This is a real duplication risk already realized once: the second copy exists *because* someone fixed a bug in one location without the fix propagating to the other (classic duplicated-logic bug pattern — the alias-resolution logic drifted, got fixed in one spot, and the other spot was a separate, easy-to-miss patch site).

**Second pattern (structural, not jscpd-flagged as an exact clone but visually confirmed):** the `move`, `mine`, and `place` action handlers each hand-roll their own `try { await bot.pathfinder.goto(...) } catch (err) { classifyError + logStructured + sendEvent(..., failed:true, reasonCode...) }` scaffold (e.g. lines ~671–684 for `move`, ~1184–1199 for `place`'s terrain-clear sub-flow). This is the same shape 3+ times with minor event-name/payload variation.

**Recommendation.**
1. Extract `resolveAcceptableBlockIds(shortName, blockId)` as a single helper; call it from both sites. Low-risk, mechanical, immediately eliminates the drift vector that already caused one bug.
2. Extract a `gotoWithClassifiedError(bot, target, { actionLabel, correlationId })` helper wrapping the goto/catch/classify/sendEvent scaffold; have `move`/`mine`/`place` call it. This directly serves the still-open **TSK-0405** ("goto() timeout for 9+ unprotected calls") and **TSK-0166** ("modularize Mineflayer adapter monolith," currently Backlog/Low) — both already-tracked tasks whose implementation this refactor would substantially de-risk. Recommend re-prioritizing TSK-0166 given this concrete evidence of active bug-causing duplication rather than leaving it as a low-priority "nice to have."
3. `index.js` at 2,189 lines in a `switch`-per-action-type structure is itself a Repeated-Switches / god-file smell mirroring F1 on the JS side — same recommendation shape: extract per-domain modules (movement, mining, placement, crafting) as already scoped by TSK-0166, using the two helpers above as the first extraction seams.

**Confidence: 88%.** jscpd output is objective; the "already caused a bug" claim is inferred from the Sprint-40-P0-C comment co-located with the duplicate, which is strong but not certain evidence of causation (the comment could describe a fix applied to both sites simultaneously that later drifted for unrelated reasons).

---

### F7 — Task-tracker duplication (process/SNR finding, not code)

The Wave D/E handoff document (`Data/Pages/Handoffs/handoff-sprint60-wave-d-e-f-20260711.md`) already self-identifies:
- TSK-0384 / TSK-0407 — duplicate ("BuildGoal.Id property for outcome correlation")
- TSK-0407 / TSK-0408 — duplicate (rate limiting + cmdQueue bounds)
- TSK-0380/0381/0382 vs TSK-0387/0388/0389 — duplicates, explicitly noted as "should be Archived"

Confirmed via task-file inspection: none of the above have been archived as of HEAD. This isn't a code-quality issue but directly affects future audit SNR (per your own stated priority) — every future audit pass has to re-discover these are duplicates.

**Recommendation.** Trivial cleanup: mark the 6 duplicate/superseded records `Archived` with a `comments` entry pointing at the canonical task. Five-minute task, removes recurring noise from every future backlog scan.

**Confidence: 99%** (self-admitted in the repo's own handoff doc; only the "still not archived" part required verification, which was done).

---

### F8 — Confirmed still-open, previously-identified cross-language retry/backoff duplication

Verified still present at HEAD (no new analysis needed, citing for completeness per your "keep looking for these smells" instruction):
- C# side: `AgentBackgroundService.DefaultReconnectDelays` (array-based, `TimeSpan[]`, line 72–76) / `_reconnectDelays` (line 178), used for WebSocket reconnect attempts (line 621–622).
- JS side: `RECONNECT_BASE_DELAY_MS` / `RECONNECT_BACKOFF_FACTOR` formula-based exponential backoff (`MineflayerAdapter/index.js:63–64, 427`), for Mineflayer bot reconnect.
- `ReplanGovernor` (`Agent.Core/ReplanGovernor.cs`) implements a third, independent graduated-backoff shape for replan throttling.

These three do not share a type, an interface, or even a backoff *strategy* (array-lookup vs. formula vs. governor-specific state machine) despite solving structurally identical problems (bounded retry with increasing delay). No task currently scopes a unification. Given the "no legacy/no duplicated fallback systems" directive, recommend a follow-up task: extract a shared `IBackoffPolicy` (C# side) and a matching small JS module, both driven by the same delay-sequence configuration where feasible (WebSocket reconnect and bot reconnect could plausibly share one; `ReplanGovernor`'s semantics are different enough — throttling successive *replans*, not *connection attempts* — that it may legitimately warrant a separate policy, but should still implement the same interface shape for consistency).

**Confidence: 85%** (mechanically confirmed still-present; "should share an interface" is a design recommendation, not a defect claim).

---

### F9 — Minor: primitive obsession / data-clump note on `AgentBackgroundService` constructor

Not a standalone action item, but worth recording since it's the direct enabler of F1: the primary constructor (lines 38–68) has **24 parameters**, most nullable/optional, several of which are clearly a cohesive configuration bundle traveling together (`maxConsecutiveFailures`, `reconnectDelays`, `maxConcurrentPlaceBlock`, `chatMaxResponseLength`, `safetyOptions` — five independently-threaded tuning knobs). This is a Data Clump: bundling these into a single `AgentRuntimeOptions` (or similar, following the existing `SafetyOptions`/`IOptions<T>` pattern already used elsewhere in `WebUI.Blazor/Options/`) would both shrink the constructor surface and give the eventual F1 decomposition a natural configuration object to pass to the extracted services instead of re-threading individual primitives through each one.

**Confidence: 80%** (design opinion, lower-severity, included for completeness per your request to flag primitive obsession/data clumps).

---

## Assumptions & Open Questions

1. **No `dotnet build`/Roslyn analyzers were run.** `nuget.org` is not in this sandbox's network allowlist (only `npm`/`pypi`/`crates.io` mirrors are permitted), so no compiler warnings, nullable-reference-type diagnostics, or Roslyn analyzer output could be gathered. All findings above are from static text/AST-adjacent inspection (grep, jscpd, manual read), not a build. If you can run `dotnet build -warnaserror` and `dotnet format --verify-no-changes` locally, that would surface a class of issues (nullability, unused usings, IDE-suggested simplifications) this pass could not reach.
2. **jscpd was only run against `MineflayerAdapter/`.** I did not run it against the C# tree — C# duplication in this report (F4) was found by manual/targeted grep, not exhaustive token-diffing, because jscpd's C#/Roslyn support is weaker than its JS support and a naive token-match across 20k lines of C# would have produced too much low-signal noise (e.g. matching boilerplate DI registration patterns) for the SNR bar you set. If a dedicated C# duplication pass is wanted, `dotnet tool install -g dotnet-format` + a proper CPD tool (e.g. PMD's copy-paste-detector with a C# grammar) run locally would be more reliable than what I could do here.
3. **No dynamic/fuzz testing was performed.** The sandbox has no Minecraft server or Node runtime with the Mineflayer dependency tree installed (no network access to install `mineflayer` itself — `registry.npmjs.org` is allowlisted but a full `npm install` of the adapter's dependencies was not attempted given the scope of this pass was static). All findings are static-analysis-derived.
4. **F5's severity claim ("would be invisible in production")** assumes no external monitoring inspects the `Continue` directive's `reason` string today. I did not trace every consumer of `EvaluationDirective.Continue` to confirm the reason is truly discarded everywhere — this should be spot-checked before treating the severity as certain (confidence reflects this: 90%, not higher).
5. **F1's line-count recommendation (≤500 lines)** is a judgment call, not derived from a team standard found in the repo (no `.editorconfig` or style doc specifies a file-size limit). Adjust to whatever threshold the team considers "orchestration-only."

---

## Suggested Sequencing (lowest-risk → highest-leverage)

1. **F7** (archive duplicate tasks) — 5 minutes, zero code risk, immediate SNR win for future audits.
2. **F6.1** (extract `resolveAcceptableBlockIds`) — mechanical, low-risk, closes a proven drift vector.
3. **F5** (log the evaluator parse failures) — small diff, high safety value, unblocks confident trust in the observation-driven replan loop this team has invested in most heavily.
4. **F3** (wire `Reconcile`) — single call site, closes out a task that's 90% done already.
5. **F4** (unify command grammar) — moderate diff, prevents a live behavioral bug (missing verb synonyms in chained commands).
6. **F2** (delete or truly migrate AgentRuntime/Managers) — decide-then-execute; deletion is a half-day task, real migration is the true start of F1.
7. **F1** (decompose AgentBackgroundService) — the big one; sequence after F2's decision since F2's outcome determines whether extraction targets already exist.
8. **F6.2 / TSK-0166** (modularize `index.js`) — largest single effort; do after the C# side proves out the pattern in F1, so the JS decomposition can mirror lessons learned.
