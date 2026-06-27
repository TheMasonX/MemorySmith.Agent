# Council Review: Sprint 51 Project Plan — Consolidation & Cleanup

**Date:** 2026-06-26
**Council Type:** 10-adversarial-reviewer project planning council (user-authorized subagents)
**Baseline:** v0.50.2, 731 tests, 0w/0e, `sprint-35-llm-first`

---

## Decision

**Adopt Sprint 51 "Canonicalize & Classify" as a zero-runtime-risk consolidation sprint that aligns all documentation to one pipeline, classifies every compatibility bridge, fixes the highest-ROI small defects, and unblocks Sprint 52 monolith extraction — with a parallel "Sprint 51b" hardening track for the critical robustness gaps discovered during adversarial review.**

---

## Evidence Reviewed

### Dataset
- 135+ tasks across MemorySmith task system (Ready: 15, InProgress: 4, Backlog: ~55, Done: ~60)
- 6 audit documents (2 markdown + 4 research .docx)
- 2 council reports + 1 addendum
- Sprint 50 handoff document
- 10 adversarial review reports covering: architecture, error handling, plan recovery, agent robustness, debugging/observability, security, task system health, and roadmap synthesis

### Source Files Verified
- `AgentBackgroundService.cs` (full 2300-line review)
- `HtnTaskLibrary.cs`, `HtnPlanner.cs`, `GatherGoalDecomposer.cs`
- `ToolDispatcher.cs`, `SearchMemoryTool.cs`, `MoveToTool.cs`
- `RestMemoryGateway.cs`, `LlmChatInterpreter.cs`, `ChatInterpreter.cs`
- `MineflayerAdapter/index.js` (full review)
- `Program.cs`, `appsettings.json`
- `architecture.md`, `AGENTS.md`, `roadmap.md`

---

## 10-Reviewer Findings Summary

| Reviewer | Top Finding | Severity | Action |
|:---------|:------------|:---------|:-------|
| **Architectural Integrity** | `architecture.md` contradicts actual LLM-first pipeline; `_agentRuntime` injected but unused — decomposition STALLED | **Critical** | TSK-0125 (docs), TSK-0126 (_agentRuntime) |
| **Error Handling** | `Task.WhenAll` masks multi-exception context; `MonitorAndCancelOnFaultAsync` bare catch swallows cause; WebSocket JSON parse errors not propagated to C#; `sendEvent` silently drops events when socket not open | **Critical** | New tasks: TSK-0139, TSK-0140, TSK-0141, TSK-0142 |
| **Plan Recovery** | DeathEvent has NO handler → stale inventory after respawn; no SearchMemory routing in gather; no flee on damage; `maxConsecutiveFailures=3` → agent idles forever with no terminal recovery | **Critical** | New tasks: TSK-0143, TSK-0144, TSK-0145, TSK-0146 |
| **Agent Robustness** | Bot never reconnects after server restart (Node.js `bot` entity dies but process survives); no mob/hunger/night handling; mine `while` loop has no max time bound; inventory-full detection missing in JS adapter | **Critical** | New tasks: TSK-0147, TSK-0148, TSK-0149, TSK-0150 |
| **Debugging/Observability** | SQLite sink at Warning+ swallows all diagnostic data; `Agent.Planning*` overrides suppress all planner/LLM Info/Debug logs; correlation IDs decorative only (never used for matching); OpenAI provider has zero latency logging; no trace from chat→intent→plan→completion | **Critical** | New tasks: TSK-0151, TSK-0152, TSK-0153 |
| **Security** | `AllowUnauthenticatedApi=true` opens all agent endpoints to world with no IP restriction; HTTP header `X-Api-Key` could leak at Debug log level; Node.js subprocess has no resource limits | **High** | TSK-0134 covers health endpoints; New: TSK-0154 |
| **Task System** | 6 tasks need closing (superseded/done/stale); 5 need reprioritization; 3 pairs need deduplication; 4 have non-standard status strings | **Medium** | Task curation pass |
| **Synthesizer** | Sprint 51 theme: CANONICALIZE & CLASSIFY. Top 10 tasks identified. Sprint 51b: HARDEN ROBUSTNESS for the critical gaps 10 reviewers found | **N/A** | This document |

---

## CRITICAL NEW FINDINGS (Not in Previous Audits)

The 10 adversarial reviewers discovered **14 new critical/high-severity issues** that none of the 6 audit documents had identified:

### 🔴 P0 — Agent Can Die and Stay Dead

| ID | Finding | Reviewer |
|:---|:--------|:---------|
| **F-DEATH** | DeathEvent has NO handler in ProcessEventsAsync → inventory stays stale after respawn, goal silently fails | Plan Recovery |
| **F-RECONNECT** | Node.js adapter never recreates `bot` entity after server restart — C# reconnects to WebSocket but bot is dead | Robustness |
| **F-TERMINAL** | `maxConsecutiveFailures=3` → agent idles forever with no fallback goal | Plan Recovery |
| **F-FLEE** | Damage interrupt only stops + GetStatus — bot doesn't move away from damage source | Plan Recovery |

### 🔴 P0 — Errors Silently Lost

| ID | Finding | Reviewer |
|:---|:--------|:---------|
| **F-WHENALL** | `Task.WhenAll` wraps multiple exceptions into AggregateException — only first logged | Error Handling |
| **F-BARECATCH** | `MonitorAndCancelOnFaultAsync` bare catch swallows exception context before rethrow | Error Handling |
| **F-WS-JSON** | Malformed WebSocket JSON silently dropped — no error event sent to C# | Error Handling |
| **F-SENDEVENT** | `sendEvent` silently drops events (including blockMined, mineComplete) when socket not open | Error Handling |

### 🔴 P0 — Debugging Impossible in Production

| ID | Finding | Reviewer |
|:---|:--------|:---------|
| **F-SQLITE-LEVEL** | SQLite sink at Warning+ swallows ALL diagnostic data (goal set/completed, plan actions, dispatch correlation, LLM latency, chat flow) | Debugging |
| **F-PLANNING-OVERRIDE** | `Agent.Planning: Warning` and `Agent.Planning.Llm: Warning` overrides suppress ALL planner and LLM logs across all sinks | Debugging |
| **F-CORRELATION** | Correlation IDs are generated but never used for event matching — `CompleteCorrelatedActionByTool` matches by tool name only | Debugging |
| **F-OPENAI-NOLOG** | OpenAICompatibleProvider has zero latency/response logging (unlike OllamaProvider which has excellent instrumentation) | Debugging |

### 🟠 HIGH

| ID | Finding | Reviewer |
|:---|:--------|:---------|
| **F-MOB-HUNGER** | Zero mob/hunger/night handling — bot will starve and die to mobs on survival | Robustness |
| **F-AUTH-BOOL** | `AllowUnauthenticatedApi=true` opens all agent endpoints to world with no IP restriction | Security |

---

## Sprint 51 Plan — Primary Track: CANONICALIZE & CLASSIFY

### Top 10 Tasks (zero runtime risk, ~5 hours total)

| # | TSK | Priority | Task | Est. |
|:--:|:---:|:--------:|:-----|:----:|
| 1 | TSK-0124 | Critical | **Classify all compatibility bridges** — permanent/temporary/obsolete with owner, purpose, replacement, removal criteria | 45 min |
| 2 | TSK-0125 | Critical | **Align docs to single canonical pipeline** — Mermaid diagram + architecture.md update + AGENTS.md sync | 45 min |
| 3 | TSK-0128 | High | **Fix SearchMemoryTool regex fragility** — TryParse, scan all results, fix group names | 30 min |
| 4 | TSK-0127 | High | **Remove ChatInterpretation.GoalName** — deferred since Sprint 38 | 30 min |
| 5 | TSK-0126 | High | **Fix lying comment + resolve _agentRuntime** | 20 min |
| 6 | TSK-0129 | Critical | **Add SearchResult.Kind field** (page vs memory disambiguation) | 30 min |
| 7 | TSK-0130 | Critical | **Adopt deprecation policy + semantic versioning** | 30 min |
| 8 | TSK-0131 | High | **Adopt IHttpClientFactory** with Polly resilience | 30 min |
| 9 | TSK-0082 | High | **Extract SmeltableMapping** shared class | 20 min |
| 10 | TSK-0134 | High | **Add DI startup failure logging + health check endpoints** | 30 min |

**Gate:** `dotnet build` 0w/0e, 731+ tests pass, one pipeline documented, all bridges classified.

---

## Sprint 51b Plan — Parallel Track: HARDEN ROBUSTNESS

These are the critical gaps discovered by adversarial review. They require code changes and should run in parallel with the documentation/classification track.

### Critical Robustness Fixes

| # | New TSK | Priority | Task | Reviewer Source |
|:--:|:-------:|:--------:|:-----|:----------------|
| 1 | TSK-0139 | **Critical** | Fix `Task.WhenAll` exception masking — unwrap AggregateException.InnerExceptions | Error Handling F-WHENALL |
| 2 | TSK-0140 | **Critical** | Fix `MonitorAndCancelOnFaultAsync` bare catch — add LogError before rethrow | Error Handling F-BARECATCH |
| 3 | TSK-0141 | **Critical** | Fix WebSocket JSON parse error propagation — send error event to C# | Error Handling F-WS-JSON |
| 4 | TSK-0142 | **Critical** | Fix `sendEvent` silent drop — log warning when socket not open | Error Handling F-SENDEVENT |
| 5 | TSK-0143 | **Critical** | Add DeathEvent handler — cancel goal, clear queue, mark inventory stale | Plan Recovery F-DEATH |
| 6 | TSK-0144 | **Critical** | Add terminal recovery after maxConsecutiveFailures — fallback to wander/explore | Plan Recovery F-TERMINAL |
| 7 | TSK-0145 | **High** | Add flee/dodge on damage interrupt — move away from damage source | Plan Recovery F-FLEE |
| 8 | TSK-0146 | **High** | Add `bot.on('end')` handler for server restart reconnection | Robustness F-RECONNECT |

### Critical Debugging Fixes

| # | New TSK | Priority | Task | Reviewer Source |
|:--:|:-------:|:--------:|:-----|:----------------|
| 9 | TSK-0147 | **Critical** | Fix logging levels — SQLite to Information, remove `Agent.Planning*` overrides | Debugging F-SQLITE-LEVEL |
| 10 | TSK-0148 | **Critical** | Add trace ID linking chat→intent→plan→completion | Debugging F-CORRELATION |
| 11 | TSK-0149 | **High** | Add latency/response logging to OpenAICompatibleProvider (match OllamaProvider) | Debugging F-OPENAI-NOLOG |
| 12 | TSK-0150 | **High** | Add mine loop max elapsed time bound in JS adapter | Robustness F-MINE-LOOP |

---

## Task System Curation Actions

Per the Task System Optimizer review, the following actions are needed:

### Close (6 tasks)
- **TSK-0096** — Won't Fix (documented tradeoff)
- **TSK-0117** — Already done (Sprint 50 Wave B)
- **TSK-0078** — Superseded by TSK-0122
- **TSK-0118** — Remaining work → TSK-0127
- **TSK-0003** — Stale 10+ days; superseded by 731-test suite
- **TSK-0093** — No consumer need since Sprint 43

### Reprioritize (5 tasks)
- **TSK-0081**: Critical → Medium (tests for 7-sprint-old feature)
- **TSK-0130**: Critical → High (governance, not runtime)
- **TSK-0104**: Low → Medium (genuine architectural gap)
- **TSK-0040**: High → Medium (deferred to S54+)
- **TSK-0003**: High → Medium (if not closed)

### Deduplicate (3 pairs)
- Close TSK-0078 (absorbed by TSK-0122)
- Close TSK-0118 (remaining work → TSK-0127)
- Sequence TSK-0130 before TSK-0136

### Normalize Status (4 tasks)
- TSK-0012: `"Open"` → `"Backlog"`
- TSK-0013: `"Open"` → `"Backlog"`
- TSK-0086: `"Closed - Merged into TSK-0105"` → `"Done"`
- TSK-0098: `"Closed - Merged into TSK-0103"` → `"Done"`

---

## Sprint 52-53 Preview

| Sprint | Theme | Key Deliverables |
|:------:|:------|:-----------------|
| **Sprint 52** | Extract Boundaries | Split AgentBackgroundService (4 collaborators), split HtnTaskLibrary (5 decomposers), add SearchMemory→MoveTo gather routing, introduce typed PlanContext Phase 1 |
| **Sprint 53** | Harden & Validate | Structured memory metadata, 3+ host-level integration tests, delete obsolete bridges, expand PlanContext Phase 2, Runtime Configuration Model |
| **Sprint 54+** | Dashboard & Polish | Dashboard event bus, SignalR real-time push, LLM evaluator, OpenTelemetry, prompt injection defenses, SARIF/SCA |

---

## Acceptance Criteria (Cross-Sprint)

1. **Build gate:** `dotnet build` 0w/0e at every sprint boundary
2. **Test gate:** All existing + new tests pass
3. **Pipeline singularity:** One canonical pipeline documented; no contradictions
4. **Bridge catalog:** Every bridge classified with exit strategy
5. **Error handling:** Zero bare catch blocks; all exceptions logged before swallow
6. **Death recovery:** Bot recovers from death with correct inventory state
7. **Logging:** Diagnostic data visible at Information level in production
8. **Correlation:** Trace ID links chat→intent→plan→actions→completion

---

## Confidence

| Area | Confidence |
|---|---:|
| Sprint 51 primary track feasibility | 95% |
| Sprint 51b hardening track feasibility | 85% |
| Reviewer finding accuracy | 90% |
| Task curation accuracy | 92% |
| Sprint 52-53 preview feasibility | 80% |
| **Overall council confidence** | **88%** |

---

## Dissent

**Architectural Integrity Reviewer vs Rest of Council:** The reviewer argued that the LLM-first pipeline is "functionally complete" (90% confidence) and the architecture.md divergence is a documentation problem, not a runtime one. The rest of the council agrees on the documentation fix but notes the divergence has caused real onboarding confusion (Human Learning Advocate: 87% confidence). **Resolution:** Documentation fix (TSK-0125) remains Critical priority but acknowledged as documentation-only.

**Architectural Integrity Reviewer also noted** that `architecture.md` line "LLM is used sparingly" is factually wrong — LLM is primary path for all non-trivial intents. The reviewer recommends removing this sentence entirely. **Accepted.**

---

## Open Questions

1. **Can the Node.js adapter recreate the `bot` entity on server restart?** Requires Mineflayer API verification.
2. **Should the `Agent.Planning*` log overrides be removed or set to `Information`?** Removing them restores visibility but may be noisy. Setting to `Information` is the safer middle ground.
3. **Is the `_agentRuntime` field intended for Sprint 39+ or is it dead code?** Needs resolution in TSK-0126.
4. **Should mob/hunger handling be in-scope for S51-53 or deferred to S54+?** Mob handling is a large feature. Hunger/food consumption is smaller. Council recommends: food consumption in S53, mob combat deferred to S54+.
5. **When will a Minecraft server be available for TSK-0003 (first E2E game test)?** Task has been InProgress for 11 days. Close or reschedule.

---

**Council Signatures:** 10 adversarial reviewers + Synthesizer. All findings source-grounded. All tasks tracked. Nothing forgotten.
