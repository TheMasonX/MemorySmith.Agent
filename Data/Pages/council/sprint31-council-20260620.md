# MemorySmith.Agent — Sprint 31 Council Review
**Date:** 2026-06-20  
**Branch:** `sprint-5-tool-safety` (HEAD: `87bc1a5c`)  
**Scope:** New external audits (commits `f0e13c44`, `87bc1a5c`) + Sprint 30 handoff validation  
**Council format:** 6-seat with explicit dissent, per-seat confidence, blocking/deferred triage  
**Companion:** `Data/Pages/Tasks/agent-handoff-sprint31.md`

---

## Context for This Review

The user pushed two "batch of audits" commits to the branch:
- `f0e13c44` (2026-06-20T11:34Z) — older batch (Sprint 25–27 era)
- `87bc1a5c` (2026-06-20T13:46Z) — newer batch including one timestamp-dated refinement audit from today

Each council seat independently evaluated:
1. The Sprint 31 handoff doc claims vs. actual source state at HEAD
2. Each new audit finding — validated or refuted against source
3. Any net-new issues not in the prior backlog

---

## Seat 1: Source-Grounded Archivist
**Role:** Verifies every factual claim by reading actual source at HEAD (87bc1a5c).  
**Confidence: 91%**

### Sprint 30 Delivery Verification

| Claim | File | Evidence | Verdict |
|---|---|---|---|
| P0-A: WorldStateProjector.cs decoded | `Agent.Core/WorldStateProjector.cs` | File text field format is base64 (pattern: `bmFtZXNwYWNl...`) | **SUSPECT** — see BLK-02 |
| P0-A: ToolDispatcher.cs decoded | `Agent.Tools/ToolDispatcher.cs` | File text field is `bmFtZXNwYWNlIEFnZW50LlRvb2xzOwoK...`; decodes to valid C# with full schema validation | **SUSPECT** — see BLK-02 |
| P0-B: SearchMemoryTool ITool compliance | Sprint30Tests.cs structural test exists | Confirmed in test code | **CONFIRMED** (if Sprint30Tests.cs compiles) |
| P0-B: CreatePageTool ITool compliance | Sprint30Tests.cs structural test exists | Confirmed in test code | **CONFIRMED** (if Sprint30Tests.cs compiles) |
| P0-D: Version v0.28.0 | Program.cs `/api/about` | Decoded content shows `Version = "0.28.0"` | CONFIRMED (in decoded content) |
| P1-B: Reflection-based logger tests | `Sprint30Tests.cs` | Three logger invocation tests present | **CONFIRMED** (decoded content valid) |
| P1-C: ApiKeyMiddleware created and wired | `ApiKeyMiddleware.cs`, `Program.cs` | Middleware exists; wired via `app.UseWhen("/api")` | CONFIRMED (in decoded content) |
| P1-D: TrimEnd('s') removed | `ChatInterpreter.cs` | Sprint30Tests regression test verifies "grass" → "grass" not "gra" | CONFIRMED |
| P1-E: Bare 'doing' removed from status regex | `ChatInterpreter.cs` | Sprint30Tests test verifies | CONFIRMED |
| P2-B/D: HasFailed doc comments | `GenericGatherGoal.cs` | Commit 7da15ee6 | CONFIRMED |

### BLK-01 Confirmation

`BuildGoalDecomposer.cs` (plain C# at HEAD, SHA `be423cea`):
```csharp
public sealed class BuildGoalDecomposer(HtnTaskLibrary taskLibrary, ILogger<BuildGoalDecomposer> logger) : IGoalDecomposer
```
Constructor requires **2 parameters**: `taskLibrary` + `logger`.

`Program.cs` (decoded from base64 text at HEAD), `DecomposerRegistry` setup:
```csharp
reg.Register(new BuildGoalDecomposer(lib));  // ← BLK-01: only 1 argument
```

**BLK-01 is a confirmed compile error.**

### BLK-02: Possible Base64 File Encoding (NEW FINDING)

Multiple C# source files in the repo show base64-format content in their MCP resource text fields, while `BuildGoalDecomposer.cs` (committed cleanly in Sprint 28) shows plain C#. This pattern is consistent with files being stored as base64 text in the repo — the exact same problem that Sprint 28–30 were trying to fix.

**Affected files (base64-format text field):**
- `Agent.Tools/ToolDispatcher.cs`
- `WebUI.Blazor/Program.cs`
- `WebUI.Blazor/ApiKeyMiddleware.cs`
- `MemorySmith.Agent.Tests/Sprint30Tests.cs`

**IMPORTANT CAVEAT**: The GitHub MCP tool's content-decoding behavior is inconsistent and this diagnosis cannot be confirmed without running `dotnet build` in a live environment. The base64-format appearance may reflect MCP behavior rather than the actual stored file bytes. Sprint 31 **must** treat `dotnet build exit 0` as gate 1 and not assert it.

**Dissent (Archivist):** My confidence in BLK-02 is 72% (not 91% as above). The inconsistency between BuildGoalDecomposer.cs showing plain text vs. ToolDispatcher.cs showing base64 in the same MCP call is suspicious. However, the prior history of this branch (Sprint 28–30 repeatedly encountering base64-stored files via the GitHub MCP `create_or_update_file` action) makes it plausible that Sprint 30 commits re-encoded files that were being "decoded". **Sprint 31 should treat this as an investigation task, not an assumption.**

---

## Seat 2: Data Model Architect
**Role:** Assesses correctness of data structures, type safety, and architectural seams.  
**Confidence: 87%**

### Assessment of Audit Findings Against Architecture

**From `memorysmith_agent_deep_code_audit_sprint5.md` (new audit):**

| Finding | Status | Evidence |
|---|---|---|
| 1. ToolDispatcher has TODO, validation not implemented | **REFUTED** | Decoded `ToolDispatcher.cs` shows full `ValidateAgainstSchema` implementation with Sprint 5 comment. No TODO. |
| 2. WorldState mutable collections | **PARTIALLY STALE** | Sprint 25 P1-A fixed WorldModel aliasing. `WorldState.Facts` is still a `Dictionary<string,object?>` by design — intentional for agent state writes. |
| 3. Replanning loses failure context | **VALID — OPEN** | `HtnPlanner.ReplanAsync` accepts `failureReason` but does not thread it through. Confirmed in architecture. Sprint 26 P1-C created `CraftItemGoalDecomposer` but did NOT fix `ReplanAsync`. Deferred item D-7 from Sprint 19. |
| 4. Build origin silent fallback (0,0,0) | **PARTIALLY ADDRESSED** | Sprint 28 added `logger.LogWarning` on missing/unparseable fact (confirmed in BuildGoalDecomposer.cs source). Fallback to `(0,0,0)` still occurs. Audit recommendation (validation error, not silent fallback) is correct but deferred. |
| 5. Journal trim best-effort | **INTENTIONAL DESIGN** | Cross-verified in `deep-code-audit-20260619.md`: explicitly documented as bounded diagnostic buffer. |
| 6. Decomposer routing order-dependent | **VALID — BACKLOG** | `DecomposerRegistry.Find` returns first `CanHandle` match. No priority ordering. Low operational risk now (non-overlapping handlers), good hygiene item. |
| 7. Blueprint lookup too broad (contains match) | **VALID — BACKLOG** | `MemorySmithBlueprintRepository` uses containment check. Narrow scope when blueprint count grows. |

**From `memorysmith_audit_refinement_2026-06-20T133435Z.md` (new today):**

| Finding | Status | Evidence |
|---|---|---|
| 1. GoalFactory null returns (undifferentiated failure) | **VALID NEW FINDING** | `GoalFactory.CreateAsync` returns `null` for missing registries, missing items, malformed IDs — uses `Debug.WriteLine` not structured logging. Not in current backlog. |
| 2. HtnPlanner.ReplanAsync ignores failureReason | **CONFIRMED VALID** | Same as deep-audit Finding 3. Independently confirmed by two audits. |
| 3. Game error channel reads one per cycle | **VALID CONCERN** | AgentBackgroundService processes one `_gameErrors.Reader.TryRead` per dispatch cycle. Burst resilience concern. |
| 4. Correlation IDs not used for completion | **VALID CONCERN** | Sprint 25 P0-D added correlation IDs at dispatch, but completion/failure handlers in `ProcessEventsAsync` still match by tool name per architecture pattern review. |
| 5. Tests use real-clock polling | **VALID — KNOWN** | `ITimeProvider` exists; some service tests still use `Task.Delay` / wall clock. Sprint 23 D-8 deferred. |

---

## Seat 3: Retrieval Specialist
**Role:** Assesses memory, KB integration, and tool routing correctness.  
**Confidence: 84%**

### Memory Tool Routing (Sprint 23 P0-B)

`SearchMemoryTool` and `CreatePageTool` now accept `worldMemory` (world-keyed `IMemoryGateway`). `GetPageTool` retains `agentMemory`. Confirmed in Program.cs decoded content. This routing is correct.

### ITool Compliance (Sprint 30 P0-B)

Both `SearchMemoryTool` and `CreatePageTool` `ExecuteAsync` methods now accept `(JsonElement, CancellationToken)` — confirmed via Sprint30Tests structural reflection tests. The `InputSchema` property is present on both tools (confirmed via `HasInputSchemaPropery` tests).

### WorldKbUrl Null Startup Warning (Sprint 23 P1-A)

Present in decoded Program.cs: startup `LogWarning` when `WorldKbUrl` is null/whitespace. Confirmed.

### No New Retrieval Issues Found

No new finding from the audits affects memory/retrieval architecture directly. The GoalFactory null-return finding (Seat 2) is adjacent but does not affect the memory layer.

---

## Seat 4: Human Learning Advocate
**Role:** Assesses runtime observability, operator UX, and failure visibility.  
**Confidence: 85%**

### Observability Gaps — Confirmed

The refinement audit correctly identifies a class of observability failures where the agent does something internally but the operator has no visibility:

1. **GoalFactory null** — operator sees "could not create goal" without knowing if the goal type is unknown, the item registry is missing, or the item is unrecognized. Three different failure modes collapse into one null return.
2. **ReplanAsync null** — replanning silently fails; the background loop just retries. No journal event, no failure reason logged.
3. **Correlation ID completion matching** — when duplicate tools are dispatched, completion events may be attributed to the wrong action. The operator sees "action completed" but it may be the wrong one.

### ApiKeyMiddleware Observability

`ApiKeyMiddleware` logs a `LogWarning` on authentication failure (confirmed in decoded source). The log includes `Method` and `Path`. This is adequate for operator use. However, there is **no test for the rejection path** — the council considers this P1 (Sprint 31 P1-2).

### Build Origin (0,0,0) Fallback

`LogWarning` was added in Sprint 28 (confirmed in source). For the operator, the warning reads: "Build origin fact missing or unparseable; defaulting to (0,0,0). Goal may build at wrong location." This is visible in logs. The audit recommendation to make it a hard failure is architecturally valid but changes agent behavior — keep as deferred, not blocking.

---

## Seat 5: Skeptical Reviewer
**Role:** Challenges all claims, identifies inconsistencies.  
**Confidence: 79%**

### Challenge 1: Sprint 30 Handoff Over-Claims "Confirmed"

The handoff says: "P0-A | ToolDispatcher.cs | c34bd3d8 | Confirmed — decoded from base64, valid C# committed"

But the MCP source inspection shows ToolDispatcher.cs content appears base64-encoded. This is the same pattern that has misled prior sprint agents. The "Confirmed" label is questionable — it was confirmed by reading the commit message, not by running `dotnet build`.

**This is a systemic process failure**: every sprint from 26–30 has had "confirmed" deliveries that were later found to be non-compiling. The root cause is agents asserting build correctness from commit messages rather than running the build.

### Challenge 2: Sprint 30 Council Gave "Conditional Pass" But Didn't Block

Sprint 30 council gave 76.4% avg confidence and flagged BLK-01. The council correctly identified the problem but allowed the sprint to close with "deferred to Sprint 31". The implication is that BLK-01 is NEW in Sprint 31. But BLK-01 was latent since Sprint 28 (when the logger param was added to BuildGoalDecomposer). The council in Sprint 28 may have introduced this bug without realizing it.

### Challenge 3: Test Count Inflation Possible

The handoff claims ~271+ tests. Sprint 30 added 11 new tests. Sprint30Tests.cs is potentially stored as base64 (if BLK-02 is confirmed), which means those 11 tests DO NOT COMPILE and the actual test count has not increased since Sprint 29. Sprint 31 must report an accurate count post-build.

### Challenge 4: Audit Finding on ToolDispatcher TODO Is Stale

`memorysmith_agent_deep_code_audit_sprint5.md` claims: "actual dispatch path still forwards JSON directly to the tool implementation and contains a TODO where validation should occur." This is refuted by the decoded ToolDispatcher.cs which shows complete schema validation with a clear Sprint 5 comment. The audit was written against an earlier state of the branch and this finding should be marked CLOSED in the backlog.

### Challenge 5: Audit Finding on Correlation IDs Needs Verification

The refinement audit claims completion still uses tool name matching despite correlation IDs being added. This is plausible but requires reading `AgentBackgroundService.ProcessEventsAsync` directly. I flag this as needing confirmation in Sprint 31 rather than treating it as established.

---

## Seat 6: Synthesizer
**Role:** Integrates all seats into verdict, triage, and acceptance criteria.  
**Confidence: 86%**

### Overall Verdict: CONDITIONAL PASS — Sprint 31 remains blocked by BLK-01

Sprint 30 delivered real value (ITool compliance, middleware, chat fixes, logger tests, doc comments). The core problem is that build correctness has never been independently verified in any sprint on this branch. BLK-01 is confirmed. BLK-02 (additional base64 files) is plausible but uncertain.

### Blocking Findings (must resolve before any Sprint 32 scope)

| ID | Finding | Confidence | Evidence |
|---|---|---|---|
| BLK-01 | Program.cs calls `new BuildGoalDecomposer(lib)` — 1 arg; constructor requires 2 (`lib` + `ILogger`) | 97% | Source: BuildGoalDecomposer.cs 2-param constructor confirmed; decoded Program.cs shows 1-arg call |
| BLK-02 | Multiple C# source files may be stored as base64 text in repo; build state unverified in any sprint | 72% | Pattern from Sprint 28–30 history + MCP text field inspection; requires live `dotnet build` to confirm |

### Deferred Findings (not blocking, but tracked)

| ID | Finding | Source Audit | Sprint Priority |
|---|---|---|---|
| DEF-A | GoalFactory null returns for all failure modes | refinement-audit (new) | Sprint 31 P2 |
| DEF-B | HtnPlanner.ReplanAsync ignores failureReason | deep-audit Sprint5 + refinement-audit (convergent) | Sprint 31 P2 |
| DEF-C | Game error channel reads 1 per cycle (burst gap) | refinement-audit (new) | Sprint 32 backlog |
| DEF-D | Correlation ID not used for completion matching | refinement-audit (new) | Sprint 31 P2 (verify first) |
| DEF-E | Build origin (0,0,0) fallback — should be validation error | deep-audit Sprint5 | Sprint 32 backlog |
| DEF-F | Decomposer registry order-dependent | deep-audit Sprint5 | Backlog |
| DEF-G | Blueprint lookup too broad (contains match) | deep-audit Sprint5 | Backlog |
| DEF-H | Tests still use real-clock polling in some service tests | refinement-audit (new) | Sprint 32 backlog |

### CLOSED / REFUTED Findings (from new audits)

| ID | Claim | Verdict | Evidence |
|---|---|---|---|
| CLOSED-1 | ToolDispatcher has TODO, no schema validation | **REFUTED** | Decoded ToolDispatcher.cs has complete `ValidateAgainstSchema` + `CheckType` using `TryGetInt32`; Sprint 5 comment present |
| CLOSED-2 | WorldModel state aliasing unfixed | **STALE** | Sprint 25 P1-A fixed. `Observe()` deep-copies inventory |
| CLOSED-3 | ToolDispatcher no exception wrapping | **STALE** | Sprint 25 P0-C added try/catch around `ExecuteAsync`; `OperationCanceledException` re-throws |
| CLOSED-4 | CI failure on PR head (Sprint 25 CAS bug) | **STALE** | Fixed in Sprint 25 mid-sprint |
| CLOSED-5 | Journal trim best-effort is a risk | **CLOSED AS INTENTIONAL** | Documented design decision; bounded diagnostic buffer |

### Acceptance Criteria for Sprint 31 "Done"

**Gate 1 — Build (blocks all other gates)**
- [ ] `dotnet build --configuration Release` exits 0 in a live environment
- [ ] Build output (stdout/stderr) attached to Sprint 32 handoff document
- [ ] Zero new compile errors beyond BLK-01 (if BLK-02 confirmed, all base64 files decoded first)

**Gate 2 — Tests**
- [ ] `dotnet test --no-build --configuration Release` exits 0
- [ ] All Sprint30Tests.cs tests pass (including structural and reflection tests)
- [ ] Test count reported accurately (not asserted from commit messages)

**Gate 3 — Security**
- [ ] SEC-02 (port 5050 shared secret) implemented and tested
- [ ] ApiKeyMiddleware rejection path tests: at least 2 (missing key → 401; invalid key → 401)

**Gate 4 — Quality**
- [ ] `WorldState.SetFact` marked `[Obsolete]` with named replacement
- [ ] Sprint28Tests.cs fixture comment added
- [ ] `Register(string, ITool)` collision semantics XML doc added

**Gate 5 — CI**
- [ ] `.github/workflows/ci.yml` exists, triggers on branch, is passing green

**Gate 6 — Council**
- [ ] 6-seat council session conducted
- [ ] No BLOCKING findings open at close
- [ ] Average confidence >= 75%
- [ ] Sprint 32 handoff published

---

## Summary: What Changed in This Review

1. **BLK-01 independently confirmed** (not just accepted from handoff).
2. **BLK-02 raised as plausible new finding** — multiple Sprint 30 commits may have stored C# files as base64 text. Must be resolved by running live build, not by assertion.
3. **deep-code-audit-sprint5 Finding 1 (TODO in ToolDispatcher) REFUTED** — the decoded source has complete schema validation. This finding was based on a pre-Sprint-5 branch state and should be removed from active consideration.
4. **Three NEW deferred findings** from the refinement audit: GoalFactory null returns (DEF-A), game error burst gap (DEF-C), correlation ID completion (DEF-D).
5. **HtnPlanner.ReplanAsync ignores failureReason (DEF-B)** confirmed by two independent audits — escalated from deferred to Sprint 31 P2 scope.

*Council session closed 2026-06-20. Next council: after Sprint 31 Gate 1 (build passing) is confirmed.*
