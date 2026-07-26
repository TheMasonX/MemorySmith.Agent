# MemorySmith.Agent — Delta Audit #6: The "Structured Planning Policy" Model Is Functionally Dead

**Repo/commit:** `TheMasonX/MemorySmith.Agent` @ `0f27af1befb72e7534421a1bd5550aee1e077d96` (`dev/round-3`) — same commit as all prior reports.
**This is a delta report.** Per the methodology correction in Delta #5, findings below were checked against both `Data/Tasks/*.json` and the `Data/Pages/Audits/` corpus (91 documents) before being written up. This round's headline finding **extends and root-causes** an already-known, still-open council finding (`MSA-PLAN-006`) rather than presenting it as newly discovered — the value-add here is the precise mechanism, not the existence of the problem.
**This pass covered:** `Agent.Core/Models/*` (all files), `Agent.Planning/Decomposition/*` (the smaller decomposers not yet read: `GatherGoalDecomposer`, `PlaceBlockGoalDecomposer`, `TaskSequenceGoalDecomposer`, `CraftItemGoalDecomposer`, `SmeltGoalDecomposer`, `SurviveNightGoalDecomposer`, `DecomposerRegistry`).

---

## Executive Summary

| # | Finding | Class | Confidence | Impact | Status |
|---|---|---|---|---|---|
| J1 | **The entire Sprint 57 "structured planning policy" model (`IGoalPrecondition`, `IGoalPostcondition`, `IRemediationPolicy`) is functionally inert in production, and the precise mechanism explains a finding the July-11 council already flagged but never root-caused.** `IGoalPrecondition.CanAttempt()` is correctly implemented on `SmeltGoal`, `CraftItemGoal`, and `GenericGatherGoal` (real work, done under TSK-0310) — but its **only caller anywhere in the codebase** is `WebUI.Blazor/Managers/PlanningManagerImpl.cs`, which is one of the five dead `*ManagerImpl` classes already confirmed unwired in Report #1 (F2). Since nothing ever resolves `IPlanningManager`, `CanAttempt()` is never invoked by the live pipeline (`HtnPlanner`, `AgentBackgroundService`, `GoalFactory` — all directly grepped, zero references). `IGoalPostcondition` has **zero implementations anywhere**. `IRemediationPolicy` / `RemediationPolicies` (3 named default policies) has **zero consumers anywhere**. This is the concrete "why" behind `MSA-PLAN-006` ("IGoalPrecondition not enforced in planning pipeline," council-confirmed P2 on 2026-07-11) — a finding the council flagged as a symptom without (per the available record) tracing it to this specific root cause, and which still has no dedicated remediation task today. | Dead code / Speculative Generality, with a demonstrated correctness consequence | 93% | Medium-High — this isn't cosmetic dead code; it means a "Done" precondition-safety feature never actually runs |

**Why this is worth its own report rather than a one-line addendum:** this pass ties together three previously-separate threads from this audit series into one coherent, falsifiable narrative:
1. Report #1 (F2) established that `AgentRuntime`/`*ManagerImpl` are registered-but-unconsumed.
2. Report #4 (G4) established that this exact *pattern* (dead orchestration scaffolding, marked "Done") has recurred at least twice in this project's history.
3. This report shows a **specific, named feature** (goal precondition checking) that was implemented correctly at the unit level (TSK-0310) but is completely unreachable because its call site lives inside that dead scaffolding — and that the council's own July-11 audit already noticed the *symptom* (`MSA-PLAN-006`) without, as far as the available documents show, connecting it to *this* cause or filing a task to fix it.

---

## Detailed Findings

### J1 — `IGoalPrecondition`/`IGoalPostcondition`/`IRemediationPolicy`: implemented in isolation, unreachable in production

**The timeline, reconstructed from task records:**
1. **2026-07-01, TSK-0290** ("Add structured planning and replanning policy objects for goal preconditions and remediation") — `Done`. Introduces `IGoalPrecondition`, `IGoalPostcondition`, `IRemediationPolicy`/`RemediationStep`/`RemediationPolicies` in `Agent.Core/Models/PlanningPolicy.cs`.
2. **2026-07-01, TSK-0310** ("P2: Implement IGoalPrecondition on gather/craft/smelt goals") — `Done`, same day. Adds `CanAttempt(ExecutionContext, out string?)` implementations to `GenericGatherGoal`, `CraftItemGoal`, `SmeltGoal`.
3. **2026-07-11 (10 days later), council audit** (`synthesizer-verdict-internal-audit-60-20260711.md`) independently confirms **`MSA-PLAN-006` — "IGoalPrecondition not enforced in planning pipeline" — P2** — i.e., despite TSK-0310 being marked `Done` well before this audit ran, the council found the feature isn't actually active.
4. **At HEAD (same commit as the council audit)** — no task exists that addresses `MSA-PLAN-006` specifically (`grep -i precondition` across all task titles/descriptions returns only TSK-0290 and TSK-0310, both already-closed and both upstream of the actual gap).

**Root cause, confirmed this pass:**
```
$ grep -rln "IGoalPrecondition\|CanAttempt(" --include=*.cs | grep -v Tests
Agent.Core/Models/PlanningPolicy.cs                    # interface declaration
Agent.Planning/Goals/SmeltGoal.cs                      # implementation (TSK-0310)
Agent.Planning/Goals/CraftItemGoal.cs                  # implementation (TSK-0310)
Agent.Planning/Goals/GenericGatherGoal.cs              # implementation (TSK-0310)
WebUI.Blazor/Managers/PlanningManagerImpl.cs           # ← the only call site, anywhere
```
`PlanningManagerImpl.cs:69-81` (the actual check, correctly written):
```csharp
// Check goal preconditions when the goal implements IGoalPrecondition.
if (goal is IGoalPrecondition precondition)
{
    if (!precondition.CanAttempt(context, out var blockingReason))
    {
        _logger.LogWarning("[planning] precondition failed for {Goal}: {Reason}", goal.Name, blockingReason);
        return new ActionPlan(goal.Name, goal.Phases, []);
    }
}
```
This is genuinely correct, sensible code. The problem is entirely that `PlanningManagerImpl` — confirmed in Report #1 (F2) — is never resolved by anything: `AgentBackgroundService`'s constructor doesn't take an `IPlanningManager`, and nothing else in the solution does either. Confirmed again this pass with a direct check of the actual live planning call sites:
```
$ grep -rn "CanAttempt\|IGoalPrecondition" Agent.Planning/HtnPlanner.cs WebUI.Blazor/AgentBackgroundService.cs Agent.Planning/GoalFactory.cs
(no results)
```
So: 3 goal classes correctly answer "can I be attempted right now?" — and the only code that ever asks them is a class nobody constructs. The feature exists, is unit-testable in isolation, and does precisely nothing at runtime.

The same investigation confirms `IGoalPostcondition` (`ExpectedOutcome`, `ExpectedInventoryDelta`) has **zero implementations** anywhere in the codebase — not even a partial one — and `IRemediationPolicy`/`RemediationPolicies.RetryThenAbandon`/`.WanderThenRetry`/`.RefreshThenRetry` have **zero consumers** anywhere. These two aren't "implemented but unreachable" like the precondition case — they're simply unused from day one. `AgentBackgroundService`'s actual recovery logic (confirmed in Report #1's discussion of the god-class's responsibilities) uses its own separate, ad-hoc retry/backoff handling rather than these typed policy objects — meaning TSK-0290's stated goal ("so the planner can make deterministic decisions rather than relying on free-text suggestions or implicit assumptions") has not been achieved for 2 of its 3 constructs, and only partially (implemented-but-unreachable) for the third.

**Recommendation.**
1. **Immediate, low-risk fix for the precondition gap specifically**: since the check logic in `PlanningManagerImpl` is already correct, the fix is to move (not rewrite) that `if (goal is IGoalPrecondition precondition) { ... }` block into wherever `AgentBackgroundService` (or `HtnPlanner`/`GoalFactory`) actually creates an `ActionPlan` from a goal today — this is a genuinely small, mechanical change once the right insertion point is identified, since the logic itself doesn't need to change.
2. **File a task explicitly linking `MSA-PLAN-006` to this root cause** (rather than leaving it as an orphaned council finding with no owner) — this closes the loop the council audit opened on 2026-07-11 and gives the next implementer the "why" for free instead of re-discovering it.
3. **For `IGoalPostcondition`/`IRemediationPolicy`**: given zero implementations/consumers exist 3 weeks after introduction, apply the same "delete rather than finish" calculus used for `AgentRuntime`/Managers in Report #1 (F2) unless there's active near-term intent to build on them — an unused interface with no implementers is lower-cost to delete and reintroduce later than to carry forward indefinitely as aspirational surface area.
4. This is a good candidate to fold into whichever task ends up resolving Report #1's F2 (wire-or-delete the Managers layer) — since `PlanningManagerImpl` is both the F2 dead-scaffolding instance *and* the load-bearing (if unreachable) home of this precondition check, fixing one naturally forces a decision on the other.

**Confidence: 93%.** Every factual claim in this finding (implementation locations, zero-consumer counts, the TSK-0290/0310/`MSA-PLAN-006` timeline) is directly grepped/read, not inferred. The 93% (rather than higher) reflects that "the council didn't connect this to the root cause" is an inference from the available document text (no explicit statement either way in the synthesizer verdict about *why* `MSA-PLAN-006` occurs) rather than a directly confirmed fact about the council's internal reasoning.

---

## Assumptions & Open Questions

1. **Whether the council's `MSA-PLAN-006` finding already identified this exact root cause internally** (and simply didn't record it in the synthesized verdict document) can't be ruled out from the available text — the synthesizer verdict is a condensed summary, not necessarily the full reasoning trail of each of the 10 agents. If a more detailed per-agent finding document exists elsewhere in the 91-document corpus with this root cause already spelled out, this report's contribution shrinks from "root-causing an open finding" to "re-confirming an already-root-caused one" — worth a quick grep by whoever picks this up, the same caveat applied throughout Delta #5.
2. **The "move the check, don't rewrite it" recommendation assumes** `ExecutionContext` (the parameter `CanAttempt` takes) is already constructible at the point in `AgentBackgroundService`/`HtnPlanner` where plans get created — this wasn't independently verified this pass (would require tracing `ExecutionContext`'s construction sites), so treat the "small, mechanical" sizing as provisional pending that check.
