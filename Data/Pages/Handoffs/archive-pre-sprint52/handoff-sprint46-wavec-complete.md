# Handoff: Sprint 46 Wave C Complete — "Tightening the Contracts"

**Date:** 2026-06-24
**Prepared by:** SteveBot
**Next sprint:** Sprint 47 — "Architecture Consolidation"

---

## Sprint 46 Summary

Sprint 46 completed 11 tasks across three waves, all under the "Observability First" / "Tightening the Contracts" themes. The codebase is now fail-closed on auth, all known bare-catch sites log warnings, runtime metadata is current, BuildOrigin is a proper value object, ReplanResult types replace null overloads, and documentation has been corrected.

### Waves Completed

| Wave | Theme | Tasks | Status |
|---|---|---|---|
| **Wave A** | Observability First | TSK-0100, TSK-0101, TSK-0102, TSK-0105 | ✅ |
| **Wave B+** | Observability First | TSK-0099, TSK-0103, TSK-0104, TSK-0106 | ✅ |
| **Wave C** | Tightening the Contracts | TSK-0109, TSK-0110, TSK-0111 | ✅ |

### Key Metrics

| Metric | Wave A/B | Wave C | Net |
|---|---|---|---|
| Build warnings | 0 | 0 | 0 |
| Tests passing | 664 | 666 | +2 |
| Files changed | ~15 | 6 | — |

---

## Critical Review of Wave C

### What went well

1. **TSK-0109 (/api/about)** was a 30-second fix with immediate trust impact — the fastest high-confidence win in the backlog.
2. **TSK-0110 (auth hardening)** changed the default from "implicitly open" to "explicitly closed." The behavioral change is correct per every audit that examined it.
3. **TSK-0111 (bare catches)** eliminated the last known silent-failure sites in the C# agent code. The `ParseEvent`/`ParseStatus` logger threading was more invasive than ideal but produced a net improvement — those static methods now write structured diagnostics instead of `Debug.WriteLine`.

### Issues and residual risks

1. **TSK-0110 startup warning gap** — The `LogWarning` fires per-request (when auth is missing and the flag is false), not at startup. The original requirement said "Emit LogWarning on startup when auth is disabled." True startup-time warnings require hooking into `WebApplication` lifecycle events. This is a minor gap — the per-request warning will still appear in logs on the first API call — but it's not a true startup-time signal. **Deferred to Sprint 47 stretch.**

2. **TSK-0109 version centralization (stretch goal deferred)** — The version string remains a hand-maintained literal in `Program.cs`. It will drift again unless centralized into `Directory.Build.props` or a shared constants file. The test (`ApiAbout_ReturnsCorrectVersionAndPhase`) will catch drift after the fact but won't prevent it. **Recommend prioritizing for Sprint 47 Wave C.**

3. **ApiKeyMiddleware behavioral change** — The test `ApiKeyMiddleware_NoKeyConfigured_AllowsRequest` was renamed to `ApiKeyMiddleware_NoKeyConfigured_WithExplicitOptIn_AllowsRequest`. Any CI/CD gating, health-check scripts, or documentation that assumed the old "no key = open" behavior will need updating. The migration is documented in the task, but operators should be notified.

4. **Logger parameter threading in WebSocketBridge** — `ParseEvent` and `ParseStatus` now take an `ILogger<WebSocketBridge>` parameter as a workaround for being static methods called from a primary-constructor class. This works but adds method-signature surface area. If these methods are ever made instance methods (a reasonable refactor), the parameter should be removed.

### Audit-derived tasks not yet started

Seven new tasks were created from the 6 audit documents but none were implemented in Wave C. These are scoped to Sprint 47:

| Task | Priority | Confidence |
|---|---|---|
| TSK-0112: Fix CraftItem prerequisite count scaling | P1 | 96% |
| TSK-0114: Preserve structured exception metadata in ToolDispatcher | P1 | 93% |
| TSK-0117: Post-craft/post-smelt inventory reconciliation | P2 | 98% |
| TSK-0113: Add drop-resolution table (mined block ≠ item) | P2 | 88% |
| TSK-0116: Move creative-mode build into decomposer layer | P2 | 90% |
| TSK-0118: Resolve chat interpretation split-brain | P2 | 99% |
| TSK-0115: Unify ActionQueue synchronization | P2 | 84% |

---

## Files Changed (Wave C)

| File | Change | Risk |
|---|---|---|
| `WebUI.Blazor/Program.cs` | Version/phase string literals | None |
| `WebUI.Blazor/ApiKeyMiddleware.cs` | Added `AllowUnauthenticatedApi`, fail-closed default | Medium (behavioral change) |
| `Agent.World.Minecraft/WebSocketBridge.cs` | 3 catch sites + logger threading | Low |
| `Agent.Memory/RestMemoryGateway.cs` | Added ILogger + 1 catch site | None |
| `MemorySmith.Agent.Tests/Sprint46Tests.cs` | New ApiAbout test | None |
| `MemorySmith.Agent.Tests/Sprint32Tests.cs` | Updated/added middleware tests | Low |

---

## Outstanding Cross-Repo Items

| Item | Status | Owner |
|---|---|---|
| ChatServices.cs 20+ bare catch blocks | Doc filed at `Data/Pages/MS-Requests/chat-services-bare-catches-2026-06-24.md` | Base repo |
| World KB deploy config | Tracked in base repo sprint plan | Base repo |

---

## Sprint 47 Quick-Start

The Sprint 47 plan is at `Data/Pages/Tasks/sprint47-plan.md`. The recommended start order:

1. **TSK-0112** (craft scaling) — highest-confidence correctness bug, affects every multi-item craft
2. **TSK-0114** (ToolDispatcher metadata) — highest-leverage observability fix remaining
3. **TSK-0117** (inventory reconciliation) — closes the oldest open P0-class inventory truth gap
4. **TSK-0109 stretch** (version centralization) — quick win before it drifts again

### Council Documents Created This Session

| Document | Purpose |
|---|---|
| `Data/Pages/council/sprint46-wavec-plan-council-2026-06-24.md` | Wave C scope and approval |
| `Data/Pages/Audit/msa_code_audit_report-6-24-26.md` | Code audit — 6 findings |
| `Data/Pages/Audit/msa_followup_audit-6-24-26.md` | Follow-up audit — 6 findings |
| `Data/Pages/Audit/memorysmith_situational_awareness_design_doc_20260625T020914Z.md` | Situational awareness design proposal |
| `Data/Pages/Audit/msa_sprint35-llm-first_audit_2026-06-24.md` | Sprint 35 branch audit |
| `Data/Pages/Audit/memorysmith_agent_deep_dive_audit_2026-06-24.md` | Deep dive audit — 9 findings |
| `Data/Pages/Audit/memorysmith_agent_deep_dive_audit_addendum_2026-06-24.md` | Audit addendum — 6 findings |
| `Data/Pages/Audit/sprint-45-audit-6-24.md` | Sprint 45 audit (base repo) |
| `Data/Pages/council/audit-validation-sprint46-plan-council-2026-06-24.md` | Sprint 46 plan council (base repo) |
