# Sprint 53: Security Hardening & Critical Test Coverage

**Date:** 2026-06-27  
**Parent audit:** [council-review-msa-audit-6-27-26.md](../Audit/council-review-msa-audit-6-27-26.md)  
**Status:** Planned  
**Capacity:** 6 days (1 day buffer)

---

## Sprint Objective

Close all P0 security and thread-safety gaps, and establish test coverage for the LLM critical path. No release ships with P0 findings open.

---

## Committed Items (P0 + P1)

### Wave A: Security Hardening (Days 1-3)

| Task ID | Description | Effort | Depends On |
|---------|-------------|--------|------------|
| [TSK-0180](tsk-0180-gemini-api-key-header.md) | Move Gemini API key from query string to HTTP header | S | — |
| [TSK-0181](tsk-0181-signalr-auth.md) | Add API key authentication to SignalR `/agent-hub` | S | — |
| [TSK-0182](tsk-0182-llm-provider-error-body-logging.md) | Log HTTP error bodies in OpenAI/Anthropic/Gemini providers | S | — |
| [TSK-0183](tsk-0183-player-chat-sanitization.md) | Sanitize player messages before LLM (strip injection markers) | M | — |
| [TSK-0184](tsk-0184-api-key-log-masking.md) | Mask API keys in log output | S | — |
| [TSK-0185](tsk-0185-createpage-toolresult.md) | `CreatePageTool`: return `ToolResult(false,...)` instead of throw | S | — |

### Wave B: Thread Safety (Days 3-5)

| Task ID | Description | Effort | Depends On |
|---------|-------------|--------|------------|
| [TSK-0186](tsk-0186-thread-safe-shared-fields.md) | Synchronize `_currentGoal`, `_consecutiveFailures`, `_connectionStatus` | M | — |
| [TSK-0187](tsk-0187-worldstate-lock.md) | Route `_worldState` reads through `IStateManager.Current` | M | TSK-0186 |
| [TSK-0188](tsk-0188-dashboard-publisher-sync.md) | Synchronize `DashboardPublisherImpl` fields | M | TSK-0186 |

### Wave C: Critical Test Coverage (Days 4-7)

| Task ID | Description | Effort | Depends On |
|---------|-------------|--------|------------|
| [TSK-0189](tsk-0189-chat-rate-limiter-tests.md) | Add `ChatRateLimiter.TryAcquire` tests | M | — |
| [TSK-0190](tsk-0190-llm-provider-factory-tests.md) | Add `LlmProviderFactory.Create` tests | S | — |
| [TSK-0191](tsk-0191-llm-provider-tests.md) | Add tests for all 4 LLM providers (Ollama, OpenAI, Anthropic, Gemini) | L | TSK-0180, TSK-0182 |
| [TSK-0192](tsk-0192-agent-background-error-tests.md) | Add `AgentBackgroundService` error-path tests | L | TSK-0186 |
| [TSK-0193](tsk-0193-search-memory-error-handling.md) | Add error handling to `SearchAsync` + `SearchMemoryTool` | S | — |
| [TSK-0194](tsk-0194-http-retry-resilience.md) | Add HTTP retry/resilience for MemorySmith clients | M | — |

---

## Stretch Items (if capacity allows)

- [TSK-0195](tsk-0195-coverage-threshold-ci.md) Add minimum coverage threshold to CI (40% line)
- [TSK-0196](tsk-0196-fix-version-drift.md) Fix version drift (README, `/api/about`, wiki pages)

---

## Exit Criteria

- [ ] All 4 P0 findings resolved with passing tests
- [ ] All 8 P1 findings resolved with passing tests
- [ ] LLM critical path has test coverage (providers + factory + rate limiter)
- [ ] Player chat is sanitized before reaching LLM
- [ ] API keys never appear in URLs or unmasked logs
- [ ] SignalR hub requires authentication
- [ ] `AgentBackgroundService` shared fields are thread-safe
- [ ] MemorySmith HTTP clients have retry/resilience
- [ ] 0 new build warnings
- [ ] All 742+ existing tests continue to pass
- [ ] CI green

---

## Capacity Assumptions

- 1 developer × 6 days focused
- S = <2 hours, M = 2-4 hours, L = 4-8 hours
- 1 day buffer for council review + CI fixes

---

## Risks

| Risk | Mitigation |
|------|-----------|
| Thread-safety fix introduces deadlock | Use simple `lock` patterns; stress test with concurrent access |
| LLM provider test mocks diverge from real API | Smoke test against real endpoints where possible |
| Sanitization too aggressive breaks legitimate chat | Test with Minecraft chat corpus |
| Effort estimates wrong by 2x | 1-day buffer; defer stretch items |

---

## References

- Council review: [council-review-msa-audit-6-27-26.md](../Audit/council-review-msa-audit-6-27-26.md)
- Original audit: [msa-audit-6-27-26.md](../Audit/msa-audit-6-27-26.md)
- AGENTS.md rules: `d:\@Repos\MemorySmith.Agent\AGENTS.md`
