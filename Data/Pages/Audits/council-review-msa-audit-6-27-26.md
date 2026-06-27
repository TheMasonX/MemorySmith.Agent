# Council Review: Adversarial Audit of MemorySmith.Agent (msa-audit-6-27-26)

**Date:** 2026-06-27  
**Review type:** 10-seat adversarial council review  
**Original audit:** [msa-audit-6-27-26.md](./msa-audit-6-27-26.md)  
**Synthesizer confidence:** 88%

---

## Decision

**Accept the audit with significant corrections.** The original audit correctly identified architectural strengths and the LLM fallback pattern, but overstated test coverage confidence, mischaracterized `ChatInterpreter` size (~310 LOC, not ~2500), missed 4 critical security gaps, and understated "deterministic-first" claims. Proceed with a 2-sprint remediation plan (Sprint 53: Security + Critical Tests; Sprint 54: Thread Safety + Data Model + Cleanup). 21 unique findings across 4 priority tiers.

---

## Evidence Reviewed

### Code Evidence (verified by Archivist seat)
- `Agent.Planning/ChatInterpreter.cs` — 310 LOC, deterministic fast-paths only (not 2500 LOC as claimed)
- `Agent.Planning/LlmChatInterpreter.cs` — full pipeline verified (lines 60-170, 290-440)
- `Agent.Planning/ChatRateLimiter.cs` — sliding window correct, inconsistent cleanup design
- `Agent.Core/ReplanGovernor.cs` — `TryAutoRecover` vs `Evaluate` auto-recovery paths diverge (lines 97-109 vs 152-162)
- `Agent.Construction/BlueprintParser.cs` — strict input, no dimension validation
- `Agent.Construction/BlueprintExecutor.cs` — floor-first sort, no dedicated tests
- `Agent.Core/CommonMinecraftBlocks.cs` — ~10-15 KB, NOT "huge" (~170 lines)
- `Agent.Planning/Llm/GeminiProvider.cs` — API key in URL query string (line 44)
- `Agent.Planning/Llm/OpenAICompatibleProvider.cs` — silently drops HTTP error bodies (line 87)
- `Agent.Planning/Llm/LlmProviderFactory.cs` — throws on unknown provider (lines 33-38)
- `Agent.Planning/OllamaLlmClient.cs` — confirmed dead stub (3 lines)
- `WebUI.Blazor/AgentHub.cs` — unauthenticated SignalR hub
- `WebUI.Blazor/AgentBackgroundService.cs` — unsynchronized shared fields (lines 171-182)
- `WebUI.Blazor/Program.cs` — `/api/about` version 0.50.2 vs README 0.51.0
- `README.md` — version drift, test count inconsistency (742 vs 501)
- `Data/Pages/home.md` — version 0.23.0 (28 versions behind)
- `Data/Pages/getting-started.md` — version stale

### Test Evidence (verified by Test Coverage seat)
- `MemorySmith.Agent.Tests/` — 57 test files, 742+ passing
- Zero test files for: `ChatRateLimiter`, `OllamaProvider`, `OpenAICompatibleProvider`, `AnthropicProvider`, `GeminiProvider`, `LlmProviderFactory`
- `BlueprintExecutor` has no dedicated tests
- `Agent.Vision/WorldVision` has zero tests
- `MinecraftAdapter`/`WebSocketBridge` have zero direct tests
- `TryParseTruncatedJson` tested only via brittle reflection (`Sprint20/21/39Tests`)
- No minimum coverage threshold in CI

### CI/DevOps Evidence (verified by DevOps seat)
- `.github/workflows/ci.yml` — collects coverage but never processes/reports it
- No Node.js adapter testing in CI
- No dependency vulnerability scan
- No `global.json` SDK pinning
- No SourceLink / `SourceRevisionId` configuration
- 6 scripts with hardcoded `D:\@Repos\MemorySmith.Agent` paths
- `Serilog.Sinks.EventLog` referenced but never used

---

## Seat-by-Seat Findings

| # | Seat | Key Finding | Confidence | Blocking Concern |
|---|---:|---|---|
| 1 | **Source-Grounded Archivist** | Audit claim 3 REFUTED (ChatInterpreter ~310 LOC not 2500); ReplanGovernor state drift (MEDIUM); 4 missed findings | 95% | Audit overstated ChatInterpreter size by ~8× |
| 2 | **Security & Input Auditor** | Gemini API key in URL (CRITICAL); SignalR unauthenticated (HIGH); unsanitized LLM input (HIGH) | 90% | Gemini key leakage is P0 — cannot ship |
| 3 | **Performance & Concurrency** | AgentBackgroundService unsynchronized shared fields (HIGH); fire-and-forget SignalR pushes (MEDIUM); `_worldState` bypasses lock (MEDIUM) | 85% | Thread-safety gaps are latent crash risks |
| 4 | **Test Coverage Advocate** | 5 LLM providers + factory + rate limiter have ZERO tests (CRITICAL); `AgentBackgroundService` missing error-path tests (HIGH) | 95% | LLM critical path is untested |
| 5 | **Data Model Architect** | `ActionData.Tool` wire-name inconsistency "PlaceBlock" vs "place" (CRITICAL); no schema versioning (CRITICAL); `WorldState.Facts` mutable dictionary (HIGH) | 80% | Silent data corruption on field renames |
| 6 | **Retrieval Specialist** | `SearchAsync` no error handling (HIGH); no HTTP retry/resilience (HIGH); `LocalKnowledgeResolver` uses Agent KB only, not World KB (MEDIUM) | 90% | Transient HTTP failures crash tool execution |
| 7 | **Human Learning Advocate** | `/api/about` version 0.50.2 vs README 0.51.0 (BLOCKING); `home.md` 28 versions behind (BLOCKING); test count 501 vs 742 | 85% | Documentation version drift undermines trust |
| 8 | **DevOps & CI Reliability** | No coverage report generation (HIGH); no Node.js CI testing (CRITICAL); hardcoded machine paths in 6 scripts (CRITICAL) | 90% | CI coverage data is invisible; adapter untested |
| 9 | **Skeptical Reviewer** | Original audit overconfident: deterministic-first claim is misleading (LLM-dependent for core); 4 silent failure modes missed; blueprint claims overstated | 85% | Audit confidence scores inflated ~20-30% above justified |
| 10 | **Synthesizer** | 21 unique findings after de-duplication; 4 P0, 8 P1, 8 P2, 7 P3; 2-sprint plan (Sprint 53-54) | 88% | Effort estimates ±1 day uncertainty per wave |

---

## Autopsy of Original Audit Claims

| # | Original Claim | Original Confidence | Verdict | Corrected Confidence | Rationale |
|---|---:|---|---|---|
| 1 | "Modular, deterministic-first design" | 90% | 🔴 OVERSTATED | 60% | LLM is REQUIRED for gather/build/craft — deterministic path returns "Didn't catch that." `LlmEnabled=false` disables 80% of functionality. |
| 2 | "LLM integration robust" | 80% | 🟡 OVERSTATED | 55% | Transport-layer handling is solid, but ZERO semantic validation on LLM output. Hallucinated blueprints/items pass through silently. |
| 3 | "ChatInterpreter ~2500 LOC, may be obsolete" | 75% | 🔴 REFUTED | N/A | Actually 310 LOC. Actively used as deterministic fast-path fallback. Not removable. |
| 4 | "Rate limiting & concurrency correct" | 85% | 🟡 SLIGHTLY OVERSTATED | 82% | Logically correct but inconsistent cleanup design (player entries pruned, global window not). `ReplanGovernor` dual recovery path is a maintenance hazard. |
| 5 | "Blueprint & Construction stable" | 75% | 🟡 OVERSTATED | 50% | Works for simple rectangular structures only. No physics awareness, no non-contiguous layer validation, no dimension-vs-grid validation. |
| 6 | "No glaring security or silent failures" | 80% | 🔴 REFUTED | 45% | 4 silent failure modes found: greedy JSON brace regex, UTF-16 truncation, Gemini key in URL, truncated JSON confidence=1.0 bypass. |
| 7 | "CommonMinecraftBlocks is huge" | N/A | 🟡 OVERSTATED | N/A | ~170 lines, ~10-15 KB total. Not "huge." Maintainability risk from Minecraft version drift is real but the size claim is wrong. |

---

## New Findings (Missed by Original Audit)

| ID | Finding | Severity | Discovered By |
|---|---|---|---|
| NF-1 | **Gemini API key in URL query string** (leaks to logs/proxies) | 🔴 CRITICAL | Security |
| NF-2 | **SignalR hub unauthenticated** (any LAN client can connect) | 🔴 CRITICAL | Security |
| NF-3 | **AgentBackgroundService unsynchronized shared fields** (_currentGoal, _consecutiveFailures) | 🔴 CRITICAL | Performance |
| NF-4 | **Zero tests for 5 LLM providers + factory + rate limiter** | 🔴 CRITICAL | Test Coverage |
| NF-5 | **Unsanitized player messages to LLM** (prompt injection) | 🟠 HIGH | Security |
| NF-6 | **OpenAI/Anthropic/Gemini silently drop HTTP error bodies** | 🟠 HIGH | Archivist |
| NF-7 | **`_worldState` bypasses StateManagerImpl lock** | 🟠 HIGH | Performance |
| NF-8 | **`SearchAsync` has zero error handling** (transient HTTP → crash) | 🟠 HIGH | Retrieval |
| NF-9 | **No HTTP retry/resilience for MemorySmith API** | 🟠 HIGH | Retrieval |
| NF-10 | **No Node.js adapter testing in CI** | 🟠 HIGH | DevOps |
| NF-11 | **No coverage report generation in CI** | 🟠 HIGH | DevOps |
| NF-12 | **`ActionData.Tool` wire-name inconsistency** ("PlaceBlock" vs "place") | 🟠 HIGH | Data Model |
| NF-13 | **No schema versioning mechanism** | 🟠 HIGH | Data Model |
| NF-14 | **ReplanGovernor dual auto-recovery path drift** | 🟡 MEDIUM | Archivist + Performance |
| NF-15 | **Hardcoded machine paths in 6 scripts** | 🟡 MEDIUM | DevOps |
| NF-16 | **`/api/about` version 0.50.2 vs README 0.51.0** | 🟡 MEDIUM | Human Learning |

---

## Synthesis: Prioritized Sprint Plan

### 🔴 P0 — BLOCKING (Sprint 53, Days 1-3)

| ID | Task | Effort |
|----|------|--------|
| **F-SEC-1** | Move Gemini API key from query string to `x-goog-api-key` header | S |
| **F-SEC-2** | Add API key authentication to SignalR `/agent-hub` | S |
| **F-THREAD-1** | Synchronize `_currentGoal`, `_consecutiveFailures`, `_connectionStatus` | M |
| **F-CORRECT-1** | Log HTTP error bodies in OpenAI/Anthropic/Gemini providers | S |

### 🟠 P1 — HIGH (Sprint 53, Days 4-7)

| ID | Task | Effort |
|----|------|--------|
| **F-TEST-1** | Add tests for all 5 LLM providers + factory + ChatRateLimiter | L |
| **F-TEST-2** | Add `AgentBackgroundService` error-path tests | L |
| **F-SEC-3** | Sanitize player messages before LLM (strip injection markers) | M |
| **F-SEC-4** | Mask API keys in log output | S |
| **F-CORRECT-2** | `CreatePageTool`: return `ToolResult(false,...)` instead of throw | S |
| **F-THREAD-2** | Route `_worldState` reads through `IStateManager.Current` | M |
| **F-THREAD-3** | Synchronize `DashboardPublisherImpl` fields | M |
| **F-DATA-1** | Normalize `ActionData.Tool` to canonical form | M |
| **F-RETRIEVAL-1** | Add error handling to `SearchAsync` + `SearchMemoryTool` | S |
| **F-RETRIEVAL-2** | Add HTTP retry/resilience for MemorySmith clients | M |

### 🟡 P2 — MEDIUM (Sprint 54)

| ID | Task | Effort |
|----|------|--------|
| **F-DATA-2** | Schema versioning RFC document | M |
| **F-DATA-3** | Convert `StructuredEffect.Type` to enum | M |
| **F-DATA-4** | Validate `Blueprint.Dimensions` against parsed grid | S |
| **F-TEST-3** | Stub `Agent.Vision` test structure | M |
| **F-TEST-4** | Add `MinecraftAdapter`/`WebSocketBridge` contract tests | L |
| **F-TEST-5** | Add minimum coverage threshold to CI | S |
| **F-CLEAN-1** | Move `ActionQueue`/`WorldModel` to `Runtime/` | S |
| **F-CLEAN-2** | Delete `.bak` files, add `*.bak` to `.gitignore` | S |
| **F-CI-1** | Add coverage report generation to CI | M |
| **F-CI-2** | Add Node.js adapter testing to CI | M |
| **F-CI-3** | Add vulnerability scan to CI | S |
| **F-DOC-1** | Fix version drift (README, `/api/about`, `home.md`) | S |
| **F-DOC-2** | Update stale wiki pages (`getting-started.md`, `architecture.md`) | S |

### 🟢 P3 — LOW (Backlog)

| ID | Task | Effort |
|----|------|--------|
| **F-MED-1** | Add secrets manager support for Memory API key | S |
| **F-MED-2** | Configure SignalR max message size | S |
| **F-MED-3** | Bound chat channel with `DropOldest` | S |
| **F-DATA-5** | Convert `SearchResult.Kind` to enum | S |
| **F-DATA-6** | Consolidate `BeliefState`/`ObservationState` | M |
| **F-MED-4** | Add semantic tool argument validation | M |
| **F-CI-4** | Fix hardcoded paths in scripts (use `$PSScriptRoot`) | M |
| **F-CI-5** | Add `global.json` SDK pinning | S |
| **F-CI-6** | Add SourceLink for `GitHash` embedding | S |
| **F-CI-7** | Remove unused `Serilog.Sinks.EventLog` | S |

---

## Dissent

### Skeptical Reviewer vs. Original Audit
**Disagreement:** The original audit's "deterministic-first" characterization and 80-90% confidence scores are systematically overstated. The code is LLM-first in practice (gather/build/craft require LLM). The audit's 80% "no glaring issues" confidence should be ~45%.

**Resolution:** The Skeptical Reviewer's assessment is accepted. The audit's confidence scores are downgraded as shown in the Autopsy table above. The "deterministic-first" label is corrected to "LLM-first with deterministic safety fast-paths" in all downstream documentation.

### Synthesizer vs. Security Seat — Memory API Key
**Disagreement:** Security seat rated "Memory API key from env var only" as MEDIUM. Synthesizer downgraded to P3.

**Resolution:** Synthesizer's downgrade stands. Environment variables are 12-factor standard. The key CAN be set via any `IConfiguration` source (Kubernetes secrets, Azure Key Vault, etc.). Adding dedicated secrets manager integration is a feature, not a security gap fix.

### Synthesizer vs. Security Seat — Semantic Tool Validation
**Disagreement:** Security seat rated "Tools lack semantic validation" as HIGH. Synthesizer downgraded to P3.

**Resolution:** Synthesizer's downgrade stands. The Minecraft server validates block types, coordinates, and game rules. A C#-side semantic validator is redundant with the server and adds maintenance burden for every Minecraft version.

---

## Acceptance Criteria

### Sprint 53 Gate (Security + Critical Tests)
1. ✅ Gemini API key moved to HTTP header (unit test asserts no `?key=` in URL)
2. ✅ SignalR hub requires authentication (unauthenticated → 401)
3. ✅ `_currentGoal`, `_consecutiveFailures`, `_connectionStatus` thread-safe (concurrent-access stress test)
4. ✅ All LLM providers log HTTP error bodies before returning null
5. ✅ Player messages sanitized before LLM (injection markers stripped)
6. ✅ API keys masked in all log output
7. ✅ `ChatRateLimiter`, `LlmProviderFactory`, all 4 LLM providers have tests
8. ✅ `AgentBackgroundService` has error-path tests (reconnect, corrupt queue, goal transition)
9. ✅ `SearchAsync` + `SearchMemoryTool` handle HTTP errors gracefully
10. ✅ HTTP retry/resilience configured for MemorySmith clients
11. ✅ All 742+ existing tests continue to pass. 0 new build warnings.

### Sprint 54 Gate (Thread Safety + Data Model + Cleanup)
1. ✅ `_worldState` reads routed through `IStateManager.Current`
2. ✅ `DashboardPublisherImpl` fields synchronized
3. ✅ `ActionData.Tool` normalized to canonical form
4. ✅ `StructuredEffect.Type` is an enum
5. ✅ `BlueprintParser` validates dimensions vs grid
6. ✅ Coverage report generated and published in CI
7. ✅ Node.js adapter tested in CI
8. ✅ Vulnerability scan in CI
9. ✅ All version strings consistent (README, `/api/about`, wiki pages)
10. ✅ `.bak` files deleted; operational classes moved to `Runtime/`
11. ✅ Schema versioning RFC written

---

## Open Questions

| ID | Question | Owner | Due |
|----|----------|-------|-----|
| Q-1 | What authentication mechanism for SignalR? Reuse `ApiKeyMiddleware` pattern or something else? | Product Owner | Sprint 53 Day 1 |
| Q-2 | What sanitization is sufficient for player-to-LLM messages? Strip `[[SYSTEM]]` only, or full entity encoding? | Security Lead | Sprint 53 Day 2 |
| Q-3 | CI coverage threshold starting point? 40% is proposed as floor; actual baseline unknown. | DevOps | After F-CI-1 done |
| Q-4 | Should schema versioning be an ADR (needs council) or RFC (lighter weight)? | Architect | Sprint 54 |
| Q-5 | Is `Agent.Vision` still planned for Phase 5, or should it be archived? | Product Owner | Sprint 54 |
| Q-6 | Should `ChatInterpreter` be classified as "permanent bridge" (not legacy to remove)? | Architect | Sprint 53 |

---

## Risk Register

| ID | Risk | Impact | Likelihood | Mitigation |
|----|------|--------|-----------|------------|
| R-1 | Thread-safety fix introduces deadlock | Agent hangs | Low | Use simple locks, test with concurrent-access stress test |
| R-2 | LLM provider test mocks diverge from real API | Tests pass, prod fails | Medium | Integration smoke test against real endpoints in CI |
| R-3 | Sanitization too aggressive breaks legitimate chat | Reduced functionality | Low | Test with real Minecraft chat corpus |
| R-4 | Schema versioning RFC triggers breaking-change cascade | Sprint scope creep | Medium | Keep as design doc only in Sprint 54 |
| R-5 | Effort estimates wrong by 2x | Sprint overrun | Medium | 1-day buffer per sprint; defer P2 items if needed |

---

## References

- Original audit: [msa-audit-6-27-26.md](./msa-audit-6-27-26.md)
- Sprint 53 plan: [sprint-53-security-test-coverage.md](../Tasks/Sprints/sprint-53-security-test-coverage.md)
- Sprint 54 plan: [sprint-54-thread-data-cleanup.md](../Tasks/Sprints/sprint-54-thread-data-cleanup.md)
- AGENTS.md: `d:\@Repos\MemorySmith.Agent\AGENTS.md`
- Architecture: `Data/Pages/architecture.md`
- Decisions: `Data/Pages/decisions.md`

---

*Council convened 2026-06-27. Report synthesized from 10 independent seat reviews. All seats ran with explicit subagent permission. Confidence values and dissent are preserved per council procedure.*
