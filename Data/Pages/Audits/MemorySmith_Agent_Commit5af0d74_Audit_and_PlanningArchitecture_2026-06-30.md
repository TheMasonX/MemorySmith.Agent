# MemorySmith.Agent — Commit Audit + Planning Architecture Assessment

**Commit reviewed:** `5af0d749d316587d8f32c805e310de84e62a5fa1` ("Sprint 56 Wave B+C — security fixes, sequence fix, LLM eval, configurable safety")
**Branch:** `dev/round-3` (not yet on `main`, which is at `d6dc26e`)
**Method:** `git clone` of the public repo + `git show`/`git log` against the exact SHA (your link's hash was 41 hex chars, one too many — I matched it via `git log --all` rather than the GitHub API, which also happened to be rate-limited for unauthenticated requests).
**Context loaded:** the 4 prior audit docs and the `2026-06-30-external-audits-council.md` synthesis this commit was implementing against, plus the planning/intent source tree (`IntentManager.cs`, `HtnPlanner.cs`, `PlannerRouter.cs`, decomposers) for the architecture section below. I did not re-derive findings already in those docs — only new findings and direct connections to your stated goal are below.

---

## Part 1 — Commit Correctness Audit

### 1.1 TaskSequenceGoal.IsComplete fix (TSK-0274) — **Correct, but the contract is fragile**
**Confidence: 85%**

The diagnosis in the commit message is right: the old `IsComplete` only checked `_currentStep >= _steps.Count`, which `TryAdvance()` could never satisfy on its own, so sequences could never self-report completion. The fix — delegate to `_steps[_currentStep].IsComplete(state)` — is correct and is backed by 16 new unit tests that pin the behavior precisely.

The residual risk is in the **shape of the fix, not its logic**: `TaskSequenceGoal.IsComplete()` now means two different things depending on who's asking:
- "the whole sequence is done" (when `_currentStep` is on the last step), or
- "the current step is done, please call `TryAdvance()`" (when steps remain).

This is disambiguated correctly today at both real call sites (`AgentBackgroundService.cs:1712` via `TryAdvanceSequence()`, and `:1549` via the event-driven path), so **current behavior is correct**. But it's an easy trap for a future change: any new code path that calls `goal.IsComplete(state)` and treats `true` as "goal is done" without checking `is TaskSequenceGoal` first will silently truncate a multi-step plan after step one. There is no compiler-enforced or test-enforced guard against this — it's tribal knowledge encoded in two call sites.

**No integration test exercises this through the real dispatch loop.** All 16 new tests call `TaskSequenceGoal` methods directly with a fake `IGoal`; none drive a 2-or-3-step sequence through `DispatchActionsAsync` to confirm the queue is actually cleared, the next step's plan is actually generated, and dashboard/journal state doesn't leak between steps. Given this is the exact mechanism your stated goal depends on (stringing actions together without human input), I'd treat "multi-step sequences work end-to-end" as **unverified, not confirmed**, despite the unit tests passing. This echoes the council's own Testing seat concern, just localized to the one feature that matters most for your ask.

**Recommendation:** add one `[TestFixture]` integration test that constructs a 2-step `TaskSequenceGoal`, drives it through `DispatchActionsAsync` with a fake tool caller, and asserts both steps execute and the goal disappears only after step 2.

### 1.2 `/give` injection fix (TSK-0275) — **Correct, present in 3 of 3 needed places**
**Confidence: 92%**

Verified independently in `creativeProvider.js` (regex `^[a-zA-Z0-9_]+$` after stripping `minecraft:`) and `AgentBackgroundService.SanitizeBlockName` (same regex, same stripping). Both reject rather than escape, which is the right call for a command-injection vector. The C# and JS implementations are logically identical, which matters since they guard the same attack surface from two different code paths (host-side provisioning and adapter-side provisioning).

Minor: the regex and the `minecraft:` strip are duplicated verbatim in two languages with no shared source of truth. Low risk today (45 lines, easy to eyeball), but if the allowed-character set ever needs to change (e.g. to support modded namespaced IDs with colons), it's two places to update and no test that would catch a drift between them.

### 1.3 DeniedCommands / AllowDestructiveCommands (TSK-0277, TSK-0286, TSK-0287) — **Correct and well-wired**
**Confidence: 90%**

I traced this end-to-end: `appsettings.json` → `SafetyOptions` (bound in `Program.cs`) → `IOptions<SafetyOptions>` injected into `AgentBackgroundService` → `DeniedCommands` property falls back to the static default when no options are bound → checked before every `/`-prefixed `Chat` dispatch, with `AllowDestructiveCommands` as the explicit override. This is a legitimate hard safety layer against LLM-hallucinated or adversarial command dispatch, and the default-false posture is the right one.

Two small things worth a cleanup pass, not blocking:
- `/w`, `/teammsg`, and `/tm` each appear **twice** in the denied-command list (both in the C# default and in `appsettings.json`). Harmless — it's a `HashSet`/JSON array, not a state machine — but it suggests the list was assembled by concatenating overlapping sources without a final dedupe pass.
- The list mixes genuinely destructive/admin commands (`/op`, `/ban`, `/stop`) with ordinary chat/info commands (`/list`, `/me`, `/tell`, `/msg`, `/w`). Blocking the LLM from sending `/tell` isn't a security necessity — it's a behavioral policy choice. Not wrong, just worth being intentional about: if the goal is "the LLM can never escalate privileges or destroy server state," the list is doing double duty as a chat-etiquette filter too. Fine if that's deliberate.

### 1.4 BlockNotFound retry counter (TSK-0276) — **Correct**
**Confidence: 88%**

The old code only accepted the count if it was stored as a `string`; the new `int.TryParse(pc?.ToString(), ...)` accepts both boxed `string` and boxed `int`/other numeric types. This matches the stated bug (some write path was presumably storing a boxed `int` instead of always going through `.ToString()` first). I didn't trace every writer of `event:BlockNotFound:Count:*` to confirm which one was inconsistent, so I can't independently confirm the *root cause* diagnosis, but the fix is a strict superset of the old behavior — it can't make things worse, and it directly closes the type-mismatch class of bug the council flagged as highest-ROI.

### 1.5 LLM parse-failure signal: IsSuccess/FailureReason (TSK-0278) — **Correctly implemented, but doesn't yet change runtime behavior**
**Confidence: 80%**

This is the one I'd flag most carefully, because the commit message and the council's AC-5 both frame it as "treat parse failure as a signal, not silence" — and the *plumbing* for that is genuinely done well (structured `FailureReason` enum-like strings, `ExtractJson` returning `null` instead of a misleading `"{}"`, internal visibility + 16 new tests covering empty/malformed/no-JSON cases). But trace it to its only two call sites in `AgentBackgroundService.cs`, and both of them still do exactly what they did before:

```csharp
if (evalResult.ShouldReplan) { ... break; }
```

`ParseEvaluationResult`'s failure branches all construct `EvaluationResult(false, ...)` — `ShouldReplan` is always `false` on a parse failure, exactly as before the fix. So a malformed LLM response and a deliberate "no, don't replan" response are now *distinguishable in logs and tests*, but the agent's actual behavior — silently continue the current plan — is identical either way. The "signal not silence" framing oversells the current state of the fix: the signal exists, but nothing downstream consumes `IsSuccess`/`FailureReason` to do anything differently (e.g., retry the LLM call once, fall back to a heuristic evaluator, or escalate to a forced replan after N consecutive parse failures).

This isn't wrong to land incrementally, but I'd correct the framing in the next handoff doc: AC-5 is "structurally satisfied" but not "behaviorally satisfied." If a parse failure starts happening repeatedly (e.g. the LLM provider changes its response format), the agent will keep silently continuing a stale/failing plan with no visible symptom besides a warning log line — which is precisely the failure mode this task was meant to close.

**Recommendation:** add a consecutive-parse-failure counter (separate from `_consecutiveFailures`, which tracks action failures) that forces `ShouldReplan = true` after e.g. 3 consecutive `IsSuccess=false` evaluations, on the theory that an evaluator that can't be understood is as useless as one that says "continue" forever.

### 1.6 Dead code / housekeeping
- `chatFilter.js` deletion (TSK-0279): confirmed dead by the prior audit; deletion is clean, no remaining references (`git grep chatFilter` after the commit returns nothing in the affected paths I checked).
- `appsettings.json` is missing a trailing newline after this commit's edit — cosmetic, not worth a ticket.

### Net assessment of the commit
Six of seven Sprint 56 Wave B items, plus both Wave C items, do what they claim. The security fixes (1.2, 1.3) are the strongest part of this commit — concrete, well-scoped, low-risk. The `TaskSequenceGoal` fix (1.1) is correct but under-tested at the integration level for the exact capability your stated goal depends on most. The LLM-evaluator fix (1.5) is the one place where "fixed" is doing more rhetorical work than the code currently backs up.

---

## Part 2 — How this connects to "string together actions and replan intelligently"

You asked me to prioritize research toward: multi-step autonomous execution, dynamic replanning when reality diverges from expectation, and rich, extensible modeling of Goals/Plans/Intents/WorldState/Actions/Results with a broad ad hoc toolset. I went and read the planning/intent layer (`IntentManager.cs`, `HtnPlanner.cs`, `PlannerRouter.cs`, the decomposers) to see how this commit's changes sit inside that bigger picture, since none of those files were touched by this commit but they're where the ceiling actually is.

**Confidence in this section: 80-85%** — based on direct reading of the cited files, not exhaustive tracing of every code path.

### 2.1 The only "multi-step" primitive is a fixed-length array, capped at 5
`TaskSequenceGoal` is a flat `IReadOnlyList<IGoal>` with `MaxSteps = 5`, advanced one index at a time, no branching, no insertion, no conditional steps. It's a reasonable v1 for "gather wood then build a house," but it cannot represent the kind of plan your goal implies — e.g. "build a house, and if I run out of planks midway, go gather more wood, then resume" needs a step inserted *into* an in-progress sequence, not a fixed list resolved once at parse time. There's no data model today for "plan" as a thing distinct from "a list of goals decided once."

### 2.2 The LLM evaluator — your only replan decision point — is blind to sequences
This is the most direct and, I think, most actionable finding. `LlmEvaluatorImpl.AppendGoalContext` branches on `goal is IBuildGoal`, `goal is IItemSpecGoal`, `goal is CraftItemGoal`, or a `Navigate:` name prefix — checking the **wrapper** type. When `goal` is a `TaskSequenceGoal`, none of these match, because the interesting type information lives on `goal.CurrentStep`, not on the `TaskSequenceGoal` itself. So when the agent is partway through a multi-step plan and something goes wrong, the LLM evaluator's prompt says only:

```
Goal: Sequence:gather_oak (TaskSequenceGoal)
```

— with none of the gather-progress, build-progress, or skip-reason context that every other goal type gets, and with zero indication that a 3-step plan exists, which step it's on, or what's still ahead. The evaluator that's supposed to decide "should I abandon this and replan" is making that call with strictly less information than it has for a single-goal task. This directly undercuts "produce dynamic responses... and replan as needed" for exactly the multi-step case you care about most.

**Fix is small and contained:** unwrap to `seq.CurrentStep` before the existing `is IBuildGoal` / etc. checks, and prepend a line like `Sequence: step 2/3 (build_house) — 1 step remaining after this`.

### 2.3 Replanning today regenerates the same plan from scratch; the LLM's diagnosis is discarded
When the evaluator says `ShouldReplan = true`, both call sites either `break` out of the dispatch loop (causing `PlanAsync` to run again for the *same* goal) or clear the queue and send the LLM's free-text `Suggestion` to **chat**, for the human player to read. Neither path feeds the suggestion — or any structured equivalent of it — back into plan generation. So if the LLM diagnoses "skip block #9, it's occupied by stone," the system's actual response is to regenerate the identical plan via `HtnPlanner`/`DecomposerRegistry`, which has no mechanism to know block #9 should be skipped. In the worst case this can loop: same failure, same diagnosis, same regenerated plan, repeat.

This is the gap between "the agent notices something's wrong" (which this codebase does reasonably well — `WorldStateDiff`, threat detection, stall detection are all decent signals) and "the agent does something different as a result" (which today is binary: keep going, or start over identically). Closing this is probably the single highest-leverage change for your stated goal, ahead of adding more goal types.

**Direction, not a full design:** change `EvaluationResult.Suggestion` from free text to a small structured `RemediationHint` (e.g. `SkipStep(index)`, `InsertGoal(GoalRequest)`, `RetryWithBackoff`, `AbandonAndReportToChat`) that `PlanAsync` can mechanically apply. Free text is fine as a *human-readable* accompaniment, but it shouldn't be the only output of the one place in the system that's allowed to decide "this isn't working."

### 2.4 The intent vocabulary is a closed set, not a tool surface
`IntentManager.BuildGoalRequest` is a `switch` over six hardcoded intent strings (`gather`, `place`, `craft`, `build`, `navigate`, `smelt`), each mapped to a specific `GoalRequest` subclass. `ParseCommandString` (used for `TaskSequenceGoal`'s `NextSteps` chaining) re-implements the same six intents again as hand-written regexes. Adding a genuinely new ad hoc action — say, "attack the nearest hostile mob" or "trade with a villager" — means: a new `GoalRequest` subclass, a new `switch` case, a new regex pattern (so it also works inside sequences), a new decomposer, and a new entry wherever the LLM's system prompt enumerates intents. This is consistent with what your memory notes already flagged about tool names not reaching the LLM — it's not a documentation gap, it's that the intent layer's vocabulary is fixed at compile time, which is structurally opposed to "a wide variety of actions, including ad hoc ones, with a very rich toolset."

This is the deepest of the four findings and the one I'd treat as a multi-sprint design project rather than a fix — but it's the one that actually determines whether "ad hoc" actions are possible at all, as opposed to just "more pre-anticipated goal types."

### 2.5 Net new cluster for the roadmap
The existing council doc's 7 clusters (ABS god class, inventory SSOT, creative policy, recovery, Facts duality, security, test debt) are about **paying down debt in the current reactive, single-goal architecture**. None of them, even fully resolved, would give you proactive multi-step planning with real replanning — because the planning *model* itself (fixed-length sequences, free-text remediation, closed intent vocabulary) is the limiting factor, not the cleanliness of `AgentBackgroundService`. I'd suggest treating this as an explicit **C8: Planning & Intent Model** cluster, sequenced *after* the Sprint 57-58 debt work the council already scoped (extracting recovery and inventory SSOT first makes C8 much easier to build on top of — a `RecoveryManager` that already outputs structured policy decisions is most of the way to the `RemediationHint` model in §2.3), but before further goal-type sprawl on the current architecture, since every new goal type added now is more surface area to migrate later.

---

## Open Questions (yours to resolve, not mine to assume)

1. Is `TaskSequenceGoal`'s 5-step cap a deliberate safety/scope guard, or just an initial conservative default? If the former, the planning-model work in §2 should respect some cap; if the latter, it's worth lifting once §2.2/§2.3 land, since a richer plan model with no execution depth is moot.
2. Do you want replanning to stay LLM-evaluator-driven (current architecture), or do you want a deterministic planner (HTN) in the loop for replanning too, with the LLM evaluator as one input among several? This materially changes the shape of §2.3's `RemediationHint` — a pure-LLM loop can tolerate looser structure than one where a deterministic planner has to consume it.
3. Should `IntentManager`'s vocabulary expansion (§2.4) be tackled by generalizing the existing typed `GoalRequest` hierarchy, or by moving toward a function-calling-style tool schema the LLM selects from directly (closer to how `LlmEvaluatorImpl` already does structured JSON I/O)? The latter is a bigger lift but is the more direct fix for "ad hoc" actions specifically.
