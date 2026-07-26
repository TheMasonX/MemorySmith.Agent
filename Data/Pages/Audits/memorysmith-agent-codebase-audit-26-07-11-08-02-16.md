# MemorySmith.Agent codebase audit

**Target:** `TheMasonX/MemorySmith.Agent`  
**Branch / commit:** `dev/round-3` @ `0f27af1befb72e7534421a1bd5550aee1e077d96`  
**Date:** 2026-07-11

## Executive summary

The strongest risks in the current branch are not isolated bugs; they are systems-level consistency problems:

1. CI does **not** run on the active `dev/round-3` branch, so the in-progress work can bypass the guardrails entirely. The workflow only triggers on `main`, `master`, and `feature/**` pushes. fileciteturn25file0
2. The public-facing metadata is already drifting: `Program.cs` still reports `/api/about` as version `0.55.0` / Sprint 55, while the roadmap says the project is at `v0.60.0` and Sprint 60, and `about.html` still shows `v0.51.0` / Sprint 51. fileciteturn22file0turn10file0turn41file0
3. Several legacy shim surfaces still behave like permanent contracts: `/api/agent/connect`, `/api/agent/stop`, and `/api/blueprints` return hardcoded or no-op responses instead of reflecting actual runtime state. `Program.cs` also still falls back to the legacy `Agent:Chat:Model` key when `LlmModel` is unset. fileciteturn23file0turn19file0
4. The prompt/memory boundary remains porous. Session facts are loaded directly into system chat history, and remembered facts are written back with only minimal slug normalization. That is exactly the kind of path that can become prompt injection or memory poisoning if untrusted content gets stored. The roadmap already has prompt-injection remediation items queued, which confirms this is a known risk area. fileciteturn35file0turn15file0turn10file0
5. Time abstraction is inconsistent. Most of the host uses `ITimeProvider`, but the inventory sync loop still uses `DateTimeOffset.UtcNow`, which weakens deterministic testing and creates a second clock source. fileciteturn31file0

## Highest-priority findings

| Priority | Finding | Why it matters | Confidence |
|---|---|---|---:|
| P0 | CI does not cover `dev/round-3` pushes | The active branch can accumulate regressions without CI feedback. | 98% |
| P1 | `/api/about` and `about.html` are stale | Public metadata and dependency inventory no longer match the current sprint state. | 96% |
| P1 | Legacy/no-op API surfaces still ship | They create false success signals and preserve technical debt in a greenfield system. | 92% |
| P1 | Prompt/memory injection boundary is weak | Untrusted content can be stored and later injected into the LLM context. | 88% |
| P2 | `DateTimeOffset.UtcNow` bypasses `ITimeProvider` | Tests become non-deterministic and time-based logic is harder to reason about. | 95% |
| P2 | Fact slug normalization is too weak | Keys can collide or become invalid when facts contain punctuation or path-like text. | 84% |
| P2 | `/health` reports resolution, not readiness | A service can resolve while still being disconnected or otherwise unhealthy. | 82% |

## Actionable findings and recommendations

### 1) CI misses the active branch
The workflow triggers on pushes to `main`, `master`, and `feature/**`, plus PRs targeting `main`/`master`. That excludes `dev/round-3`, which is the branch under review. This is a direct coverage hole, not a theoretical one. Add `dev/**` (or the exact branch) to the push trigger, or move the branch into the existing trigger set. fileciteturn25file0

### 2) Version / phase metadata is drifting
`Program.cs` returns `Version = "0.55.0"` and a Sprint 55 phase string from `/api/about`, while the roadmap says the current version is `v0.60.0` and the current sprint is 60. `about.html` still shows `v0.51.0` and “Sprint 51 — Wave A Complete.” That is stale enough to mislead operators and any tooling that treats `/api/about` as canonical. Generate both from one source of truth. fileciteturn22file0turn10file0turn40file0

### 3) Legacy shim endpoints should be removed or made explicit
`/api/agent/connect` and `/api/agent/stop` always return success without changing state, and `/api/blueprints` returns a single hardcoded blueprint. In a greenfield codebase, these are legacy compatibility shims that should either be removed, feature-flagged, or renamed to signal that they are placeholders. `Program.cs` also still maps `Agent:Chat:Model` into `LlmModel` as a fallback, which is another legacy compatibility path that should be treated as temporary. fileciteturn23file0turn19file0

### 4) Prompt injection / memory poisoning risk is still real
`LoadSessionFactsAsync()` pulls pages from the MemorySmith wiki and inserts the contents directly into chat history as a `System` record. `RememberFactAsync()` writes facts back with just lowercase + hyphen replacement; it does not fully slugify or validate the key. That is a wide trust boundary. If a stored page or remembered fact contains adversarial content, it can shape future prompts or create malformed / colliding page slugs. The roadmap already contains explicit prompt-injection remediation backlog items, which means this area is known to need hardening. Use structured, schema-validated memory records and keep untrusted content out of the system prompt. fileciteturn35file0turn15file0turn10file0

### 5) Time abstraction is inconsistent
`InventorySyncLoopAsync()` computes age with `DateTimeOffset.UtcNow`, even though the rest of the host is already wired around `ITimeProvider`. That breaks deterministic tests and introduces a second implicit clock source. Replace the raw UTC call with the injected provider, and keep freshness logic on one time abstraction. fileciteturn31file0

### 6) Readiness signaling is too shallow
The `/health` endpoint only verifies that `AgentBackgroundService` can be resolved inside a scope. That is weaker than readiness. A service can be resolvable while still disconnected, stalled, or otherwise not operational. Tie the endpoint to explicit runtime state, not DI resolution alone. fileciteturn22file0

### 7) Fact key normalization is under-specified
`RememberFactAsync()` turns spaces into hyphens, but it leaves punctuation, slashes, and other special characters untouched. That can create invalid or ambiguous slugs. Use a real slugifier plus collision checks, especially if these facts are later loaded into prompt context. fileciteturn35file0

## Duplication / sprint-plan check

The roadmap already shows that several adjacent items exist and should not be re-created as new work:

- `TSK-0371` is explicitly superseded by `TSK-0394` in the task comments. fileciteturn14file0
- `TSK-0374` is only partially independent; the task comments say some of its scope is already covered by `TSK-0401`. fileciteturn15file0
- The roadmap also lists prompt-injection remediation, memory gateway error handling, and other audit-fix work in Sprint 60/61, so new tasks should extend those existing items rather than duplicate them. fileciteturn10file0

## Supplemental implementation guidance

- Prefer deleting dead shims over preserving them behind “for now” branches.
- Make `/api/about` and `about.html` consume the same generated metadata source.
- Move memory loading/saving behind a typed envelope, not raw prompt text.
- Use `ITimeProvider` everywhere a freshness or timeout decision is made.
- Convert readiness checks from “can resolve” to “is actually operational.”
- Add branch coverage for `dev/**` or the exact active branch pattern so CI protects the work-in-progress line.

## Assumptions and open questions

- I reviewed the accessible current-branch files and the sprint/task surfaces that were exposed in this session. I could not directly enumerate the full repository tree from the connector, so there is a small chance of additional file-level issues outside the surfaced paths.
- I assumed `/api/about` is intended to be canonical operational metadata. If it is intentionally archival, it should be renamed so callers do not treat it as live truth.
- I assumed the memory pages loaded in `LoadSessionFactsAsync()` are not fully trusted. If they are only authored by operators and never by end users, the injection risk is lower but not eliminated.
- I assumed the active branch should be protected by CI. If branch-specific CI is intentionally disabled, that should be documented and compensated for elsewhere.

## Bottom line

The code is moving in the right direction, but the branch still carries legacy compatibility, stale metadata, and trust-boundary risks that should be removed before this becomes hard-to-reason-about technical debt. The most urgent fixes are CI branch coverage, metadata synchronization, and prompt/memory hardening. 
