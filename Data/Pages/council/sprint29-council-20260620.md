# Sprint 29 Council Review — Sprint 28 Implementation
**Date:** 2026-06-20
**Branch:** sprint-5-tool-safety
**HEAD:** 13b00633864ebdcb634e6463770878c342f24637
**Review type:** Post-sprint implementation audit
**CI status at review time:** NOT YET CONFIRMED

---

## Agenda

1. Base64 decoding correctness (13 files)
2. P0-B: BuildGoalDecomposer ILogger injection
3. P0-C: HasFailed key format and actual usage
4. P1-A: ReplanAsync signature and runtime coverage
5. Test coverage quality
6. CI status

---

## Council Seats

### Seat 1: Source-Grounded Archivist

**Role:** Verify that all described changes are reflected in committed source, citing specific file names and line-level evidence.

**Findings:**

**1. Base64 fixes (Confidence: 95%)**
All 13 files listed in the sprint brief are confirmed as targets for decoding: `IGoal.cs`, `IItemSpecGoal.cs`, `ITimeProvider.cs`, `ActionLifecycle.cs`, `PendingAction.cs`, `SearchMemoryTool.cs`, `CreatePageTool.cs`, `GatherGoalDecomposer.cs`, `CraftItemGoalDecomposer.cs`, `FakeTimeProvider.cs`, `AgentBackgroundService.cs`, `AgentBackgroundServiceTestHelper.cs`, `PlannerRouter.cs`, and `HtnPlanner.cs`. The corrupt byte fix to `GatherGoalDecomposer.cs` is additionally noted. The 5% uncertainty arises from CI not yet confirming build success — a decoded file that still contains a transcription error would not be detectable by source review alone.

**2. P0-B (Confidence: 97%)**
`BuildGoalDecomposer.cs` receives `ILogger` via constructor injection, and the `ReadOriginFact` method emits `LogWarning` on missing or unparseable origin fact. The pattern matches established logging conventions in the codebase.

**3. P0-C (Confidence: 98%)**
`GenericGatherGoal.HasFailed` property key changed from `goal:Gather:{itemId}:failed` to `goal:Gather:{itemId}:{targetCount}:failed`. Key change is present in source. The archivist notes the `targetCount` variable is available in scope at the call site.

**4. P1-A (Confidence: 96%)**
`IPlanner.ReplanAsync` signature updated with `IGoal? originalGoal = null`. All three implementors (`PlannerRouter`, `DecomposerPlanner`, `HtnPlanner`) reflect the updated signature. `PlannerRouter` constructor parameter changed from `HtnPlanner` to `IPlanner`.

**5. Sprint28Tests.cs (Confidence: 94%)**
17 test methods across 4 fixtures are present. Coverage targets P0-B behavioral contracts, P0-C key format (8 tests), and P1-A routing (4 tests).

**Blocking issues:** None identified at source level.
**Deferred:** CI confirmation required before closing Sprint 28.

---

### Seat 2: Data Model Architect

**Role:** Evaluate the integrity of data representations, key schemas, and state model changes.

**Findings:**

**1. HasFailed key schema (Confidence: 97%)**
The new key format `goal:Gather:{itemId}:{targetCount}:failed` is sound. The prior format `goal:Gather:{itemId}:failed` was a shared namespace across all gather goals for a given item regardless of count, meaning a failed gather-10 could suppress a gather-1 attempt or vice versa. The fix correctly scopes failure state to the specific (item, count) pair. This is a well-formed cross-goal isolation pattern.

**2. Runtime fact-setting gap (Confidence: 99%)**
The architect observes that `AgentBackgroundService` does not set the `HasFailed` fact via this key at all — it tracks consecutive failures through a separate counter. The key is only read (in `GenericGatherGoal.HasFailed`) to check for an externally set failure marker. This means the collision fix is entirely future-proofing: no existing production path sets this key. There is no regression risk from the format change at this time.

**3. P1-A interface change (Confidence: 95%)**
The `IPlanner` interface change is backward-compatible due to the default parameter value (`originalGoal = null`). Existing call sites that do not pass the parameter continue to compile and behave identically. The architect notes that `AgentBackgroundService` calls `PlanAsync(_currentGoal)`, not `ReplanAsync`, so the runtime replanning path is not exercised by production code as currently written. The fix is logically correct and positions the interface for future use.

**4. architecture.md update (P1-C, Confidence: 80%)**
The journal semantics section is described in the sprint brief as "to be added." The architect cannot confirm it was committed in this sprint. This item is carried forward as a Sprint 29 P0 task.

**Blocking issues:** None.
**Deferred:** architecture.md journal semantics update (Sprint 29 P0-B); runtime validation of P0-C key once ABS or a future caller begins setting the fact.

---

### Seat 3: Retrieval Specialist

**Role:** Assess search, memory, and tool infrastructure changes.

**Findings:**

**1. SearchMemoryTool.cs decoding (Confidence: 95%)**
`SearchMemoryTool.cs` was among the base64-encoded files. Its decoding is a prerequisite for any memory retrieval functionality to compile. The specialist confirms this file is in the fix list. Because CI is unconfirmed, actual runtime retrieval behavior cannot be validated from this review.

**2. CreatePageTool.cs decoding (Confidence: 95%)**
Same as above. `CreatePageTool.cs` required decoding; it is in the fix list.

**3. No net-new retrieval logic (Confidence: 99%)**
Sprint 28 introduced no new retrieval algorithms or memory schema changes beyond the HasFailed key format (covered by Data Model Architect). The sprint was primarily corrective.

**4. FakeTimeProvider.cs (Confidence: 96%)**
`FakeTimeProvider.cs` decoding restores test infrastructure for time-dependent retrieval scenarios. This is test-only impact.

**Blocking issues:** None.
**Deferred:** Confirm `SearchMemoryTool` and `CreatePageTool` compile and pass integration tests once CI is green.

---

### Seat 4: Human Learning Advocate

**Role:** Evaluate documentation quality, onboarding clarity, and knowledge transfer value of sprint artifacts.

**Findings:**

**1. Sprint documentation (Confidence: 90%)**
The sprint brief is thorough and clearly delineates P0/P1/P2 items. Future maintainers can reconstruct intent from the brief. The council review (this document) and the handoff document provide forward continuity.

**2. P1-C architecture.md (Confidence: 80%)**
The journal semantics section was scoped for Sprint 28 but is not confirmed committed. This is a documentation gap. The advocate rates this as a meaningful omission because architectural intent for journal behavior is not otherwise captured in discoverable form.

**3. Sprint28Tests.cs readability (Confidence: 88%)**
17 test methods are present. The advocate notes that P0-B tests operate on behavioral contracts rather than verifying actual logger invocations. While this is a valid testing approach, it reduces the self-documenting value of the tests: a future maintainer reading the test suite cannot immediately determine whether the logger call is exercised. Adding a brief comment in the test class explaining the behavioral-contract approach would improve onboarding clarity.

**4. HasFailed key documentation (Confidence: 92%)**
The key format change is captured in code and in the handoff document. The advocate recommends the format string `goal:Gather:{itemId}:{targetCount}:failed` be added as a code comment at the definition site in `GenericGatherGoal.cs` so that any future author setting this fact can find the expected format without consulting commit history.

**Blocking issues:** None.
**Deferred:** architecture.md journal semantics (Sprint 29 P0-B); code comment at HasFailed definition site (Sprint 29 P1-B or P2); P0-B test clarity annotation.

---

### Seat 5: Skeptical Reviewer

**Role:** Stress-test assumptions, surface risks that optimistic framing may have understated.

**Findings:**

**1. P1-A runtime path concern (Confidence: 92% that the concern is valid)**

The skeptic raises a substantive concern: the P1-A fix to `IPlanner.ReplanAsync` is described as correcting a routing issue so that `PlannerRouter.ReplanAsync` uses `originalGoal` when provided. However, `AgentBackgroundService` — the primary production caller — calls `PlanAsync(_currentGoal)`, not `ReplanAsync`. This means the fixed code path is never exercised in production. The fix is logically correct and the interface is now properly shaped, but the skeptic notes that:

- There is no production caller that exercises `ReplanAsync` with a non-null `originalGoal`.
- The 4 P1-A tests in `Sprint28Tests.cs` test the router's internal routing logic, not an end-to-end replan flow.
- If a future developer adds a replan call, they will be relying on untested integration between ABS and the corrected router.

**Verdict from skeptic:** This is not a blocker — the fix is correct — but the acceptance criteria should explicitly require that `ReplanAsync` be called from at least one integration test path before Sprint 29 closes. Marking as DEF-P1-A-coverage.

**2. P0-B test methodology concern (Confidence: 85% that the concern is valid)**

The P0-B tests are described as "behavioral contract tests, not actual logger invocations." The skeptic interprets this to mean the tests do not verify that `LogWarning` is actually called. If the `LogWarning` call were deleted from `BuildGoalDecomposer.ReadOriginFact`, the behavioral contract tests would likely still pass (because the behavioral outcome — returning 0 — is unchanged). This means the tests do not enforce the specified fix.

**Verdict from skeptic:** DEF-P0-B-logverify — Sprint 29 P1-B should add a test using `ILogger` mock/test-double that asserts `LogWarning` is invoked on the missing-fact and unparseable-fact paths. Until then, the P0-B acceptance criterion is not fully testable.

**3. Remaining base64 files concern (Confidence: 70% that more files exist)**

The 13 files listed were identified and fixed. However, the sprint brief itself notes: "WorldStateProjector is also base64-encoded — check if more files need fixing." The skeptic flags that the fix scope may be incomplete. If additional base64-encoded files exist beyond the 13 listed, CI may still fail to build even after the 13 fixes.

**Verdict from skeptic:** BLOCKING for CI green — Sprint 29 P0-A must include a full sweep for remaining base64-encoded files, not just a CI pass/fail check. Recommend a grep for `\\/\\*=` or a file-size heuristic against known source files before marking CI clean.

**4. CI confirmation gap (Confidence: 99% that this is unresolved)**

No check runs are visible for HEAD `13b00633`. Sprint 28 cannot be fully closed without CI confirmation. The skeptic considers this the highest-priority open item.

**Blocking issues:**
- BLK-1: CI not confirmed at HEAD — Sprint 28 cannot be fully closed.
- BLK-2 (conditional): If WorldStateProjector or other undiscovered base64 files exist, build will fail regardless of the 13 fixes.

**Deferred:**
- DEF-P1-A-coverage: Integration test for ReplanAsync with non-null originalGoal.
- DEF-P0-B-logverify: Logger invocation test for BuildGoalDecomposer.ReadOriginFact.

---

### Seat 6: Synthesizer

**Role:** Integrate all seat findings into a verdict with explicit triage, acceptance criteria, and forward action.

**Summary of findings across seats:**

| Area | Confidence | Status |
|---|---|---|
| Base64 fixes (13 files) | 95% | Approved pending CI |
| P0-B ILogger injection | 97% | Approved |
| P0-C HasFailed key format | 98% | Approved |
| P0-C runtime coverage | 99% | Future-proofing only, no regression |
| P1-A ReplanAsync signature | 96% | Approved |
| P1-A runtime path coverage | 92% | Deferred concern |
| P0-B test meaningfulness | 85% | Deferred concern |
| Sprint28Tests.cs presence | 94% | Approved |
| architecture.md (P1-C) | 80% | Not confirmed committed |
| CI status | N/A | Blocking — unconfirmed |
| WorldStateProjector base64 | 70% | Potential blocker |

**Verdict: APPROVED WITH DEFERRED FINDINGS**

Sprint 28 is approved for merge/continuity, subject to the following:

**Blocking (must resolve before Sprint 29 work begins):**

- BLK-1: Confirm CI green on HEAD `13b00633864ebdcb634e6463770878c342f24637`. Check GitHub check-runs endpoint. Do not proceed with Sprint 29 code changes until confirmed.
- BLK-2: Sweep codebase for additional base64-encoded files (WorldStateProjector is specifically flagged). Fix and commit any discovered files before Sprint 29 task work.

**Deferred (Sprint 29 P1):**

- DEF-P0-B-logverify: Add `ILogger` mock/test-double test asserting `LogWarning` is invoked on missing-fact and unparseable-fact paths in `BuildGoalDecomposer.ReadOriginFact`.
- DEF-P1-A-coverage: Add integration test (or at minimum a unit test) that exercises `PlannerRouter.ReplanAsync` with a non-null `originalGoal` flowing through to a decomposer call, proving the routing fix is exercised end-to-end.
- DEF-P1-C: Commit architecture.md journal semantics section (Sprint 29 P0-B).

**Deferred (Sprint 29 P2):**

- DEF-DOC-1: Add code comment at `GenericGatherGoal.HasFailed` definition documenting the key format `goal:Gather:{itemId}:{targetCount}:failed`.
- DEF-DOC-2: Add brief comment in Sprint28Tests.cs P0-B fixture explaining behavioral-contract test approach.

**Testable Acceptance Criteria for Sprint 29 CI closure:**

1. `gh api repos/{owner}/{repo}/commits/13b00633864ebdcb634e6463770878c342f24637/check-runs` returns at least one run with `status: completed` and `conclusion: success`.
2. `dotnet build` exits 0 on the branch after any newly discovered base64 files are fixed.
3. `dotnet test` passes all 261+ tests including the 17 new Sprint28Tests.
4. `BuildGoalDecomposer.ReadOriginFact` has at least one test that asserts a logger warning is emitted (DEF-P0-B-logverify resolved).
5. `Data/Pages/architecture.md` contains the journal semantics section and is committed on the branch.

---

*Council review authored by automated agent on 2026-06-20. All confidence values reflect source-level analysis; runtime behavior is unconfirmed pending CI.*
