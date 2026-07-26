# Audit Synthesis & Sprint 61/62 Plan (2026-07-26)

Summary: Consolidated findings from the 5-agent audit sweep. Prioritize security and observability fixes in Sprint 61, begin a slice-based runtime migration, and schedule adapter/deduplication work in Sprint 62.

Sprint 61 (high-priority)
- TSK-0412 — Slice 1: Extract `IDashboardPublisher` and wire dashboard publishes out of `AgentBackgroundService` (Critical)
- TSK-0414 — Mitigate LLM prompt-injection: escape/strip control characters, wrap untrusted text in strict delimiters, and add LLM-output schema validation (Critical)
- TSK-0415 — Unify LLM JSON extraction and add parse-failure logging/metrics (High)
- TSK-0416 — Secure SignalR `/agent-hub` with API-key parity and integration tests (High)
- TSK-0415a — Replace Debug.WriteLine usage in adapters with ILogger and add banned-API analyzer (High)
- TSK-0417 — Cache tool `InputSchema` parse results to eliminate repeated `JsonDocument.Parse` in hot dispatch (Medium)

Sprint 62 (medium-priority)
- Complete remaining ABS migration slices (IStateManager, IExecutionManager, IPlanningManager, IRecoveryManager)
- Deduplicate MineflayerAdapter (replace vec3 shim, extract helpers, reset Movements on reconnect)
- Collapse shadow-copied domain dictionaries to `CommonMinecraftBlocks` and add a CI check
- Backlog housekeeping: merge/close duplicate tasks and update statuses (TSK-0424)

Notes:
- Each Sprint 61 task must include a small test/validation gate (unit + smoke/integration where applicable). Do not proceed to next slice of ABS migration until acceptance criteria for the prior slice pass in staging.

Planning note path: Data/Pages/Audits/audit-synthesis-sprint61-62-plan-20260726.md
# Audit Synthesis Sprint 61/62 Plan

## Scope
This note captures the durable backlog items derived from the 2026-07-15 through 2026-07-23 external audit corpus for MemorySmith.Agent. It focuses on findings that remain valid against the current repo state and that are not already fully covered by the existing task history.

## Sprint 61 candidates

1. TSK-0412 — Make the observe/decide/execute/verify/recover workflow explicit
   - Goal: Create explicit workflow contracts and replay metadata for the autonomous loop.
   - Why now: the audits repeatedly show that behavior is still distributed across service chains rather than owned by a single workflow object.

2. TSK-0413 — Add lifecycle health contracts for runtime services and adapters
   - Goal: Make service readiness, liveness, reconnect, and shutdown ordering explicit and testable.
   - Why now: several findings point to unclear ownership and hidden ordering dependencies across runtime components.

3. TSK-0414 — Enforce planner preconditions and failure-state propagation end to end
   - Goal: Ensure gather/craft/smelt flows cannot silently continue from stale or invalid state.
   - Why now: the planning-policy and execution-context audits show that the policy surface exists but is not consistently enforced in the live path.

4. TSK-0415 — Standardize boundary-failure contracts for adapter, memory, and LLM calls
   - Goal: Introduce consistent timeout, cancellation, and recovery semantics across external boundaries.
   - Why now: the current runtime handles failures inconsistently and often suppresses them too early.

## Sprint 62 candidate

5. TSK-0416 — Add scenario-based regression suites and replay support for autonomy failures
   - Goal: Add deterministic regression scenarios for stale memory, interrupted operations, partial success, and world-state drift.
   - Why now: the audits point to missing scenario coverage for long-running autonomous loops, not just unit-level bug fixes.

## Notes
- This plan intentionally avoids re-creating work already covered by TSK-0181, TSK-0350, TSK-0402, TSK-0405, TSK-0406, TSK-0346, TSK-0348, TSK-0364, TSK-0365, TSK-0374, TSK-0392, TSK-0393, TSK-0396, and TSK-0401.
- The tasks are framed as durable backlog items that can be split into implementation waves during the sprint.
