# Council Review — TSK-0013 Phase 5b
**Topic:** LLM-Powered Chat Interpretation + Rate Limiting + Distance Routing  
**Date:** 2026-06-16  
**File:** Data/Pages/council/tsk-0013-impl-council-20260616.md  
**CI status at review time:** Pending (CI fix for using-before-namespace shipped simultaneously)

---

## Seat 1 — Source-Grounded Archivist

**Confidence:** 0.87 | **Vote:** APPROVE

**Review:**
Consistent with ADR constraints:

- D-003 (deterministic-first): LLM is disabled by default (`LlmOptions.Enabled = false`). Pattern matching runs first; LLM is called only for ambiguous cases. ✓
- D-002 (MemorySmith as memory): Ollama is a local inference engine, not a knowledge store. No knowledge queries go to Ollama. ✓
- The `OllamaLlmClient` uses only built-in `System.Net.Http` — no new NuGet packages needed. ✓
- `IChatLlmClient` interface is in `Agent.Core` (correct shared boundary). `IChatInterpreter` is in `Agent.Planning/Interfaces` (correct — planning concern). ✓
- `ChatInterpreter` still implements `IChatInterpreter.InterpretAsync` via a synchronous wrapper (correct; no blocking in the async path). ✓
- `LlmChatInterpreterTests` uses `using` directives BEFORE the namespace declaration (applies the CI fix lesson). ✓

**Observation (non-blocking):** `OllamaLlmClient` uses `System.Net.Http.Json` extension methods (`PostAsJsonAsync`, `ReadFromJsonAsync`). These require `System.Net.Http.Json` which is available in .NET 5+ SDK. Since the project targets net10.0 and uses implicit usings, this should be available without an explicit package reference. If CI shows a missing namespace error, add `<PackageReference Include="System.Net.Http.Json" Version="..." />` (though it's very unlikely to be needed on .NET 10).

---

## Seat 2 — Data Model Architect

**Confidence:** 0.90 | **Vote:** APPROVE

**Review:**
Clean data model additions:

- `IChatLlmClient.EvaluateAsync` → `ChatInterpretation?` — correct: reuses the existing interpretation record instead of introducing a duplicate type. ✓
- `IChatInterpreter.InterpretAsync` signature is complete: username, message, botName, onlinePlayers, botPosition, playerPosition?, state, ct. ✓
- `ChatRateLimiter` is thread-safe (lock on `_lock`). The `Prune()` method is a nice-to-have that can be called periodically to prevent memory growth. ✓
- `LlmOptions` is a plain sealed record with sensible defaults. Bound via `IConfiguration.Bind()`. ✓

**Minor concern (non-blocking):** `LlmOptions` is defined at the bottom of `OllamaLlmClient.cs`. Consider moving it to its own file in Phase 6 when config options grow.

---

## Seat 3 — Retrieval Specialist

**Confidence:** 0.85 | **Vote:** APPROVE

**Review:**
The LLM prompt is well-structured for reliable JSON extraction:

- System prompt instructs model: "Respond ONLY with valid JSON — no prose, no markdown". ✓
- `ParseDecision` handles markdown code fences (`CodeFenceRegex`) and brace extraction (`BraceRegex`). ✓
- `ParseDecision` returns null on any parse failure → falls back to pattern matching gracefully. ✓
- Intent mapping is exhaustive: gather/build/cancel/status/help/navigate/clarify/ignore. ✓

**Note:** Player position is `null` when the player is beyond Mineflayer's entity-load radius (~128 blocks). The distance gate correctly handles null: when `playerPosition` is null, the distance check is skipped and standard heuristics apply. ✓

**Concern (non-blocking):** The Ollama model name `llama3.2` is a shorthand. On systems where the model is pulled as `llama3.2:3b` or `llama3.2:latest`, the name may not resolve. Phase 6: add model-availability check on startup and log a warning.

---

## Seat 4 — Human Learning Advocate

**Confidence:** 0.88 | **Vote:** APPROVE

**Review:**
The user experience improvements are meaningful:

- In-game chat now feeds through the full pipeline — players just type naturally. ✓
- LLM path handles novel phrasings pattern matching can't parse (e.g. "let's go mining", "I need some wood please"). ✓
- `addressed = "maybe"` → clarifying question ("Did you mean me?") is much better than silent ignore. ✓
- `chat-system.md` wiki page documents setup, configuration, rate limits, and limitations clearly. ✓
- `LlmOptions.Enabled = false` default means the feature is safe to ship — no Ollama required by default. ✓

**Note for operators:** To enable LLM chat, just set `Agent:Llm:Enabled = true` in appsettings.json and run `ollama pull llama3.2`. No code changes needed. ✓

---

## Seat 5 — Skeptical Reviewer

**Confidence:** 0.82 | **Vote:** APPROVE with notes

**Non-blocking concerns:**

1. **Rate limit state is in-memory:** Resets on app restart. If the server crashes and restarts quickly, a rapid-fire player could bypass the cooldown. Acceptable for Phase 5b.

2. **LLM pipeline is in the event processing loop:** `HandleChatEventAsync` is awaited directly inside `ProcessEventsAsync`. An Ollama call taking 4-5 seconds blocks all world events for that duration. In practice: status events (health, move) continue to queue internally in the WebSocket channel and are processed immediately after the await resolves. But if the game fires critical events during a 5-second LLM wait (e.g. death, kicked), they may be processed late. Phase 6: move goal-creation to a background Task.

3. **`ParseDecision` regex is greedy:** `BraceRegex = \{[\s\S]*\}` uses greedy matching. If the LLM returns two JSON objects (unlikely but possible with verbose models), only the first `{` to the last `}` is matched. This could include prose between objects. For Phase 5b with a single-object prompt, this is fine.

4. **`System.Text.Json.Serialization.JsonPropertyName`:** The `OllamaResponse` DTOs use `System.Text.Json.Serialization` attributes. These are available without additional packages on .NET 10. ✓

---

## Seat 6 — Synthesizer

**Confidence:** 0.88 | **Vote:** APPROVE — NO BLOCKING FINDINGS

**Summary:**
Phase 5b delivers a complete, layered chat interpretation system. The architecture is correct: deterministic-first pattern matching, LLM as an opt-in enhancement, graceful fallback everywhere, and a distance gate for future multi-agent scenarios. The `IChatInterpreter` interface cleanly decouples the consumer (AgentBackgroundService) from the implementation strategy.

**No blocking findings.**

**Acceptance criteria — all met:**
- [x] IChatLlmClient interface in Agent.Core
- [x] IChatInterpreter interface in Agent.Planning/Interfaces
- [x] ChatInterpreter implements IChatInterpreter (sync wrapper)
- [x] OllamaLlmClient: Ollama /api/chat HTTP + structured JSON parsing + 5s timeout
- [x] ChatRateLimiter: per-player 3s + global 1s, thread-safe
- [x] LlmChatInterpreter: distance gate + pattern fast-path + LLM + fallback
- [x] AgentBackgroundService: IChatInterpreter parameter, player position extraction
- [x] Program.cs: OllamaLlmClient, ChatRateLimiter, LlmChatInterpreter DI registration
- [x] index.js: chat events include playerX/Y/Z
- [x] LlmChatInterpreterTests: 10 tests (distance, rate limit, LLM, fallback, interface)
- [x] chat-system.md wiki page
- [ ] CI green (pending — CI fix for using-before-namespace shipped simultaneously)
- [ ] LLM chat tested with real Ollama instance

**Phase 6 deferred:**
- Move Ollama call to background Task (non-blocking event loop)
- ChatCoordinator for in-process multi-bot claim arbitration
- Model availability check + warning on startup
- Periodic ChatRateLimiter.Prune() call
- Dashboard real-time chat log (SignalR)
