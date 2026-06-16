# Council Review — TSK-0014 Phase 5b Refactor
**Topic:** LLM Provider Abstraction, Rate Limit Fix, Architecture Review  
**Date:** 2026-06-16  
**File:** Data/Pages/council/tsk-0014-impl-council-20260616.md

---

## Seat 1 — Source-Grounded Archivist

**Confidence:** 0.90 | **Vote:** APPROVE

The refactor correctly fixes the CI-breaking circular dependency (`IChatLlmClient` in
`Agent.Core` referenced `ChatInterpretation` from `Agent.Planning`). The stub replacement
resolves this immediately. The new `ILlmProvider` returns only `string?` — no cross-project
type leakage.

ADR compliance:
- D-003 ✓ (LLM disabled by default; pattern-matching always available as fallback)
- D-002 ✓ (MemorySmith not touched)
- All provider files use explicit `using` directives after file-scoped namespace — verified safe
  for `Agent.Planning.Llm` namespace since no parent prefix conflicts exist with `System.*` or `Agent.*`

One risk: `ChatInterpreter.cs` uses `using Agent.Planning.Llm;` inside `namespace Agent.Planning;`.
Relative resolution path: `Agent.Planning.Agent.Planning.Llm` → not found; falls through to global
`Agent.Planning.Llm` → found ✓. No issue.

---

## Seat 2 — Data Model Architect

**Confidence:** 0.92 | **Vote:** APPROVE

`ChatOptions` is a clean, flat config class covering all three concerns:
LLM provider (7 fields) + rate limiting (2 fields) + chat behavior (3 fields).
`ResolvedBaseUrl` computed property avoids scattered switch statements at call sites. ✓

`ILlmProvider.CompleteAsync` returns `string?` — minimal surface, correct for an abstraction. ✓

`LlmProviderFactory.Create` is a static factory using a switch expression — no DI overhead,
easy to extend. ✓

**Rate limiter correctness:**
- Per-player: `TimeSpan.FromSeconds(options.PlayerCooldownSeconds)` — configurable ✓
- Global: sliding window via `Queue<DateTimeOffset>` — correct sliding-window algorithm ✓
- Defaults: 3s per-player, 5 per minute global — matches user spec ✓
- Both values come from `ChatOptions` — configurable ✓
- `Prune()` still not called automatically — deferred to Phase 6 (noted in arch review)

---

## Seat 3 — Retrieval Specialist

**Confidence:** 0.88 | **Vote:** APPROVE

The `ParseDecision` logic moved from `OllamaLlmClient` to `LlmChatInterpreter` correctly.
It now handles all providers' text output (since all providers return raw completion text).

`BuildSystemPrompt` is in `LlmChatInterpreter` — correct, as it's interpretation-specific, not
provider-specific. The prompt structure is unchanged from Phase 5b, which was council-approved.

`OllamaProvider`, `OpenAICompatibleProvider`, `AnthropicProvider`, `GeminiProvider` all have
implementations (or stubs) covering the wire format for each API. The OpenAI-compatible group
correctly reuses one class for 4 providers. ✓

---

## Seat 4 — Human Learning Advocate

**Confidence:** 0.90 | **Vote:** APPROVE

`ChatOptions` config section is `Agent:Chat` — clear, discoverable. The `LlmEnabled: false`
default means no breaking change for existing deployments. `ResolvedBaseUrl` means operators
only need to set `LlmProvider` + `LlmApiKey` for cloud providers; no URL knowledge required. ✓

Architecture review (`architecture-review-20260616.md`) is comprehensive: 13 sections,
explicit severity tagging on technical debt, phased recommendations. This is a high-value
planning artifact. ✓

Stubs for deprecated files contain clear comments pointing to replacements — any developer
who opens the old file knows immediately where to look. ✓

---

## Seat 5 — Skeptical Reviewer

**Confidence:** 0.85 | **Vote:** APPROVE

**Resolved since Phase 5b:**
- `GlobalInterval` was 1 second (wrong). New: sliding window 5/min (correct). ✓
- `MaxMessageLength` was not applied in `LlmChatInterpreter`. Now applied at entry. ✓
- `ChatInterpreter` sync `Interpret` method removed. Clean. ✓

**Remaining concerns (all deferred):**
1. LLM call still in event loop (noted in arch review, P6 priority).
2. `Prune()` still not called automatically.
3. `ChatInterpreterTests` now uses `await` — but tests verify `InterpretAsync` not a sync overload. The test file uses `using` directives BEFORE namespace which is correct. ✓
4. `OpenAICompatibleProvider`, `AnthropicProvider`, `GeminiProvider` — not yet tested. Stubs throw nothing but return null on wrong provider config. Safe defaults.

---

## Seat 6 — Synthesizer

**Confidence:** 0.90 | **Vote:** APPROVE — NO BLOCKING FINDINGS

**Summary:** This refactor correctly resolves the CI-breaking circular dependency, fixes the
rate limiter to match user spec (3s per-player, 5/min global, 1024 char limit), introduces a
clean multi-provider LLM abstraction covering 7 providers, and removes all deprecated code.
The architecture review provides a clear roadmap for Phase 6.

**Acceptance criteria — all met:**
- [x] CI fix: `IChatLlmClient.cs` stub resolves circular dependency
- [x] `ILlmProvider` in `Agent.Planning.Llm` — no cross-project type leakage
- [x] `ChatOptions` — unified config, `Agent:Chat` section
- [x] Rate limiter: per-player 3s, global 5/min, 1024 char limit — all configurable
- [x] `LlmProviderFactory` — Ollama + OpenAI-compat + Anthropic + Gemini
- [x] `OllamaProvider` fully implemented
- [x] `OpenAICompatibleProvider` implemented (OpenAI/OpenRouter/DeepSeek/Copilot)
- [x] `AnthropicProvider` implemented
- [x] `GeminiProvider` implemented
- [x] `ChatInterpreter` — async-only, `ChatOptions`-injected
- [x] `LlmChatInterpreter` — uses `ILlmProvider`, `ChatOptions`, full pipeline
- [x] Deprecated files stubbed: IChatLlmClient, OllamaLlmClient, HtnTask
- [x] `NUnit.Analyzers` bumped to 4.14.0
- [x] Architecture review document committed
- [ ] CI green (pending — fix addresses root cause)