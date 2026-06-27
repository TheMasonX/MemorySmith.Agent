# Council Review — Supplemental Addendum: Research Report Deep-Dive

**Date:** 2026-06-26
**References:** `Data/Pages/council/audit-findings-consolidation-council-2026-06-26.md`
**Reason:** Initial council review underweighted 4 research .docx reports. This addendum corrects that with 4 independent peer review subagents.

---

## What Was Missed

The 4 research reports (originally `.docx`, extracted to `Data/Pages/Audit/Research/`) contain ~160 distinct findings. Only ~42 were confirmed FIXED in Sprints 34-50. The remaining ~118 are open or partially addressed.

Key findings MISSING from the original council review:

1. **Memory Gateway data model issues** (R1-011–R1-017): ID ambiguity, score handling, HttpClient misuse — all HIGH severity, all OPEN
2. **Security posture** (R2-014–R2-016): Prompt injection, taint analysis, MCP config risks — all HIGH priority, all OPEN
3. **CI/CD infrastructure** (R2-017–R2-020): No integration/mutation/fuzz testing, no SARIF, no SCA — all OPEN
4. **Observability** (R1-007, R1-072–R1-074): No OpenTelemetry, no structured tracing — all OPEN
5. **Governance** (R2-025–R2-028): Missing CONTRIBUTING, CODEOWNERS, SECURITY — all OPEN
6. **Breaking changes** (R3-091–R3-096): NO deprecation policy, NO semantic versioning — CRITICAL
7. **6 adversarial findings** (ADV-001–ADV-006) — all tracked but none implemented

---

## New Tasks Created (see task system)

### Critical — Sprint 51

| TSK | Title | Research IDs |
|:----|:------|:------------|
| TSK-0129 | Add SearchResult.Kind field (page vs memory disambiguation) | R1-011 |
| TSK-0130 | Adopt deprecation policy + semantic versioning | R3-094, R3-095 |

### High — Sprint 51

| TSK | Title | Research IDs |
|:----|:------|:------------|
| TSK-0131 | Adopt IHttpClientFactory in RestMemoryGateway | R1-014 |
| TSK-0132 | Fix page Score=0.0 under-ranking in search results | R1-013 |
| TSK-0133 | Fix UpdatePageAsync title bug + ToSlug hardening | R1-012 |
| TSK-0134 | Document all breaking changes with migration guide | R3-091–R3-093 |
| TSK-0135 | Fix parameter preservation on replan (remaining count) | R2-003 |
| TSK-0136 | Add DI startup failure logging and health checks | R2-006 |

### Medium — Sprint 51

| TSK | Title | Research IDs |
|:----|:------|:------------|
| TSK-0137 | Fix HtnTaskLibrary.MakeAction mutability pattern | R2-004 |
| TSK-0138 | Fix consecutive failure guard (reset on partial progress) | R2-007 |

### Deferred to Sprint 52-53

| TSK | Title | Research IDs |
|:----|:------|:------------|
| TSK-0139 | Schema validation for LLM-generated JSON plans | R1-027 |
| TSK-0140 | Capture failure contexts in memory for learning | R1-028 |
| TSK-0141 | Prompt versioning and provenance tracking | R1-020, R1-081, R1-082 |
| TSK-0142 | Hardcoded block ID → version-aware parameterization | R2-012, R2-013 |
| TSK-0143 | World event schema validation | R1-045 |
| TSK-0144 | Implicit DTO versioning + contract tests | R1-069–R1-071 |
| TSK-0145 | Tool schema drift prevention for LLM tools | R1-019 |
| TSK-0146 | Streaming/cancellation test coverage | R1-021 |

### Deferred to Sprint 54+ (tracked, not forgotten)

OpenTelemetry integration, SARIF/static analysis, SCA/dependency scanning (Snyk/Dependabot), integration/mutation/fuzz testing, governance docs, prompt injection defenses, taint analysis, MCP config hardening, secret management.

Full deferred list: see `Data/Pages/council/audit-findings-consolidation-addendum-2026-06-26.md`

---

## Reconciliation: DA Findings vs Research Reports

| DA | Enhanced By Research |
|:---|:---------------------|
| DA-001 (Pipeline) | Planner concurrency risks, ReplanAsync fix history, parameter preservation |
| DA-002 (BackgroundService) | Specific error gaps, DI startup failure, failure guard strictness |
| DA-003 (Planner) | Context carryover need, MakeAction mutability, plan serialization |
| DA-004 (Context typing) | DTO versioning, schema validation at serialization boundaries |
| DA-005 (Memory) | ID ambiguity, score handling, HttpClient misuse (substantially expanded) |
| DA-006 (Testing) | Mutation/fuzz/SARIF/adversarial testing gaps |
| DA-007 (Compat layers) | No deprecation policy, no semver — entirely new dimension |
| DA-008 (Dispatcher) | Dynamic registration fragility, tool schema drift |

---

**Council Addendum Confidence: 90%**
