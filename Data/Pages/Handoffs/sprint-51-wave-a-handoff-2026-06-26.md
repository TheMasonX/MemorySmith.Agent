# Sprint 51 Handoff — Wave A: Canonicalize, Classify & Harden

**Date:** 2026-06-26
**Branch:** `sprint-35-llm-first`
**Author:** Council (10-reviewer project planning council)
**For:** Next agent / developer beginning Sprint 51 implementation
**Tests:** 731 passing, 0 failing (0 warnings, 0 errors)
**Version:** v0.50.2

---

## Sprint 51 Theme

> **"Canonicalize & Classify"** — Define one pipeline. Classify every legacy path. Execute high-ROI fixes. Zero runtime risk on the primary track. Critical hardening on the parallel track.

After 15 consecutive feature sprints (Sprint 35–50), the codebase needs consolidation: documentation is contradictory, compatibility bridges are undocumented, silent failure paths remain, and the memory gateway is fragile. Sprint 51 addresses these before Sprint 52 begins monolith extraction.

---

## Current State

| Metric | Value |
|:-------|:------|
| Build | 0 warnings, 0 errors |
| Tests | 731 passing, 0 failing |
| Version | v0.50.2 |
| Branch | `sprint-35-llm-first` |
| Last sprint | Sprint 50 (4 waves: A, B, C, D) |
| Ready tasks | 19 (9 Critical, 7 High, 3 Medium) |
| InProgress tasks | 3 (TSK-0004, TSK-0014, TSK-0081) |
| Council reports | 4 documents in `Data/Pages/council/` |

---

## Task Inventory — Sprint 51 Wave A

### Track 1: CANONICALIZE & CLASSIFY (Primary — zero runtime risk)

These tasks are documentation, classification, and small mechanical fixes. They can be done in any order and have no code risk.

#### Execution Order (recommended)

| Seq | TSK ID | Priority | Task | Files | Est. | Depends On |
|:---:|:------:|:--------:|:-----|:------|:----:|:----------:|
| **1** | TSK-0124 | Critical | **Classify all compatibility bridges** — label every fallback/backward-compat path as permanent/temporary/obsolete with owner, purpose, replacement, removal criteria, target sprint. Document in `architecture.md`. | `Data/Pages/architecture.md` (write only) | 45 min | — |
| **2** | TSK-0125 | Critical | **Align docs to single canonical pipeline** — create Mermaid diagram of actual S50 runtime flow; update `architecture.md` Runtime Flow section; sync `AGENTS.md`; add ADR D-003 note; link from README. | `Data/Pages/architecture.md`, `AGENTS.md`, `README.md` | 45 min | — |
| **3** | TSK-0126 | High | **Fix lying comment + resolve `_agentRuntime`** — update `GatherItemDecompose` comment (~line 950 of `HtnTaskLibrary.cs`) to match actual code; either integrate `_agentRuntime` or remove dead code from `AgentBackgroundService.cs`. | `HtnTaskLibrary.cs`, `AgentBackgroundService.cs` | 20 min | — |
| **4** | TSK-0127 | High | **Remove `ChatInterpretation.GoalName`** — deferred since Sprint 38. Update `ChatInterpreterTests` + `Sprint21Tests`. Verify CRITICAL Rule A-1 (Parsers Never Create Goals) intact. | `ChatInterpretation.cs`, `ChatInterpreterTests.cs`, `Sprint21Tests.cs` | 30 min | — |
| **5** | TSK-0128 | High | **Fix SearchMemoryTool regex fragility** — scan ALL results (not just `FirstOrDefault`); replace `int.Parse` with `int.TryParse`; fix `CoordLabelsPattern` to use distinct named groups `x`/`y`/`z`. | `SearchMemoryTool.cs`, `Sprint51Tests.cs` | 30 min | — |
| **6** | TSK-0129 | Critical | **Add `SearchResult.Kind` field** (page vs memory disambiguation). Update all consumers to check `Kind` before calling `GetPageAsync`. | `ActionData.cs` (SearchResult record), `RestMemoryGateway.cs`, all SearchResult consumers | 30 min | — |
| **7** | TSK-0130 | Critical | **Adopt deprecation policy + semantic versioning** — write deprecation policy; create `BREAKING_CHANGES.md`; adopt semver for API surface; update version to v0.51.0. | New: `BREAKING_CHANGES.md`, `AGENTS.md`; update `README.md`, `about.html` | 30 min | — |
| **8** | TSK-0134 | High | **Add DI startup failure logging + health check endpoints** — `/api/agent/health` returning DI status, gateway reachability, adapter connection state. | `Program.cs`, `AgentBackgroundService.cs` | 30 min | — |
| **9** | TSK-0131 | High | **Adopt `IHttpClientFactory` in `RestMemoryGateway`** — replace `new HttpClient()` with factory; add retry + circuit breaker via Polly or `Microsoft.Extensions.Http.Resilience`. | `RestMemoryGateway.cs`, `Program.cs` | 30 min | — |
| **10** | TSK-0133 | High | **Fix parameter preservation on replan** — compute remaining count (targetCount - currentInventory) and pass to new goal instance. | `AgentBackgroundService.cs` (replan section), goal classes | 45 min | — |
| **11** | TSK-0136 | High | **Document all breaking changes with migration guide** — catalog breaking changes with before/after; create per-tool migration paths. | New/update: `BREAKING_CHANGES.md`, wiki pages | 30 min | Depends on TSK-0130 |
| **12** | TSK-0138 | High | **Fix `UpdatePageAsync` title bug + harden slug generation** — title must be explicit parameter; `ToSlug` handles special chars, collisions. | `RestMemoryGateway.cs` | 30 min | — |
| **13** | TSK-0132 | High | **Fix page search Score=0.0 under-ranking** — investigate why pages get zero scores; normalize or fix server-side. | `RestMemoryGateway.cs`, possibly MemorySmith server | 30 min | — |
| **14** | TSK-0135 | Medium | **Fix `HtnTaskLibrary.MakeAction` Arguments dictionary mutability** — freeze dict after construction or clone before mutation. | `HtnTaskLibrary.cs` | 20 min | — |
| **15** | TSK-0137 | Medium | **Fix consecutive failure guard** — reset on partial progress, not just full success. | `AgentBackgroundService.cs` | 20 min | — |

---

### Track 2: HARDEN ROBUSTNESS (Parallel — code changes)

These are the critical gaps discovered by the 10-reviewer adversarial council. They require code changes and address silent failures, death recovery, and debugging.

#### Execution Order

| Seq | TSK ID | Priority | Task | Files | Est. | Reviewer Source |
|:---:|:------:|:--------:|:-----|:------|:----:|:----------------|
| **H1** | TSK-0139 | Critical | **Fix `Task.WhenAll` exception masking** — unwrap `AggregateException.InnerExceptions` in catch block at `AgentBackgroundService.cs:482`. Log each individually. | `AgentBackgroundService.cs` | 15 min | Error Handling Auditor (F-WHENALL) |
| **H2** | TSK-0140 | Critical | **Add `DeathEvent` handler** — `case DeathEvent:` in `ProcessEventsAsync` switch: cancel goal, clear queue, clear correlated actions, mark inventory stale, set goal to null. | `AgentBackgroundService.cs` (~line 755 switch) | 30 min | Plan Recovery Specialist (F-DEATH) |
| **H3** | TSK-0141 | Critical | **Fix `MonitorAndCancelOnFaultAsync` bare catch** — add `logger.LogError(ex, "...")` before `cts.Cancel(); throw;` at line 507. | `AgentBackgroundService.cs` | 5 min | Error Handling Auditor (F-BARECATCH) |
| **H4** | TSK-0142 | Critical | **Fix logging levels** — change SQLite sink from `Warning` to `Information` in `appsettings.json`; remove `Agent.Planning: Warning` and `Agent.Planning.Llm: Warning` overrides. | `appsettings.json` | 10 min | Debugging Advocate (F-SQLITE-LEVEL) |
| **H5** | TSK-0143 | Critical | **Add terminal recovery after `maxConsecutiveFailures`** — don't idle forever. After goal abandonment, enqueue fallback (wander/explore). At minimum: log `LogWarning` that agent is idling. | `AgentBackgroundService.cs` (~line 1226-1240) | 30 min | Plan Recovery Specialist (F-TERMINAL) |

---

### Track 3: IN-PROGRESS COMPLETION (finish S50 partial deliveries)

These tasks were started in Sprint 50 Wave D and need completion.

| TSK ID | Priority | Current State | Remaining Work | Est. |
|:------:|:--------:|:-------------|:---------------|:----:|
| TSK-0004 | High | MoveToTool context wiring partially done in S50 Wave D | Per-tool allowlist; SearchMemory coordinate hints | 30 min |
| TSK-0014 | Medium | Serilog SQLite sink partially done in S50 Wave D | NU1903 cleanup; startup failure isolation; retention policy | 20 min |
| TSK-0081 | Critical → **Medium** | Unit tests for Sprint 42/43 checkpoint changes — **council recommends downgrading from Critical to Medium** | Tests for 7-sprint-old feature; low marginal value | 30 min (or close) |

---

## Key Context for the Next Agent

### 1. What NOT to do

- **Do NOT remove deterministic fast-paths** for cancel/status/help/navigate. These are permanent per ADR D-003 and CRITICAL Rule A-1 in `AGENTS.md`.
- **Do NOT begin `AgentBackgroundService` extraction** (Sprint 52 work). Bridge classification (TSK-0124) must complete first.
- **Do NOT change pipeline behavior.** Sprint 51 is documentation + small fixes. Zero behavioral changes to the planning pipeline.
- **Do NOT touch the Node.js adapter** for anything beyond the logging fixes identified (TSK-0141 related JS fixes are optional S51b).

### 2. Critical AGENTS.md Rules to Follow

- **Rule A-1:** Parsers Never Create Goals. `IChatInterpreter.InterpretAsync` returns `ChatInterpretation` (no GoalName). IntentManager maps to GoalRequest.
- **Rule E-1:** Never patch C# verbatim-string files via agent text tools. Use paramsFile via GitHub API.
- **Rule E-2:** Pass plain text to `github__create_or_update_file`, not base64.
- **Rule E-3:** Every `catch` block that does not rethrow MUST log at `LogWarning` or higher. Every `switch` on discriminated union MUST have `default:` that logs unhandled type.
- **No Magic Numbers:** All timeouts, TTLs, retry counts must use named constants or configurable options.

### 3. Files Most Likely to Have Merge Conflicts

These files were changed in Sprint 50 and will be touched by Sprint 51:
- `AgentBackgroundService.cs` — touched by TSK-0126, TSK-0133, TSK-0137, TSK-0139, TSK-0140, TSK-0141, TSK-0143
- `appsettings.json` — touched by TSK-0142
- `HtnTaskLibrary.cs` — touched by TSK-0126, TSK-0135
- `RestMemoryGateway.cs` — touched by TSK-0129, TSK-0131, TSK-0138
- `Data/Pages/architecture.md` — touched by TSK-0124, TSK-0125
- `AGENTS.md` — touched by TSK-0125, TSK-0130

**Recommendation:** Do `AgentBackgroundService.cs` changes LAST in Track 2, after all other changes are stable. It's the most conflict-prone file.

### 4. Verification Gates

After EVERY task or batch of tasks:
```bash
dotnet build                     # Must be 0 warnings, 0 errors
dotnet test                      # All 731+ tests must pass
```

After ALL tasks complete:
```bash
dotnet build                     # 0w/0e
dotnet test                      # ~740+ tests pass (new tests added for TSK-0127, TSK-0128, TSK-0129)
pwsh ./Scripts/Validate-Repo.ps1 # Full validation
```

### 5. Known Gotchas

| Gotcha | Detail |
|:-------|:-------|
| **TSK-0127 (`GoalName` removal)** | This field has been deferred since Sprint 38. `ChatInterpreterTests` and `Sprint21Tests` reference it. These tests MUST be updated before the field can be removed. The AGENTS.md PRINCIPLE-1 explicitly calls this out. |
| **TSK-0129 (`SearchResult.Kind`)** | This is a **breaking change** to the `SearchResult` record if you add a required field. Make `Kind` optional with default `"page"` for backward compatibility. MemorySmith server must also add `Kind` to its search response (cross-repo coordination). |
| **TSK-0131 (`IHttpClientFactory`)** | `RestMemoryGateway` is currently instantiated with `new HttpClient()`. Changing to `IHttpClientFactory` requires DI registration changes in `Program.cs`. Keep the old constructor as a fallback during migration. |
| **TSK-0140 (`DeathEvent` handler)** | The `DeathEvent` type exists in `WorldEvents.cs` and the JS adapter emits it. The handler just needs to be added to the switch. Make sure `WorldState.IsInventoryStale` is set to `true`. |
| **TSK-0142 (logging levels)** | Removing `Agent.Planning*` overrides will cause ALL `LogInformation` from planner/LLM to appear. This is intentional — the SQLite sink captures it. If console noise is a concern, redirect planner logs to file/SQLite only. |
| **TSK-0143 (terminal recovery)** | The fallback goal after abandonment should be simple: `Wander` with no target. Don't auto-restart the failed goal — that would create a loop. Use `_lastAbandonedGoalName` guard to prevent re-queuing the same failed goal. |

### 6. What's Deferred to Sprint 52-53

These are explicitly OUT OF SCOPE for Sprint 51:

| Deferred Item | Target Sprint | Reason |
|:--------------|:------------:|:-------|
| Extract `AgentBackgroundService` collaborators | S52 | Requires bridge classification first |
| Split `HtnTaskLibrary` into per-domain decomposers | S52 | Requires pipeline canonicalization |
| Add SearchMemory→MoveTo gather routing | S52 | Design needed first |
| Introduce typed `PlanContext` (Phase 1) | S52 | Safe after bridge classification |
| Structured memory metadata (MemorySmith API) | S53 | Cross-repo coordination |
| Integration tests (host-level context-carry) | S53 | Requires S52 extraction |
| Delete obsolete bridges | S53 | Gated by integration tests |
| Dashboard event bus (TSK-0042–0050) | S54+ | Council decision: foundation first |
| OpenTelemetry, SARIF, SCA | S54+ | Council decision: observability after architecture |
| Mob/hunger combat handling | S54+ | Large feature; food consumption may be pulled into S53 |

### 7. New Tasks Still Needing Creation (Wave B work)

The 10-reviewer council identified additional tasks that need task records created. These can be done in Wave B:

| Proposed Task | Priority | Description | Source |
|:-------------|:--------:|:------------|:-------|
| TSK-0144 | High | Fix WebSocket JSON parse error propagation — send error event from JS to C# | Error Handling F-WS-JSON |
| TSK-0145 | High | Fix `sendEvent` silent drop — log warning when socket not open in JS adapter | Error Handling F-SENDEVENT |
| TSK-0146 | High | Add `bot.on('end')` handler in JS adapter for server restart reconnection | Robustness F-RECONNECT |
| TSK-0147 | High | Add flee/dodge on damage interrupt — move away from damage source | Plan Recovery F-FLEE |
| TSK-0148 | High | Add trace ID linking chat→intent→plan→completion | Debugging F-CORRELATION |
| TSK-0149 | High | Add latency/response logging to OpenAICompatibleProvider (match OllamaProvider) | Debugging F-OPENAI-NOLOG |
| TSK-0150 | High | Add mine loop max elapsed time bound in JS adapter | Robustness F-MINE-LOOP |
| TSK-0151 | Medium | Replace `AllowUnauthenticatedApi` boolean with loopback-only bypass | Security F-AUTH-BOOL |
| TSK-0152 | Medium | Add `TryParse` audit sweep for all `int.Parse`/`double.Parse` in hot paths | Code Quality |
| TSK-0153 | Medium | Add food consumption when `bot.food < 14` in JS adapter | Robustness Q6 |
| TSK-0154 | Medium | Add `bot.on('entityHurt')` for damage attribution logging | Robustness Q6 |

---

## Council Documents Reference

All planning context is in these files:

| Document | Location | Content |
|:---------|:---------|:--------|
| **Original Council** | `Data/Pages/council/audit-findings-consolidation-council-2026-06-26.md` | 6-seat review of markdown audits; DA-001 through DA-008 |
| **Addendum** | `Data/Pages/council/audit-findings-consolidation-addendum-2026-06-26.md` | Deep-dive of 4 research .docx reports; ~160 findings |
| **Project Plan** | `Data/Pages/council/sprint-51-project-plan-council-2026-06-26.md` | 10-reviewer adversarial review; Sprint 51-53 roadmap |
| **This Handoff** | `Data/Pages/Handoffs/sprint-51-wave-a-handoff-2026-06-26.md` | Implementation instructions for next agent |

### Research Reports (source material)

| Report | Location |
|:-------|:---------|
| Deep Code Audit (Sprint 35) | `Data/Pages/Audit/Research/research-MemorySmith.Agent Sprint-35 Code Audit.txt` |
| Codebase Audit (Sprint 35) | `Data/Pages/Audit/Research/research-MemorySmith.Agent Codebase Audit Sprint-35.txt` |
| Delta Audit (d2ef16ab) | `Data/Pages/Audit/Research/research-MemorySmith.Agent Delta Audit Sprint-35 Commit d2ef16ab.txt` |
| Commit Delta Audit | `Data/Pages/Audit/Research/research-MemorySmith.Agent Commit Delta Audit.txt` |

---

## Quick-Start Checklist

For the next agent beginning implementation:

- [ ] Read this handoff document
- [ ] Run `dotnet build` and `dotnet test` to verify baseline (0w/0e, 731+ tests)
- [ ] Read `AGENTS.md` — especially CRITICAL Rules A-1, E-3
- [ ] Start Track 1 tasks in order (TSK-0124 → TSK-0125 → ...)
- [ ] Run `dotnet build && dotnet test` after each task
- [ ] When Track 1 is done, start Track 2 (TSK-0139 → TSK-0143)
- [ ] Complete Track 3 (TSK-0004, TSK-0014, TSK-0081)
- [ ] Update version to v0.51.0
- [ ] Write Sprint 51 complete handoff
- [ ] Commit and push

---

**Council Confidence in This Plan: 88%**
