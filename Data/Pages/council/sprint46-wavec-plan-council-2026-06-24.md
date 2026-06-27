# Council Review: Sprint 46 Wave C — "Tightening the Contracts"

**Date:** 2026-06-24  
**Review type:** Self-simulated 6-seat council (evidence gathered via Explore subagents)  
**Branch reviewed:** `sprint-35-llm-first` (MemorySmith.Agent) / `master` (MemorySmith)

## Decision

Prioritize fixing observable runtime metadata drift, auth safety defaults, and remaining silent-failure paths before beginning architectural decomposition — the codebase is healthy enough that the next wave should focus on deployment safety and operator trust, not new features.

## Evidence Reviewed

- `sprint46-waveb-complete.md` (repo memory) — Wave B+ completion (TSK-0103, TSK-0104, TSK-0106, TSK-0099)
- `sprint45-wavea-complete.md` (repo memory) — Wave A/B completion + Sprint 46 plan
- `memorysmith_agent_deep_dive_audit_2026-06-24.md` (Data/Pages/audits/) — 9 findings, 3 architectural opportunities
- `audit-validation-sprint46-plan-council-2026-06-24.md` (Data/Pages/council/) — Sprint 46 "Observability First" plan
- MemorySmith.Agent `WebUI.Blazor/Program.cs` — `/api/about` version string, WorldKbUrl warning
- MemorySmith.Agent `WebUI.Blazor/ApiKeyMiddleware.cs` — fail-open auth behavior
- MemorySmith.Agent `Data/Tasks/` — Task backlog (TSK-0107, TSK-0108, TSK-0083, TSK-0084, TSK-0085, TSK-0093, TSK-0096)
- MemorySmith base repo council reports and MS-Requests
- Test results: 664/664 passing, 0 warnings, 0 errors

## Findings

| Seat | Recommendation | Confidence | Blocking concern |
|---|---|---|---|
| **Source-Grounded Archivist** | Fix `/api/about` metadata drift first — it's the fastest fix with the highest trust impact. Move TSK-0105 (doc drift) remaining scope into this wave. | 0.92 | Stale version/phase undermines all runtime introspection; 30-second fix but 3-sprint-old lie. |
| **Data Model Architect** | Add explicit `AllowUnauthenticatedApi` flag to ApiKeyMiddleware with loud startup warning. Current fail-open is a deployment accident waiting to happen. Prioritize before any new planner work. | 0.88 | Auth being accidentally disabled by omission is P0-class risk. WorldKbUrl warning exists but ApiKey lacks the same treatment. |
| **Retrieval Specialist** | The 3 remaining bare-catch sites (RestMemoryGateway, WebSocketBridge×2) should be logged. Not P0 like Wave A catch→null fixes were, but should be swept before they hide real retrieval errors. | 0.85 | RestMemoryGateway silently swallowing 404s is the highest risk — could hide gateway misconfiguration. |
| **Human Learning Advocate** | Deploy the deferred tasks (TSK-0083, TSK-0084, TSK-0085) to Sprint 47. They are real gaps but lack a current consumer. Engineers debugging smelt or checkpoint issues will find them when needed. Don't block Wave C on them. | 0.82 | None blocking — but add a note to the Sprint 47 plan so they aren't forgotten entirely. |
| **Skeptical Reviewer** | The router-first architecture gap (HtnPlanner still carries typed-goal policy) is not yet causing bugs. Duplicating BuildOrigin resolution across HtnPlanner fallback vs BuildGoalDecomposer is the highest-latent-risk architectural item. Make TSK-0107 (runtime decomposition) concrete with a scoping doc. | 0.78 | If another typed goal (e.g. SmeltGoal) gets added to HtnPlanner's type-switch, the drift accelerates. |
| **Synthesizer** | **Wave C = Safety + Trust.** 3 concrete tasks, all P1/P2. Ship fast, then Sprint 47 picks up architecture decomposition and deferred maintenance. | 0.85 | Need to ensure TSK-0107 gets a clear scope boundary so Sprint 47 can start immediately. |

## Synthesis

### What changes now (Sprint 46 Wave C)
1. **TSK-0109 (P1): Fix `/api/about` metadata** — Update version to 0.46.0 and phase to "Sprint 46" in `WebUI.Blazor/Program.cs`. Add a compile-time or startup assertion that version strings match expectations.
2. **TSK-0110 (P1): ApiKeyMiddleware safety hardening** — Add `AllowUnauthenticatedApi` bool flag (default false). Emit `LogWarning` on startup when auth is disabled or key is missing. Keep the dev-convenience path but require explicit opt-in.
3. **TSK-0111 (P2): Sweep remaining bare catches** — `RestMemoryGateway.cs` line ~82, `WebSocketBridge.cs` lines ~96 and ~480. Add `LogWarning` before silent fallback. These are the 3 remaining catch→silent sites after Wave A's TSK-0101.

### What is deferred (Sprint 47)
- **TSK-0107 (P3): Runtime decomposition planning** — Needs a concrete scope document before implementation. Skeptical Reviewer's concern about typed-goal drift in HtnPlanner should be the centerpiece.
- **TSK-0108 (P3): Redundant state cleanup** — Requires TSK-0107 scope first.
- **TSK-0083 (P3): Checkpoint tests remainder** — ~50% done; pick up when checkpoint path changes.
- **TSK-0084 (P3): WorldStateProjector.ApplySmeltComplete** — No consumer; defer.
- **TSK-0085 (P3): HasFailed dead code** — Affects gather/smelt/craft/build equally; low risk.
- **TSK-0093 (P1 deferred): ParseItemSpec structured result** — Breaking change with no consumer.
- **TSK-0096 (P1 deferred): Mining double-counting** — Waiting on real-world evidence.

### What requires cross-repo coordination
- **TSK-0102 (P0): ChatServices.cs bare catches** — Already filed as `Data/Pages/MS-Requests/chat-services-bare-catches-2026-06-24.md`. No action needed in Wave C.
- External audit critical findings (C-1 secrets leak, C-2 OAuth, H-1 CSRF, H-2 escalation, H-3 MemoryIndex race) are base-repo work tracked in their own sprint plan.

## Dissent

- **Skeptical Reviewer vs Synthesizer on TSK-0107 priority:** Skeptical Reviewer wants a concrete scoping doc *this wave* to prevent further planner drift. Synthesizer argues that Wave C's 3 tasks are independent and fast (estimated <2h total), and a scoping doc belongs in Sprint 47 where it can be done thoughtfully. **Resolution:** Defer to Sprint 47 but add `HtnPlanner typed-goal drift analysis` as a required section in the TSK-0107 scope document.

- **Source-Grounded Archivist vs Human Learning Advocate on version centralization:** Archivist wants a single generated version source (Directory.Build.props + assembly attribute). Advocate says the `/api/about` string is trivially fixable inline and centralization can be done later. **Resolution:** Inline fix now (`/api/about` string literal update), centralization as a stretch goal.

## Acceptance Criteria

1. **TSK-0109:** `/api/about` returns `Version = "0.46.0"` and `Phase = "Sprint 46"`. A test asserts the values match roadmap.
2. **TSK-0110:** Setting `Agent:AllowUnauthenticatedApi=false` with missing `Agent:ApiKey` blocks all `/api/*` requests. Startup log contains a `LogWarning` when auth is disabled. Existing dev workflow unchanged with explicit `AllowUnauthenticatedApi=true`.
3. **TSK-0111:** Three sites have `LogWarning` before silent fallback. No new catch→null patterns added.
4. All existing 664 tests continue to pass. 0 new build warnings.
5. Council report saved to `Data/Pages/council/`.

## Open Questions

1. Should the version in `/api/about` be auto-derived from assembly metadata or hand-maintained? (TSK-0109 stretch)
2. Should `ApiKeyMiddleware` support per-route exemptions (e.g., `/api/health` unauthenticated)? (Deferred to post-Wave C)
3. What is the concrete scope boundary for `HtnPlanner` typed-goal removal? (TSK-0107 scoping doc in Sprint 47)
4. Should the remaining deferred P3 tasks (TSK-0083/0084/0085) be formally archived or retained with documented rationale?