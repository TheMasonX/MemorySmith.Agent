# Council Review — Sprint 29
**Date:** 2026-06-20
**Branch:** sprint-5-tool-safety
**HEAD at review:** f0e13c44ade7795eb16afe916dab19493507892c ("Another batch of audits")
**Review scope:**
1. Sprint 28 implementation claims validated against actual source files
2. New commit f0e13c44 — four audit reports under `Data/Pages/Audit/` assessed
3. Sprint 29 handoff assumptions cross-checked against current codebase state
4. Synthesis of new findings into Sprint 30 priority queue

---

## Seat 1 — Source-Grounded Archivist
*Validates specific code claims against committed file content.*

**Confidence: 96%**

### P0-B: BuildGoalDecomposer LogWarning — ✅ CONFIRMED
`Agent.Planning/Decomposition/BuildGoalDecomposer.cs` SHA `be423cea`:
- Constructor takes `ILogger<BuildGoalDecomposer> logger` via DI.
- `ReadOriginFact` emits `LogWarning("Build origin fact missing or unparseable; defaulting to (0,0,0)...")` on the missing-key path.
- `ReadOriginFact` emits a second `LogWarning("Build origin fact unparseable; defaulting to 0 for axis {Axis}...")` on the pattern-match fallback arm.
- Return value on both log paths is `0`.
Handoff claim: VERIFIED.

### P0-C: GenericGatherGoal HasFailed key — ✅ CONFIRMED
`Agent.Planning/Goals/GenericGatherGoal.cs` SHA `33c913e6`:
- `HasFailed` property reads `state.Facts.TryGetValue($"goal:{Name}:{targetCount}:failed", ...)`.
- `Name` returns `$"Gather:{item.ItemId}"`, so full key is `goal:Gather:{itemId}:{targetCount}:failed`.
- Comment in source: `// CHANGED: include targetCount in key to prevent cross-goal collision`.
Handoff claim: VERIFIED.

**Observation (non-blocking):** the fact key is only READ, never SET in the current production path (`AgentBackgroundService` tracks failures via a consecutive-failures counter). The format change future-proofs the write path but has no behavioral effect today. See Seat 5 for challenge detail.

### P1-A: PlannerRouter IPlanner parameter + originalGoal — ✅ CONFIRMED
`Agent.Planning/Router/PlannerRouter.cs` SHA `a7a888c0`:
- Constructor: `PlannerRouter(DecomposerRegistry registry, IPlanner htnPlanner)` — parameter type is `IPlanner`, not `HtnPlanner`.
- `ReplanAsync`: uses `originalGoal ?? new SimpleGoal(...)` as the routing goal.
- XML doc confirms Sprint 28 P1-A motivation.
Handoff claim: VERIFIED.

### P1-C: architecture.md journal semantics — ✅ CONFIRMED
`Data/Pages/architecture.md` SHA `a077f84e`:
- Section "Agent Journal Semantics" present with: bounded diagnostic buffer rationale, 1000-entry cap, in-process-only semantics, 11 event types, MemorySmith REST API for durable memory.
- Closing note: "This section closes Deep Code Audit Finding 4 from Sprint 25 external audit (Sprint 28 P1-C)."
Handoff claim: VERIFIED.

### WorldStateProjector.cs — ⛔ STILL BASE64 (BLOCKING)
`Agent.Core/WorldStateProjector.cs` SHA `a9a4fa98`:
- File content begins: `bmFtZXNwYWNlIEFnZW50LkNvcmU7` — base64 for `namespace Agent.Core;`.
- The Sprint 28 handoff flagged this file as "potentially base64-encoded, not confirmed fixed, rolls to Sprint 29 P0-A." CONFIRMED: it was NOT fixed in Sprint 28.
- Compile-blocking.

### ToolDispatcher.cs — ⛔ NEWLY CONFIRMED BASE64 (BLOCKING — NEW FINDING)
`Agent.Tools/ToolDispatcher.cs` SHA `e7ea0a93`:
- File content begins: `bmFtZXNwYWNlIEFnZW50LlRvb2xzOg==` — base64 for `namespace Agent.Tools:`.
- This file was NOT on the Sprint 28 fix list and was NOT flagged in the Sprint 29 handoff.
- Compile-blocking. The Sprint 28 base64 sweep was incomplete.
- When decoded, the ToolDispatcher contains the Sprint 5 schema validation logic (confirmed by decoding the base64 content in review). The validation IS present — but unreachable until the file is decoded.

### SearchMemoryTool.cs — ⛔ INTERFACE CONTRACT MISMATCH (BLOCKING — NEW FINDING)
`Agent.Tools/Tools/SearchMemoryTool.cs` SHA `7c0bd266`:
- File is valid C# (properly decoded in Sprint 28).
- Implements `ITool` in the class declaration.
- Method signature: `public async Task<ToolResult> ExecuteAsync(ActionData action, CancellationToken ct)`.
- `ITool` interface (`Agent.Core/Interfaces/ITool.cs` SHA `8ce27f4e`) requires: `Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)`.
- These are different signatures. `SearchMemoryTool : ITool` with only the `ActionData` overload is a **compile error**: `SearchMemoryTool` does not implement interface member `ITool.ExecuteAsync(JsonElement, CancellationToken)`.
- Root cause: the Sprint 5 interface overhaul changed `ITool.ExecuteAsync` from `ActionData` to `JsonElement`, but `SearchMemoryTool` was base64-encoded before it could be updated. When decoded in Sprint 28, the old signature was restored.

### CreatePageTool.cs — ⚠️ PRESUMED SAME MISMATCH (BLOCKING — UNVERIFIED)
- Two independent audit reports (`deep_audit_report.md` and `deep_audit_report (1).md`) assert `CreatePageTool` has the same `ActionData` signature.
- Not directly verified in this review. Must be confirmed and fixed with `SearchMemoryTool` in Sprint 30 P0-B.

---

## Seat 2 — Data Model Architect
*Evaluates structural correctness of data models and interface contracts.*

**Confidence: 88%**

### ITool interface contract
The `ITool` interface is correct as defined: `ExecuteAsync(JsonElement)` is the right design for a schema-validated dispatch boundary. The `ActionData` pattern in `SearchMemoryTool`/`CreatePageTool` is a remnant of the pre-Sprint-5 tool API. The fix is to give each tool a `JsonElement`-accepting entry point that extracts values internally (or via a thin adapter).

Preferred fix pattern for `SearchMemoryTool`:
```csharp
public Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken ct)
{
    var query = arguments.TryGetProperty("query", out var q) ? q.GetString()
                : throw new ArgumentException("SearchMemory requires 'query' parameter.");
    var limit = arguments.TryGetProperty("limit", out var l) && l.TryGetInt32(out var li) ? li : 10;
    return ExecuteCore(query!, limit, ct);
}
```

### HasFailed key write path — CORRECTNESS GAP (non-blocking)
The new key format `goal:Gather:{itemId}:{targetCount}:failed` is correctly READ in `GenericGatherGoal.HasFailed`. But the fact is never SET in the current production path — `AgentBackgroundService` uses a consecutive-failures counter, not a world-state fact. Until a write site is added (using the correct format), `HasFailed` always returns `false`, which means the P0-C change has no observable runtime effect today. Add a write site or document this as intentionally future-proofing only. See DEF-DOC-3.

### WorldState legacy SetFact path — MEDIUM STRUCTURAL CONCERN
The `WorldState.Builder.SetFact(string, object?)` overload writes only to the legacy `Facts` dictionary, bypassing the bounded `StructuredFacts` list and its `MaxFacts` cap. Callers using the legacy overload can accumulate unlimited facts. This is an open finding from prior audits (DEF-3 equivalent) and remains unresolved.

### PlannerRouter constructor — ✅ CORRECT
Broadening from `HtnPlanner` to `IPlanner` is backward-compatible since `HtnPlanner : IPlanner`. Production DI wiring is unaffected.

---

## Seat 3 — Retrieval Specialist
*Evaluates knowledge base accuracy, audit file integrity, and cross-reference consistency.*

**Confidence: 85%**

### New audit files — assessment by file

**`MemorySmith_Agent_Audit_Sprint26.md`** (703 lines, Sprint 26 / v0.25.0 context):
This is a comprehensive architectural audit targeting the Sprint 26 post-implementation state. It is now 3 sprints stale. Most findings are either resolved (God Object reduced via decomposer registry and governor) or superseded. However, two new-to-backlog items warrant carrying forward:
- **SEC-01**: REST endpoints unauthenticated — not in the task backlog.
- **SEC-02**: Node.js port 5050 unauthenticated — not in the task backlog.
Both are correctly identified as "Critical for any non-localhost deployment." These should be escalated to the backlog.

**`memorysmith_agent_code_audit_report(1).md`** (94 lines, HEAD 6392007a):
Finding 1 ("ToolDispatcher still has a TODO to validate") is **misleading** — the auditor was unable to read `ToolDispatcher.cs` because it is base64-encoded. When decoded, the file DOES have Sprint 5 validation logic. This finding should be marked as **STALE / ARTIFACT OF BASE64 ENCODING**.
Finding 2 (replanning preserves only narrow context) is partially addressed by Sprint 28 P1-A. The prefix-based context preservation remains, but the original goal type is now preserved for routing.
Finding 3 (world fact cap not evidenced) is partially true — StructuredFacts has MaxFacts, but the legacy SetFact path bypasses it.
Finding 4 (shutdown less robust than claimed) refers to Sprint 5 P2. Not verified in this review; carries forward as an open question.

**`memorysmith_agent_deep_audit_report.md`** and **`memorysmith_agent_deep_audit_report (1).md`** (158 and 207 lines, same HEAD 6392007a):
Both documents are versions of the same deep audit. Key findings:
- SearchMemoryTool/CreatePageTool interface mismatch: **CONFIRMED TRUE** (95% confidence, now verified in source).
- ITimeProvider not in ReplanGovernor: **LIKELY TRUE** (ongoing known gap from prior sprints).
- WorldState SetFact legacy path: **CONFIRMED** (see Seat 2).
- World-fact substring matching: **LIKELY TRUE** (ongoing gap, partially in backlog as DEF-4).
- Alias drift risk: **PARTIALLY ADDRESSED** — DEF-9 (XML doc comment) is in the backlog.

---

## Seat 4 — Human Learning Advocate
*Evaluates whether sprint work advances real-world usability and agent reliability.*

**Confidence: 82%**

### What Sprint 28 did for reliability

The P0-C change (`HasFailed` key with targetCount) is the right design for multi-goal robustness. When a write path is added, it will prevent a failed gather-5-oak-log from suppressing gather-64-oak-log for the same item.

The P1-A change (PlannerRouter originalGoal) directly fixes a silent regression where every replan for decomposer-handled goals (Gather, Build, Craft) silently routed to HTN fallback. Users would see the bot take wrong actions on replan. This is a high-value correctness fix.

### Security risk — real-world impact

SEC-01 and SEC-02 (unauthenticated REST and Node.js endpoints) are not theoretical. If the bot runs on a home Minecraft server — which is the expected use case — other players on the same LAN can enqueue arbitrary bot actions. This is a meaningful risk for the target user context. It should be Sprint 30 P1 at minimum, not an architectural backlog item.

### Interface mismatch impact

The SearchMemoryTool/CreatePageTool interface mismatch means the entire Agent.Tools project fails to compile. This has been the compile state since Sprint 5 changed the interface. Every sprint since Sprint 5 that claimed "CI green" was either (a) working with a pre-Sprint-5 checkout, or (b) the CI check was checking main (not this branch), or (c) CI was never actually running for this branch. The absence of check-runs on all inspected SHAs supports option (c).

---

## Seat 5 — Skeptical Reviewer
*Challenges all claims. Looks for incomplete work, misleading claims, and structural risks.*

**Confidence: 91%**

### Challenge 1 — Has CI EVER been green on this branch?

The CI check-runs endpoint returns `total_count: 0` for every SHA checked, including the Sprint 28 implementation commits. This means either:
(a) GitHub Actions workflows exist but are not triggering for this branch.
(b) No CI workflow file exists or is configured for this branch.
(c) The CI was running earlier and the runs aged out of the API response (unlikely — GitHub keeps check runs for 90 days).

The Sprint 28 council doc says "CI: queued" for prior sprints, but those were against main, not sprint-5-tool-safety. Every handoff that cites CI as "green" for this branch should be treated as **unverified** until check-runs are confirmed.

**Impact**: without CI, every compile-breaking defect persists until a human or agent manually builds and tests. The two base64 files and the interface mismatch could have been caught immediately with a green-build gate.

### Challenge 2 — The "17 new Sprint 28 tests" may be non-functional

Sprint28Tests.cs was committed. But if `ToolDispatcher.cs` is base64-encoded, the `Agent.Tools` project fails to compile. The test project references `Agent.Tools` (it must, to test tool behavior). Therefore, the test project also fails to compile, and the 17 tests cannot run. The count "17 new tests" is accurate for lines of test code committed, but "17 tests running" is not confirmed.

**Caveat**: if Sprint28Tests.cs tests only `PlannerRouter`, `BuildGoalDecomposer`, and `GenericGatherGoal` (which are in `Agent.Planning` and `Agent.Core`), the test project might compile if `Agent.Tools` is not a dependency for those specific tests. This requires verification — the test project's project references must be checked.

### Challenge 3 — HasFailed key change has zero runtime effect today

P0-C changed the key format from `goal:Gather:{itemId}:failed` to `goal:Gather:{itemId}:{targetCount}:failed`. The handoff correctly notes that "the key is only read, not set, in the current production path." This means:
- Old key format: would never have matched (assuming no write site existed before)
- New key format: also never matches (no write site for either format)
- Net runtime effect: none — `HasFailed` returns `false` regardless of the key format

The change is correct as future-proofing, but it should not be presented as a correctness fix that changes runtime behavior. It is a format standardization.

### Challenge 4 — Sprint 29 P0-C (version bump) remains undone

The Sprint 29 handoff requires updating version strings in README, Program.cs, and `/api/about` to v0.28.0. None of the commits between 99c524ad and f0e13c44 appear to include a version bump. The version bump task rolls forward to Sprint 30.

### Challenge 5 — Audit batch is partially stale

Two of the four new audit files (`deep_audit_report.md` and `deep_audit_report (1).md`) appear to be the same audit in two versions (the (1) version has an additional "second pass" section). Publishing near-duplicate documents reduces signal-to-noise in the audit corpus. The `code_audit_report(1).md` contains the misleading ToolDispatcher TODO finding (see Seat 3). The Sprint 26 audit is legitimately historical. Future audits should clearly state which SHA they were run against and flag any findings that depend on being able to read the source (i.e., base64-encoded files may produce false negatives).

---

## Seat 6 — Synthesizer
*Produces consolidated verdict, blocking/deferred triage, and Sprint 30 task queue.*

**Confidence: 88%**

### Verdict: APPROVED with 3 blocking findings

Sprint 28 implementation is confirmed correct for P0-B, P0-C, P1-A, and P1-C. The audit batch has been assessed and synthesized. Three blocking issues must be resolved before any Sprint 30 feature work begins.

### Blocking findings (fix before Sprint 30 task work)

| ID | File | Issue | Priority |
|---|---|---|---|
| B-1 | `Agent.Core/WorldStateProjector.cs` | Still base64-encoded (known Sprint 29 deferral) | Sprint 30 P0-A |
| B-2 | `Agent.Tools/ToolDispatcher.cs` | NEWLY CONFIRMED base64-encoded (missed in Sprint 28 sweep) | Sprint 30 P0-A |
| B-3 | `Agent.Tools/Tools/SearchMemoryTool.cs` | `ExecuteAsync(ActionData)` does not satisfy `ITool.ExecuteAsync(JsonElement)` — compile error | Sprint 30 P0-B |
| B-4 | `Agent.Tools/Tools/CreatePageTool.cs` | Same as B-3 (asserted by two audits, unverified in this review) | Sprint 30 P0-B |

### Deferred findings

| ID | Finding | Target |
|---|---|---|
| D-1 | SEC-01: REST endpoints unauthenticated — not in backlog | Sprint 30 P1 |
| D-2 | SEC-02: Node.js port 5050 unauthenticated — not in backlog | Sprint 30 P1 |
| D-3 | CI gap — zero check-runs on all SHAs; no automated build gate | Sprint 30 P0 (diagnose) |
| D-4 | HasFailed write path — fact key format correct but no write site in production | Sprint 30 P2 (DEF-DOC-3) |
| D-5 | WorldState.SetFact legacy path bypasses StructuredFacts cap | Sprint 30 P2 |
| D-6 | Audit credibility — code_audit_report(1).md ToolDispatcher TODO is base64 artifact | Mark as STALE |
| D-7 | Version bump to v0.28.0 — not done in Sprint 29 | Sprint 30 P0-C |
| D-8 | DEF-P0-B-logverify — logger invocation unit tests for BuildGoalDecomposer | Sprint 30 P1 |
| D-9 | DEF-NEW-6 through DEF-NEW-10 from Sprint 28 backlog | Sprint 30 P2/P3 |

### Average confidence per seat

| Seat | Confidence |
|---|---|
| Source-Grounded Archivist | 96% |
| Data Model Architect | 88% |
| Retrieval Specialist | 85% |
| Human Learning Advocate | 82% |
| Skeptical Reviewer | 91% |
| Synthesizer | 88% |
| **Average** | **88.3%** |

---

## Testable Acceptance Criteria for Sprint 30

Sprint 30 is complete when:

- [ ] `Agent.Core/WorldStateProjector.cs` contains valid C# (namespace declaration visible, no base64 string as file content).
- [ ] `Agent.Tools/ToolDispatcher.cs` contains valid C# (namespace declaration visible, no base64 string as file content).
- [ ] `Agent.Tools/Tools/SearchMemoryTool.cs` implements `ITool.ExecuteAsync(JsonElement, CancellationToken)`.
- [ ] `Agent.Tools/Tools/CreatePageTool.cs` implements `ITool.ExecuteAsync(JsonElement, CancellationToken)`.
- [ ] Full sweep: no remaining base64-encoded `.cs` files on the branch (grep for files whose first line matches `^[A-Za-z0-9+/]{60,}={0,2}$`).
- [ ] `dotnet build` exits with code 0 on the sprint-5-tool-safety branch.
- [ ] `dotnet test` passes all tests (261+ baseline) with zero failures.
- [ ] Version string `v0.28.0` appears in README, Program.cs, and `/api/about` response.
- [ ] CI check-runs confirmed: at least one `completed`/`success` run for the Sprint 30 HEAD SHA.
- [ ] SEC-01 addressed OR explicitly triaged as Sprint 31 with recorded rationale.
- [ ] Sprint 30 council review document committed.
- [ ] Sprint 31 handoff document committed.
