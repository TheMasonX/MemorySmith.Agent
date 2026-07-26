## Council Review: Runtime Migration & LLM Hardening

**Decision**: Proceed with a slice-based migration of `AgentBackgroundService` to the manager/`ExecutionContext` architecture and immediate hardening of critical LLM/runtime security gaps (prompt sanitization, evaluator observability, and SignalR auth). Implement via discrete, test-gated slices starting with dashboard extraction and prompt-injection mitigations in Sprint 61.

**Evidence Reviewed**
- [Data/Pages/Audits/msa-capstone-architecture-requirements-audit-26-07-21-23-47-31.md](Data/Pages/Audits/msa-capstone-architecture-requirements-audit-26-07-21-23-47-31.md)
- [Data/Pages/Audits/msa-bloat-duplication-audit-26-07-15-01-32-34.md](Data/Pages/Audits/msa-bloat-duplication-audit-26-07-15-01-32-34.md)
- [Data/Pages/Audits/msa-corpus-reconciliation-part1-council-verdict-26-07-22-02-50-27.md](Data/Pages/Audits/msa-corpus-reconciliation-part1-council-verdict-26-07-22-02-50-27.md)
- [Data/Pages/Audits/msa-corpus-reconciliation-part3-signalr-auth-record-equality-26-07-23-20-49-07.md](Data/Pages/Audits/msa-corpus-reconciliation-part3-signalr-auth-record-equality-26-07-23-20-49-07.md)
- [Data/Pages/Audits/msa-regression-reinvented-library-delta-audit-26-07-19-22-59-14.md](Data/Pages/Audits/msa-regression-reinvented-library-delta-audit-26-07-19-22-59-14.md)
- Audit synthesis: [Data/Pages/Audits/audit-synthesis-sprint61-62-plan-20260726.md](Data/Pages/Audits/audit-synthesis-sprint61-62-plan-20260726.md)

## Findings (Seat-by-seat)
| Seat | Recommendation | Confidence | Blocking concern |
|---|---|---:|---|
| Source-Grounded Archivist | Prioritize wiring `ExecutionContext` and retiring unused manager scaffolding; require migration ledger entries and removal milestones for compatibility shims. | 0.94 | Need owner assignment for each shim/migration slice. |
| Data Model Architect | Make structural contract changes (WorldState.Facts read-only, BlockState structural equality) and version cross-boundary contracts before large refactors. | 0.88 | Blocking if downstream consumers require breaking changes; require migration scripts/tests. |
| Skeptical Reviewer | Do not attempt a big-bang refactor; accept slice-based migration with tight test/integration gates; immediate P0 fixes (LLM prompt injection, SignalR auth, LLM parse logging) must run first. | 0.90 | Blocking if prompt-injection & SignalR auth are not fixed first — they are security-critical. |

## Synthesis
**What changes now**
- Sprint 61: (a) Immediate security/observability fixes — prompt sanitization & delimiter wrapper, shared LLM JSON extractor + parse-failure logging/metrics, require auth for SignalR hub; (b) Slice 1 migration: extract `IDashboardPublisher` and wire dashboard publish paths out of `AgentBackgroundService`; (c) Add banned-API rule replacing `Debug.WriteLine` with `ILogger` for adapter logs.

**What is deferred**
- Sprint 62: Complete remaining migration slices (IStateManager, IExecutionManager, IPlanningManager, IRecoveryManager), deduplicate domain-data and adapter helper refactors, backlog hygiene and task merges.

**Evidence gates required before each slice**
- Unit tests asserting unchanged observable behavior for migrated features.
- Integration tests validating SignalR rejects unauthenticated handshakes and that prompt sanitization prevents injection test vectors.
- Performance/allocations baseline before/after schema caching change for tool InputSchema parsing.

## Dissent
- The Skeptical Reviewer warns against scheduling large migration slices in parallel without proven small-slice acceptance criteria; they request the security P0 items be completed and verified in staging before proceeding to further slices. This dissent is recorded and treated as blocking for slice 2+ until gates pass.

## Acceptance Criteria
- `IDashboardPublisher` implemented and wired; dashboard pushes removed from `AgentBackgroundService` with regression tests passing. 
- LLM prompt-injection mitigations in place: escaping or delimiter-enforced user text plus JSON-output schema validation and unit tests that include malicious injection testcases.
- SignalR `/agent-hub` requires the same API-key authorization as `/api/*`, verified by an automated integration test.
- Logging/metrics: evaluator parse failures emit structured logs with truncated snippets and expose a counter; alert threshold documented.
- No `System.Diagnostics.Debug.WriteLine` calls remain in production code (banned-API analyzer configured).

## Open Questions
- Who will be the migration owner for the ExecutionContext wiring (person or team)?
- Can we reserve 2 integration-test nodes to validate SignalR and LLM hardening in staging this sprint? 
- Do downstream consumers rely on mutable `WorldState.Facts` in ways that require migration tooling? If so, which callers must be migrated first?

---
Chair synthesis: adopt the recommendations above. Begin Sprint 61 with the security and slice-1 tasks; defer further slices until acceptance criteria are met.
