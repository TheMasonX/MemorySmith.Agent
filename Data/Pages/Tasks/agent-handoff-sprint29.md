# Agent Handoff — Sprint 29
**Date:** 2026-06-20
**Branch:** sprint-5-tool-safety
**Branch HEAD:** 13b00633864ebdcb634e6463770878c342f24637
**Version:** v0.28.0

---

## Section I: What Just Happened (Sprint 28)

Sprint 28 was a correctness and infrastructure sprint. No new features were shipped. The work fell into five categories.

### Base64 Decoding (Compile-blocking — resolved)

13 source files on disk were base64-encoded rather than containing valid C#. This was a compile-breaking issue. All 13 were decoded to proper C# source:

- `IGoal.cs`
- `IItemSpecGoal.cs`
- `ITimeProvider.cs`
- `ActionLifecycle.cs`
- `PendingAction.cs`
- `SearchMemoryTool.cs`
- `CreatePageTool.cs`
- `GatherGoalDecomposer.cs` (also had a corrupt byte fixed)
- `CraftItemGoalDecomposer.cs`
- `FakeTimeProvider.cs`
- `AgentBackgroundService.cs`
- `AgentBackgroundServiceTestHelper.cs` (P1-A fix also applied)
- `PlannerRouter.cs` (P1-A fix also applied)
- `HtnPlanner.cs` (P1-A fix also applied)

Note: `WorldStateProjector.cs` may also be base64-encoded. This was flagged but not confirmed fixed in Sprint 28. Sprint 29 P0-A sweep must check for remaining encoded files.

### P0-B: BuildGoalDecomposer Origin Fact Logging

`BuildGoalDecomposer.ReadOriginFact` previously returned 0 silently when the origin fact was missing or could not be parsed. It now emits `LogWarning` in both cases. `ILogger` is injected via constructor. This makes silent data quality problems observable in logs.

### P0-C: GenericGatherGoal HasFailed Key Format

The `HasFailed` property key changed from:

```
goal:Gather:{itemId}:failed
```

to:

```
goal:Gather:{itemId}:{targetCount}:failed
```

This prevents cross-goal collision where a failed gather-N could suppress an unrelated gather-M for the same item. Important: `AgentBackgroundService` does not set this fact — it tracks failures via a consecutive-failures counter. The key is only read (not set) in the current production path. The change is future-proofing for any caller that begins setting this fact.

### P1-A: IPlanner.ReplanAsync Signature and PlannerRouter Routing

`IPlanner.ReplanAsync` gained a new parameter `IGoal? originalGoal = null`. All three implementors were updated:

- `PlannerRouter.ReplanAsync` — uses `originalGoal` when provided to route to the correct decomposer.
- `DecomposerPlanner.ReplanAsync` — same fix.
- `HtnPlanner.ReplanAsync` — accepts and ignores the parameter (HTN planner does not need it).

`PlannerRouter` constructor parameter broadened from `HtnPlanner` to `IPlanner` for testability and correct DI resolution.

Caveat: `AgentBackgroundService` calls `PlanAsync(_currentGoal)` directly, not `ReplanAsync`. The fixed path is exercised by tests but not by the current production replan flow. See Section V for the deferred follow-up.

### P1-C: architecture.md Journal Semantics

The journal semantics section addition to `Data/Pages/architecture.md` was scoped for Sprint 28 but is not confirmed committed. This rolls forward to Sprint 29 P0-B.

### New Tests: Sprint28Tests.cs

17 new test methods were added across 4 fixtures:

- P0-B behavioral contract tests (origin fact missing, origin fact unparseable)
- P0-C HasFailed key collision tests (8 tests covering old vs. new key format)
- P1-A PlannerRouter routing tests (4 tests, with and without `originalGoal`)
- Interface contract tests

Note from council review: P0-B tests validate behavioral contracts, not actual logger invocations. A mock/test-double test for the `LogWarning` call is deferred to Sprint 29 P1-B.

### Copilot PR Comment Resolution

11 base64 encoding issues resolved in response to Copilot PR review comments. `BuildGoalDecomposer` range check addressed (P2-A-related long guard added). `MemorySmithItemRegistry` empty string fallback was already fixed in a prior sprint (no action needed).

---

## Section II: Critical Invariants

These invariants must be preserved by all future work on this branch. They represent either hard-coded contracts in the data layer, interface contracts across component boundaries, or behavior that tests depend on.

**From prior sprints:**

1. `IGoal`, `IItemSpecGoal`, and `ITimeProvider` are interfaces, not classes. Do not add concrete implementation logic to them directly.
2. `ActionLifecycle` state transitions must follow the defined enum progression. Tests assert specific transition sequences.
3. `PendingAction` fields are serialized to/from world state. Field names and types are part of the persistence contract.
4. `SearchMemoryTool` and `CreatePageTool` are tool implementations registered by name. Their tool-name strings are the integration contract with the planner layer.
5. `GatherGoalDecomposer` and `CraftItemGoalDecomposer` decompose goals deterministically given the same world state. Decomposition output is used to build plan steps; changes to decomposition logic are breaking changes.
6. `FakeTimeProvider` must implement `ITimeProvider` exactly. It is the only time provider used in tests; divergence from `ITimeProvider` causes silent test invalidity.
7. `AgentBackgroundService` is the production orchestration loop. Changes to its `PlanAsync` call site or its consecutive-failures counter must be reviewed against the full failure/recovery contract.
8. `HtnPlanner` is the production planner resolved via DI. Interface-level changes to `IPlanner` must not break `HtnPlanner`'s DI registration.
9. `BuildGoalDecomposer.ReadOriginFact` returns 0 on missing or unparseable origin fact AND emits `LogWarning`. Both behaviors are now part of the contract; removing either is a regression.
10. All source files on this branch must be valid UTF-8 C# (or appropriate file type). Base64-encoded files are a compile-blocking defect. If a file looks like a long single-line base64 string, it must be decoded before any other work.

**Added in Sprint 28:**

11. `P0-C HasFailed key includes targetCount` — the fact key format is `goal:Gather:{itemId}:{targetCount}:failed`. Any future code that sets this fact must use this exact format. The `targetCount` must be the specific count for the goal instance, not a default or shared value. Using the old format `goal:Gather:{itemId}:failed` will create a key that is never read by `GenericGatherGoal.HasFailed` and will not suppress goal retry.
12. `PlannerRouter constructor takes IPlanner (not HtnPlanner) as fallback` — in production, `HtnPlanner` is resolved via DI and passed as `IPlanner`. Do not revert the constructor parameter to `HtnPlanner`; doing so would break constructor injection for any non-HTN planner and reduces testability.

---

## Section III: CI Status

**CI status at handoff: NOT YET CONFIRMED**

No check runs are visible for HEAD `13b00633864ebdcb634e6463770878c342f24637` as of the time this handoff was written.

Sprint 29 must not begin task work until CI is confirmed green. The first action in Sprint 29 is:

```
gh api repos/{owner}/{repo}/commits/13b00633864ebdcb634e6463770878c342f24637/check-runs
```

Expected: at least one run with `"status": "completed"` and `"conclusion": "success"`.

If CI is not yet triggered, the run may need to be initiated manually or the workflow may need to be re-run. If CI fails, diagnose before proceeding — likely causes are undiscovered base64 files or compilation errors in recently decoded files.

---

## Section IV: Current Codebase State

This section reflects the known state of the repository as of HEAD `13b00633`.

**Code files committed in Sprint 28:** 18 code files updated or created (13 base64-decoded files + `BuildGoalDecomposer.cs` + `GenericGatherGoal.cs` + `IPlanner.cs` + `PlannerRouter.cs` + `DecomposerPlanner.cs` — some files overlap with the base64 list above).

**Test file committed in Sprint 28:** 1 (`Sprint28Tests.cs`, 17 new test methods).

**Total estimated tests:** approximately 261+ (Sprint 28 added 17 new tests on top of the prior count of approximately 244).

**Compile-blocking base64 files:** 13 confirmed fixed. `WorldStateProjector.cs` is flagged as potentially still encoded — must be verified in Sprint 29 P0-A.

**Architecture documentation:** `Data/Pages/architecture.md` journal semantics section not confirmed committed — rolls to Sprint 29 P0-B.

**Version:** README and `/api/about` and `Program.cs` have not yet been bumped to `v0.28.0` — this is Sprint 29 P0-C.

---

## Section V: Sprint 29 Task List

### P0 — Must complete before Sprint 29 is done

**P0-A: Confirm CI green on 13b00633**

Check the check-runs endpoint for HEAD `13b00633864ebdcb634e6463770878c342f24637`. Do not proceed with Sprint 29 code changes until at least one check run shows `completed` / `success`. As part of this check, sweep the codebase for additional base64-encoded files. `WorldStateProjector.cs` is specifically flagged. If found, decode and commit before re-checking CI.

Detection approach: look for files that are a single long line with only base64 characters (`[A-Za-z0-9+/=\n]`), or files whose first line is unusually long (>1000 characters) with no C# syntax.

**P0-B: Commit Data/Pages/architecture.md journal semantics section**

The journal semantics section was scoped for Sprint 28 (P1-C) but is not confirmed committed. This section must document:
- What constitutes a journal entry
- When journal entries are created vs. updated
- The relationship between journal pages and world state
- Retention / archival semantics if defined

Commit the update to `Data/Pages/architecture.md` on the current branch.

**P0-C: Bump version to v0.28.0**

Update the version string in all three canonical locations:
- `README.md` (version badge or header)
- `/api/about` endpoint response (wherever the version string is defined in source)
- `Program.cs` (if the version constant is defined there)

All three locations must agree. Commit as a single version-bump commit.

---

### P1 — High priority, complete if P0 is green

**P1-A: Verify AgentBackgroundService.cs compilation**

`AgentBackgroundService.cs` was base64-encoded and has been decoded. Once CI is green, confirm the build log shows no errors in this file specifically. If CI shows a compile error in `AgentBackgroundService.cs`, diagnose and fix before any other P1 work.

**P1-B: Meaningful P0-B unit tests — logger invocation verification**

Deferred from Sprint 28 council (DEF-P0-B-logverify). The current P0-B tests validate behavioral contracts (return value behavior) but do not assert that `LogWarning` is actually called. Add tests using a logger mock or test-double pattern that assert:

1. When `ReadOriginFact` is called and the origin fact key is absent from world state, `LogWarning` is invoked exactly once.
2. When `ReadOriginFact` is called and the origin fact value cannot be parsed as a number, `LogWarning` is invoked exactly once.
3. When `ReadOriginFact` is called and a valid origin fact is present, `LogWarning` is not invoked.

Recommended approach: use a `FakeLogger<BuildGoalDecomposer>` or a Moq/NSubstitute mock of `ILogger<BuildGoalDecomposer>`. If neither Moq nor NSubstitute is already in the test project dependencies, use the Microsoft `FakeLogger` from `Microsoft.Extensions.Logging.Testing` (available in .NET 8+) or write a minimal `CapturingLogger` test-double.

**P1-C: DEF-NEW-6 — ChatInterpreter.ResolveItemId plural map constrained**

`ChatInterpreter.ResolveItemId` uses a `TrimEnd('s')` heuristic that is too broad — it strips trailing `s` from all item names, causing false matches (e.g., "grass" -> "gra"). Replace with a constrained plural map: an explicit dictionary of known plural forms to canonical item IDs, falling back to exact match only. Do not use generic suffix stripping.

Files expected to change: `ChatInterpreter.cs`.

**P1-D: DEF-NEW-7 — Remove bare `doing` token from status regex**

The status-parsing regex includes a bare `doing` token that matches unintended input. Remove the `doing` token from the regex pattern and ensure the remaining tokens still cover the intended status values. Add a regression test asserting that the string `"doing"` alone does not parse as a valid status.

Files expected to change: wherever the status regex is defined (likely `ChatInterpreter.cs` or a constants/config file).

---

### P2 — Complete if time permits

**P2-A: BuildGoalDecomposer range check (long guard)**

A long guard was added in Sprint 28 in response to Copilot review. Verify the guard is correct and complete. The full P2-A item from the Sprint 28 backlog may have additional sub-items — review the original issue and close or carry forward each sub-item explicitly.

**P2-C and remaining Sprint 28 backlog (DEF-NEW-8, DEF-NEW-9, DEF-NEW-10)**

Review the Sprint 28 deferred list for any P2 items not yet addressed. Each item should be triaged: fix in Sprint 29, carry to Sprint 30, or close as won't-fix with a rationale recorded in the handoff.

**DEF-P1-A-coverage: ReplanAsync integration test**

Deferred from council (DEF-P1-A-coverage). Add an integration or unit test that exercises `PlannerRouter.ReplanAsync` with a non-null `originalGoal`, verifying that the call routes to the correct decomposer end-to-end. This closes the gap between the corrected interface and production usage.

**DEF-DOC-1: Code comment at GenericGatherGoal.HasFailed**

Add a code comment at the `HasFailed` property definition in `GenericGatherGoal.cs` documenting the full key format `goal:Gather:{itemId}:{targetCount}:failed` so future authors setting this fact can find the expected format without consulting commit history.

**DEF-DOC-2: Sprint28Tests.cs P0-B fixture annotation**

Add a brief comment in the P0-B test fixture explaining that the tests validate behavioral contracts (return value) rather than logger invocations, and that DEF-P0-B-logverify (P1-B above) covers the logger-invocation verification.

---

## Section VI: Files Expected to Change in Sprint 29

| File | Reason | Priority |
|---|---|---|
| `Data/Pages/architecture.md` | Journal semantics section (P0-B) | P0 |
| `README.md` | Version bump to v0.28.0 (P0-C) | P0 |
| `Program.cs` | Version bump to v0.28.0 (P0-C) | P0 |
| Version constant source file (TBD) | `/api/about` version string (P0-C) | P0 |
| `WorldStateProjector.cs` | Potentially base64-encoded; decode if confirmed (P0-A) | P0 |
| `Sprint28Tests.cs` or new test file | P0-B logger invocation tests (P1-B) | P1 |
| `ChatInterpreter.cs` | Plural map constraint (P1-C), status regex fix (P1-D) | P1 |
| `BuildGoalDecomposer.cs` | P2-A range check follow-up | P2 |
| `GenericGatherGoal.cs` | DEF-DOC-1 code comment | P2 |
| Any additional base64 files (TBD) | Decode if discovered in P0-A sweep | P0 |

---

## Section VII: GitHub and CI Tooling Reminders

**Checking CI status:**
```
gh api repos/{owner}/{repo}/commits/{sha}/check-runs
```
Look for `"status": "completed"` and `"conclusion": "success"` in the response. If no runs appear, the workflow may not have been triggered — check the Actions tab and re-run if needed.

**Checking open PRs:**
```
gh pr list --state open
```

**Viewing PR checks:**
```
gh pr checks {pr-number}
```

**Viewing recent commits:**
```
gh api repos/{owner}/{repo}/commits --jq '.[0:5] | .[] | {sha: .sha[0:8], message: .commit.message}'
```

**Pushing a branch:**
```
git push origin sprint-5-tool-safety
```
Do not force-push to `main` or `master`.

**Running tests locally:**
```
dotnet test
```
Always run this before pushing a commit that modifies test infrastructure.

**Decoding a base64-encoded file (if discovered):**
```
base64 --decode {encoded-file} > {output-file}
```
Verify the decoded output is valid C# before replacing the original.

---

## Section VIII: Definition of Done for Sprint 29

Sprint 29 is complete when all of the following are true:

- [ ] CI check-runs endpoint confirms `completed` / `success` on HEAD `13b00633864ebdcb634e6463770878c342f24637` (or the Sprint 29 HEAD if commits were added).
- [ ] No base64-encoded source files remain in the repository (WorldStateProjector and any others discovered in the P0-A sweep are decoded and committed).
- [ ] `Data/Pages/architecture.md` contains the journal semantics section and is committed on the branch.
- [ ] Version string reads `v0.28.0` in README, `/api/about` response, and Program.cs.
- [ ] `BuildGoalDecomposer.ReadOriginFact` has at least one test that asserts `LogWarning` is invoked on the missing-fact path and at least one test that asserts it is invoked on the unparseable-fact path (DEF-P0-B-logverify).
- [ ] `ChatInterpreter.ResolveItemId` uses an explicit plural map rather than `TrimEnd('s')` (P1-C).
- [ ] Status regex no longer matches bare `"doing"` token; regression test added (P1-D).
- [ ] `dotnet test` passes all tests (261+ baseline) with zero failures.
- [ ] Sprint 29 council review document drafted and saved.
- [ ] Sprint 30 handoff document drafted and saved.

---

*Handoff authored by automated agent on 2026-06-20. Do not push to GitHub until council review is complete and CI is confirmed.*
