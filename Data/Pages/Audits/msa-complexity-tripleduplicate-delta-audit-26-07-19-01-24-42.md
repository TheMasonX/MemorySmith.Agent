# MemorySmith.Agent — Delta Audit #4: Complexity-Ranked Deep Read + Closing Out Prior Open Items

**Repo/commit:** `TheMasonX/MemorySmith.Agent` @ `0f27af1befb72e7534421a1bd5550aee1e077d96` (`dev/round-3`).
**HEAD re-verified this pass**: `git fetch origin dev/round-3` confirms the branch is still at the same commit as all three prior reports — no drift.
**Relationship to prior reports:** delta-only; findings numbered continuing from Reports #1–3 (F1–F9, D1–D11, E1–E4 → this report is **G1–G5**).
**Methodology this pass:** ran `lizard` (cyclomatic-complexity ranking) across all production C# to objectively identify the riskiest methods rather than picking files impressionistically; read `AgentBackgroundService.DispatchActionsAsync` — CCN 111, the single highest-complexity method in the entire codebase by a wide margin — in full; re-ran `jscpd` against the full C# tree to specifically close out the four unverified clone pairs flagged as "not hand-verified" in Report #2's finding D11.

---

## Executive Summary

| # | Finding | Class | Confidence | Impact |
|---|---|---|---|---|
| G1 | **`IsProgressSignalTool` excludes `MoveTo`, `Wander`, `GetStatus`/`Status`, `FindFlatArea`, and `FindReachableBlock`**, even though all five are also classified `IsFireAndForgetTool`. Only `MineBlock`/`place`/`CraftItem`/`SmeltItem` successes reset `_consecutiveFailures`. A goal dominated by movement/discovery actions (e.g. a pure navigate command, or a build's pathing-heavy phase) can accumulate consecutive failures toward goal abandonment from transient pathing hiccups even while making real, if slower, progress — because a subsequent *successful* `MoveTo` never resets the counter the way a successful `MineBlock` would. | Asymmetric guard / possible goal-abandonment bug | 75% | Medium — plausible, not empirically reproduced (see Open Questions) |
| G2 | **Team-authored comment directly corroborates a prior finding.** `AgentBackgroundService.cs:2327–2334` explicitly documents, in the team's own words, that an LLM-evaluator parse failure, provider error, or null response is *"indistinguishable at this call site from a genuine 'continue' verdict"* — this is precisely Report #1's F5 / Report #2's D3&D9 (silent-swallow / weaker-JSON-extraction) finding, now confirmed as a gap the team itself already recognized in a comment but has not yet closed. Strengthens confidence in F5/D3/D9 rather than standing as a new independent finding. | Corroboration of existing finding | 95% (direct quote) | (rolls into F5/D3/D9's existing impact rating) |
| G3 | **Three lookup dictionaries (`PlankToLogMap`, `IronIngotRequirements`, `CobblestoneRequirements`) are verbatim-duplicated between the canonical `Agent.Core/CommonMinecraftBlocks.cs` and private copies inside `Agent.Planning/HtnTaskLibrary.cs`** — confirmed via `jscpd` and hand comparison (exact key/value matches). The damning part: **the same file already does this correctly for a fourth dictionary** — `HtnTaskLibrary.DirectMineBlocks` is a one-line delegation, `=> CommonMinecraftBlocks.DirectMineBlocks`, with a doc comment literally reading *"single source of truth."* The team has already established and documented the right pattern in this exact file; three other maps just didn't get the same treatment. | Duplicated Code (exact) / inconsistent application of an established pattern | 95% | Medium — real drift risk (a future game-version crafting-cost change needs edits in two places; easy to miss the private copy) |
| G4 | **Closing out Report #2's D11**: the `WorldStateProjector.cs` self-clone (279–287 vs 398–406) hand-verified this pass — **not a real issue**. It's two different domain-event branches (`SpawnEvent`, `StatusEvent`) in the same `switch` that happen to set the identical three-fact triple (`Pos`/`Health`/`Food`), which is expected/correct given both events carry the same payload shape. No action needed. | Verified false-positive | 90% | None (closes the item) |
| G5 | **Closing out Report #2's D11**: `GoalFactory.cs`'s self-clone (217–226 vs 229–238) — re-confirmed as a defensible nullable/non-nullable `int` overload pair, not a problematic duplication. No action needed. | Verified false-positive | 90% | None (closes the item) |

---

## Detailed Findings

### G1 — Asymmetric `IsProgressSignalTool` vs. `IsFireAndForgetTool` classification

**Evidence** (`WebUI.Blazor/AgentBackgroundService.cs`):
```csharp
// line 2967
private static bool IsFireAndForgetTool(string toolName) =>
    toolName.Equals("MoveTo", ...) || toolName.Equals("MineBlock", ...) || toolName == "place"
    || toolName.Equals("GetStatus", ...) || toolName.Equals("Status", ...)
    || toolName.Equals("Wander", ...) || toolName.Equals("CraftItem", ...)
    || toolName.Equals("SmeltItem", ...) || toolName.Equals("FindFlatArea", ...)
    || toolName.Equals("FindReachableBlock", ...);

// line 3776
private static bool IsProgressSignalTool(string toolName) =>
    toolName.Equals("MineBlock", ...) || toolName == "place"
    || toolName.Equals("CraftItem", ...) || toolName.Equals("SmeltItem", ...);
```
And the consumer (line 2404–2410, inside the `result.Success` branch of the dispatch loop):
```csharp
if (IsProgressSignalTool(action.Tool))
{
    _consecutiveFailures = 0;
    _lastFailureReason = null;
    // Sprint 20: RecordProgress() moved to cycle-settle below.
    // Per-tool calls caused 0ms fire-and-forget tools to mask stagnation.
}
```
`MoveTo`, `Wander`, `GetStatus`/`Status`, `FindFlatArea`, and `FindReachableBlock` are all fire-and-forget (their success is reported async, later, via an event) but **none of them reset `_consecutiveFailures` on success** — only `MineBlock`, `place`, `CraftItem`, `SmeltItem` do. The `IsProgressSignalTool` set reads like "resource-producing actions only," which is a defensible design intent (movement/discovery isn't "progress toward the goal" in the same sense as producing an item) — but it means a goal whose action stream is dominated by movement (a pure `navigate` command, or the pathing-heavy lead-up to a build/gather goal) can rack up consecutive failures from transient, recoverable `MoveTo` failures (a momentarily blocked path, a timing hiccup) without any intervening success ever resetting the counter, right up to `maxConsecutiveFailures` and goal abandonment — even while the bot is net making progress toward its destination.

**Why this is plausible and not just theoretical:** `HasFailed`/`IsComplete` checks (upstream in the same method, ~line 1929) already treat `_consecutiveFailures >= maxFailures` as an abandonment trigger independent of whether the goal is *actually* stuck — this is exactly the mechanism the comment at line 2408–2410 references having been partially reworked before ("Sprint 20: RecordProgress() moved to cycle-settle below... per-tool calls caused 0ms fire-and-forget tools to mask stagnation"), meaning the team has already iterated on this exact tension once and may not have revisited it since `FindReachableBlock`/`FindFlatArea` were added to the fire-and-forget set.

**Recommendation.** Confirm with the team whether excluding `MoveTo`/`Wander`/`GetStatus`/`FindFlatArea`/`FindReachableBlock` from progress-signal status is deliberate. If not, add them to `IsProgressSignalTool` (or introduce a narrower `ResetsFailureCounterOnSuccess` predicate distinct from "counts as goal progress" if the two concepts need to diverge) so a run of transient movement/discovery failures doesn't compound toward abandonment purely because no resource-producing action happened to interleave.

**Confidence: 75%.** The asymmetry itself is directly confirmed by reading both predicates; the "this causes premature goal abandonment for movement-heavy goals" severity claim is a traced, plausible inference from the code's own control flow, not something reproduced against a running agent — see Open Questions.

---

### G2 — Team's own comment corroborates the F5/D3/D9 evaluator-ambiguity finding

**Evidence** (`WebUI.Blazor/AgentBackgroundService.cs:2327–2334`):
```csharp
// Sprint 57 Wave D / Sprint 58 Wave C (TSK-0325): track consecutive
// LLM evaluator non-success results with circuit-breaker cooldown.
// A parse failure, provider error, or null response returns
// IsSuccess=false, ShouldReplan=false — indistinguishable
// at this call site from a genuine "continue" verdict.
// After 3 consecutive failures, suppress evaluator calls for
// LlmEvalCooldown (5 min) to break the infinite fail-loop.
```
This is the team explicitly writing down, in their own comment, the exact ambiguity Report #1's F5 and Report #2's D3/D9 identified from the callee side (`LlmEvaluatorImpl.ParseEvaluationDirective`'s silent catch-to-`Continue` fallback with no logger in scope). The circuit-breaker built here (TSK-0325, 3-strikes cooldown) is a real, useful mitigation for the *consequence* (an infinite fail-loop), but it operates on the aggregate failure count — it doesn't address the underlying observability gap this comment names: a parse failure and a genuine "keep going" verdict remain indistinguishable in the logs at the point they happen, which is exactly what F5's recommended fix (thread a logger into the parser, log the specific parse-failure reason) would resolve.

**No new action beyond what F5/D3/D9 already recommend** — reporting this because independent corroboration in the team's own words is strong evidence the underlying gap is real and already on someone's radar, which should raise its priority rather than needing further debate about whether it matters.

**Confidence: 95%.** Direct quote from the source; the "this corroborates F5" linkage is a straightforward textual match, not an inference.

---

### G3 — Three lookup dictionaries duplicated verbatim, despite the same file already using the correct shared-source pattern for a fourth

**Evidence.**
```
$ jscpd (C# tree) →
Agent.Core/CommonMinecraftBlocks.cs 156-167 <-> Agent.Planning/HtnTaskLibrary.cs 68-79    (PlankToLogMap, 8 entries)
Agent.Core/CommonMinecraftBlocks.cs 180-192 <-> Agent.Planning/HtnTaskLibrary.cs 139-151   (IronIngotRequirements, 9 entries)
Agent.Core/CommonMinecraftBlocks.cs 195-203 <-> Agent.Planning/HtnTaskLibrary.cs 157-165   (CobblestoneRequirements, 5 entries)
```
Hand-verified: all three are character-for-character identical key/value pairs between the two files (confirmed by direct diff of the dictionary bodies). `HtnTaskLibrary.cs` already has `using Agent.Core;` and already correctly delegates a fourth, analogous dictionary:
```csharp
// Agent.Planning/HtnTaskLibrary.cs:108-111
/// Delegates to <see cref="CommonMinecraftBlocks.DirectMineBlocks"/> — single source of truth.
private static HashSet<string> DirectMineBlocks => CommonMinecraftBlocks.DirectMineBlocks;
```
This is a clean, direct instance of the pattern this audit series has flagged before (Report #1's "guard exists for concern A but not sibling concern B, same team" heuristic): the team demonstrably knows the right answer — one-line delegation to the `Agent.Core` canonical source, explicitly labeled "single source of truth" — and applied it once, in this exact file, but not to three sibling dictionaries that are exposed as `public static readonly` in `CommonMinecraftBlocks.cs` for precisely this purpose.

**Why this matters beyond DRY-nitpicking:** these are Minecraft game-data tables (crafting costs, wood-type mappings) that change when the game version the project targets changes. Today, updating one (say, a new wood type, or a game-balance change to iron tool costs) requires remembering to edit both files — and `CommonMinecraftBlocks.cs`'s public visibility strongly signals it was *designed* to be the single edit point, making the private copies in `HtnTaskLibrary.cs` an easy trap for a future contributor who edits the canonical file and reasonably assumes that's sufficient.

**Recommendation.** Delete the three private dictionary declarations in `HtnTaskLibrary.cs` (lines ~68, ~139, ~157) and replace their three usages (`IronIngotRequirements.TryGetValue` at line 215, `CobblestoneRequirements.TryGetValue` at line 249, `PlankToLogMap.TryGetValue` at line 263) with `CommonMinecraftBlocks.IronIngotRequirements`/etc. — exactly mirroring the existing `DirectMineBlocks` pattern three lines away in the same class. This is a ~10-minute, zero-risk mechanical fix (the values are already provably identical, so behavior can't change) with a template already present in the same file to copy.

**Confidence: 95%.** jscpd-flagged, hand-diffed as exact matches, and the "team already knows the right pattern" claim is a direct quote/pattern-match against code in the same file, not speculation.

---

## Assumptions & Open Questions

1. **G1's severity is reasoned from control flow, not reproduced.** No running agent was available to actually generate a string of transient `MoveTo` failures interleaved with successes and observe whether goal abandonment fires prematurely. The finding rests on a direct reading of `IsProgressSignalTool`/`IsFireAndForgetTool`'s definitions and their single consumer — solid on "the asymmetry exists," more of a reasoned inference on "this causes real premature abandonments in practice," hence 75% rather than 90%+. Confirming this would need either a unit/integration test constructing exactly this sequence, or production log analysis for goals whose `FailureReason` is `ConsecutiveFailures` cross-referenced against whether `MoveTo` successes occurred in the same window.
2. **G3's fix assumes no behavioral divergence was intentional.** It's conceivable (though the exact key/value match makes it unlikely) that `HtnTaskLibrary`'s private copies were meant to be independently tunable from the canonical `CommonMinecraftBlocks` values for some in-progress reason. Worth a quick confirmation before deleting, though the evidence (identical values, no differentiating comment, and the file's own `DirectMineBlocks` precedent) strongly favors "oversight" over "intentional fork."
3. **This pass covered only the single highest-complexity method in full** (`DispatchActionsAsync`, CCN 111) plus targeted verification of Report #2's D11 backlog. The next three methods by complexity — `HandleChatEventAsync` (CCN 88), `ProcessEventsAsync` (CCN 75), and `HtnTaskLibrary.DecomposeBuild` (CCN 72) — were not read in full this pass and remain good candidates for the next slice, per this engagement's own audit-skill's "read the highest-complexity methods in full" guidance.
4. **`WebSocketBridge.ParseEvent`** (CCN 34, the highest-complexity method outside `WebUI.Blazor`/`Agent.Planning`) was not read this pass either — flagged for a future slice given it sits directly on the JS↔C# trust boundary this audit series has otherwise focused on.

---

## Suggested Sequencing (additive)

1. G3 (delete 3 duplicate dictionaries, delegate to `CommonMinecraftBlocks`) — trivial, zero-risk, do anytime.
2. G1 — needs the team confirmation from Open Questions #1 before changing behavior; if confirmed as a real gap, it's a one-line fix (add the 5 tool names to `IsProgressSignalTool` or split the concept as described).
3. G2 — no new action; treat as added weight behind already-recommended F5/D3/D9 fix, which should now be considered higher-priority than "nice to have" given the team's own comment already flags it as a known ambiguity.
4. Next slice: `HandleChatEventAsync`, `ProcessEventsAsync`, `HtnTaskLibrary.DecomposeBuild`, and `WebSocketBridge.ParseEvent` — the four next-highest-complexity methods, unread in full as of this report.
