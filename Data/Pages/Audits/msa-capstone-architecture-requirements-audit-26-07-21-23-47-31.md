# MemorySmith.Agent — Delta Audit #7 (Capstone): All Four Explicit Architecture Hard Requirements Are Unmet

**Repo/commit:** `TheMasonX/MemorySmith.Agent` @ `0f27af1befb72e7534421a1bd5550aee1e077d96` (`dev/round-3`) — same commit as all prior reports.
**This report is different in kind from Reports #1–#6.** Those found individual instances of a recurring pattern. This one shows the pattern is not incidental: `Data/Pages/user-requirements.md` contains four explicit, human-written "architecture hard requirements" — and every finding across this six-report series maps cleanly onto one of exactly those four, unmet, at this exact commit. This report is the synthesis, with one new piece of direct evidence (`ExecutionContext` is never referenced by `AgentBackgroundService.cs`, despite being built specifically to replace its internals) that ties the whole series together.

---

## The four hard requirements, and the evidence against each

`Data/Pages/user-requirements.md`, "Architecture hard requirements" section, quoted in full:

> - ExecutionContext is the canonical runtime state object. It should carry the state that flows through planning, dispatch, evaluation, and replanning instead of relying on loose arguments and repeated state derivation.
> - Removal is preferred over deprecation and fallback. The supported architecture should be the modern typed pipeline only; legacy compatibility shims should be removed rather than preserved.
> - Planning and replanning should rely on explicit preconditions, postconditions, and structured remediation policies rather than free-text fallbacks.
> - Fresh world-state and inventory truth should be treated as prerequisites before plan generation.
> - AgentBackgroundService should evolve into an orchestration layer rather than remain the primary owner of runtime policy and side effects.

*(Five bullets in the source; the summary table below groups the first and last together since they describe the same migration from opposite ends.)*

| Requirement | Status at HEAD | Evidence |
|---|---|---|
| **"AgentBackgroundService should evolve into an orchestration layer... ExecutionContext... instead of loose arguments and repeated state derivation"** | **Unmet.** | Report #1, F1: `AgentBackgroundService.cs` is 4,019 lines (grown every commit since being marked "decomposed" under TSK-0292), with a 24-parameter constructor and ~55 methods spanning chat parsing, dispatch, recovery, replanning, and dashboard push. **New this report**: `grep -c "ExecutionContext" WebUI.Blazor/AgentBackgroundService.cs` → **0**. The type built specifically to replace this file's "loose arguments and repeated state derivation" (its own doc comment's words) is never referenced by the file it was built to replace. |
| **"Removal is preferred over deprecation and fallback... legacy shims should be removed"** | **Unmet — the umbrella task for this is still `Ready`, unstarted.** | Report #1, F2: `AgentRuntime` + 5 `*ManagerImpl` classes, registered in DI since Sprint 39/40, zero consumers, doc-commented as a "Sprint 40 target" 20 sprints overdue. `TSK-0293` ("Remove legacy fallback and shim paths in planning and runtime policy") — the task that exists specifically to do this — is still `Ready`/unstarted. |
| **"Planning and replanning should rely on explicit preconditions, postconditions, and structured remediation policies rather than free-text fallbacks"** | **Unmet — implemented in isolation, unreachable in production.** | Report #6, J1: `IGoalPrecondition.CanAttempt()` is correctly implemented on 3 goal classes (TSK-0310, `Done`) — but its only caller lives in the dead `PlanningManagerImpl` from the row above, so it never runs. `IGoalPostcondition` has zero implementations anywhere. `IRemediationPolicy`/`RemediationPolicies` has zero consumers anywhere. The council's own 2026-07-11 audit independently rediscovered the symptom (`MSA-PLAN-006`) without, as far as the record shows, a follow-up task ever being filed. |
| **"Fresh world-state and inventory truth should be treated as prerequisites before plan generation"** | **Partially unmet, same root cause.** | `ExecutionContext.HasFreshInventory` (`=> !State.IsInventoryStale`) exists specifically to support this requirement, and is read by `IGoalPrecondition.CanAttempt()` implementations (e.g., `GenericGatherGoal`'s precondition checks `context.HasFreshInventory`) — but since `ExecutionContext` and the precondition-checking call site are both confirmed dead (rows above), this prerequisite-checking is, like the row above, real code that never executes. |

**Every one of the five stated hard requirements is unmet at this exact commit, for the same underlying reason in each case:** the canonical type/interface was correctly designed and built (often the same day or week, and always marked `Done`), the "legacy" code it was meant to replace was correctly identified and named, and the one step that never happened, for any of the four, is the actual migration of `AgentBackgroundService` to construct/consume the new types and stop owning the responsibilities the new architecture was built to take over.

---

## Why this matters more than the sum of six reports' individual findings

Reports #1–#6 each found a piece of this independently — a dead scaffolding class here, an unreachable precondition check there, a stale doc comment somewhere else. Taken individually, each looked like a discrete, fixable oversight. Read together against the explicit requirements doc, they resolve into one story:

**This project has correctly designed its target architecture, built the new types, and even documented the requirement that the old code be removed rather than kept — and then never taken the single remaining step of actually migrating `AgentBackgroundService` onto that architecture.** Every task that touched this area (TSK-0289, TSK-0290, TSK-0292, TSK-0295, TSK-0309, TSK-0310, and historically TSK-0126) was scoped narrowly enough — "introduce the type," "document the requirement," "implement the interface on 3 classes," "decompose the service" — that each could be honestly marked `Done` on its own narrow terms, while the thing that would make any of them *matter* (wiring `AgentBackgroundService` itself to use them) was never itself scoped as a task at all. It isn't sitting in Backlog waiting to be picked up; it doesn't exist as a ticket. That's a more specific and more actionable diagnosis than "there's some dead code here" — it identifies the exact missing task.

---

## Recommendation: one task, not six

Rather than continuing to file (or re-file) narrow tickets for each symptom — `TSK-0293` for the legacy shims, a new ticket for wiring `IGoalPrecondition`'s call site, another for `Reconcile` (Report #1, F3), another for the Manager classes — this report's single recommendation is to **file one integration-migration task that supersedes and closes several existing threads at once**:

> **"Migrate `AgentBackgroundService` to construct and thread `ExecutionContext` through its planning/dispatch/recovery flow, and delegate to `IPlanningManager`/`IExecutionManager`/`IRecoveryManager`/`IStateManager`/`IDashboardPublisher` instead of its own inline logic."**

This single piece of work, if scoped and done incrementally (per Report #1's F1 recommendation to migrate one vertical slice at a time, starting with the lowest-risk slice — dashboard push), would mechanically:
- Resolve Report #1's F1 (the god-class shrinks because responsibilities move to the Manager classes it delegates to).
- Resolve Report #1's F2 (the Managers stop being dead code the moment ABS actually constructs and calls them).
- Resolve Report #6's J1 (the precondition check starts running the moment `PlanningManagerImpl` is actually invoked).
- Directly advance `TSK-0293`'s legacy-removal mandate (the "legacy" *is* ABS's inline logic; removing it *is* this migration).
- Make `IGoalPostcondition`/`IRemediationPolicy` either genuinely useful (if `PlanningManagerImpl`/`RecoveryManagerImpl` start being real callers) or clearly and finally identifiable as speculative (if, once wired, they still aren't needed — at which point deleting them is a confident, evidence-based call rather than a guess).

This doesn't mean the six individual reports were wasted effort — each is still independently useful as a detailed, cited breakdown of one facet of the same problem, and each remains valid if the team prefers to tackle things piecemeal. But if there's appetite for a single highest-leverage next step across this entire audit series, this is it: **one migration task, explicitly linked to the five hard requirements it's meant to satisfy, rather than six independent tickets that each individually look smaller and lower-priority than the whole actually is.**

---

## Confidence and caveats

- **95%** confidence that all five hard requirements are genuinely unmet as stated — every underlying claim (`ExecutionContext` reference count, `*ManagerImpl` consumer counts, `IGoalPrecondition` call sites, `TSK-0293`'s status) is independently, directly verified via grep/read in this report or a prior one in this series, not inferred.
- **85%** confidence in the "one task instead of six" recommendation being the right sequencing call — this is a project-management judgment (concentrate vs. distribute the work) rather than a code-correctness claim, and reasonable teams could prefer the incremental-tickets approach for review-size or risk-isolation reasons even while agreeing with the underlying diagnosis.
- **Open question, stated plainly:** this report doesn't know why the migration step was never scoped as its own task across three-plus sprints of otherwise-diligent work (the task records for the surrounding work are detailed and evidence-based, which makes the gap more notable, not less). Possible explanations — sprint-boundary pressure, the migration being perceived as "too large" to fit a single ticket, or simply an oversight amid a large backlog — aren't distinguishable from the available records, and any of them would suggest a different practical fix (e.g., explicit sizing/breakdown guidance for large migrations) beyond just filing the ticket.
