# Agent Handoff — MemorySmith.Agent

**For:** Next agent session  
**From:** Session 2026-06-16 (seventh session — Phase 5b refactor + architecture review)  
**Repo:** https://github.com/TheMasonX/MemorySmith.Agent  
**CI:** All changes pushed; CI pending (fix addresses root cause: IChatLlmClient circular dep)

---

## What Was Done (TSK-0014)

### 1. CI Fix (immediate)
`IChatLlmClient` in `Agent.Core` referenced `ChatInterpretation` from `Agent.Planning` — a
project that `Agent.Core` cannot reference (circular). Fixed by replacing `IChatLlmClient.cs`
with a stub and creating `ILlmProvider` in `Agent.Planning.Llm` (returns `string?` only).

### 2. LLM Provider Abstraction

New `Agent.Planning/Llm/` directory:
- `ILlmProvider.cs` — returns `string?` raw completion
- `ChatOptions.cs` — unified config (`Agent:Chat` section)
- `OllamaProvider.cs` — full implementation (/api/chat)
- `OpenAICompatibleProvider.cs` — OpenAI/OpenRouter/DeepSeek/GitHub Copilot (/v1/chat/completions)
- `AnthropicProvider.cs` — Claude (/v1/messages)
- `GeminiProvider.cs` — Google Gemini (generateContent)
- `LlmProviderFactory.cs` — creates provider from `ChatOptions.LlmProvider` slug

### 3. Rate Limiter Fix
`ChatRateLimiter` rewritten:
- Per-player: configurable cooldown (default 3s) — unchanged
- Global: sliding-window **5 per minute** (was 1s — now correct) — configurable
- All values from `ChatOptions`

### 4. ChatOptions consolidation
Single `ChatOptions` class replaces `LlmOptions`:
- `LlmEnabled`, `LlmProvider`, `LlmModel`, `LlmBaseUrl`, `LlmApiKey`, `LlmTimeoutSeconds`
- `PlayerCooldownSeconds` (3), `GlobalPerMinuteMax` (5)
- `MaxMessageLength` (1024), `MaxResponseDistanceBlocks` (64), `ConversationWindowSeconds` (60)
Config section: `Agent:Chat`

### 5. Deprecated code removed
- `Agent.Core/Interfaces/IChatLlmClient.cs` → empty stub
- `Agent.Planning/OllamaLlmClient.cs` → empty stub
- `Agent.Planning/HtnTask.cs` → empty stub (final tombstone removal)
- `ChatInterpreter` sync `Interpret` method removed (async-only via IChatInterpreter)

### 6. LlmChatInterpreter updated
Uses `ILlmProvider` (not `IChatLlmClient`). Prompt building and `ParseDecision` moved here.

### 7. NUnit.Analyzers bumped: 4.4.0 → 4.14.0

### 8. Architecture review
`Data/Pages/architecture-review-20260616.md` — 13 sections covering all subsystems,
technical debt table (14 items with severity + phase), and phased recommendations.

---

## Current Architecture (quick ref)

```
Agent.Core       — models + interfaces (no LLM types)
Agent.Planning   — goals, HTN planner, chat system
  Llm/           — ILlmProvider, ChatOptions, 4 provider implementations, factory
  ChatModels.cs  — ChatInterpretation, ChatIntentType
  IChatInterpreter, ChatInterpreter, LlmChatInterpreter, ChatRateLimiter
Agent.Construction — blueprints
Agent.Memory     — MemorySmith REST
Agent.Tools      — 11 tools
Agent.World.Minecraft — Mineflayer bridge
WebUI.Blazor     — Minimal API, AgentBackgroundService, index.html
```

---

## Config Quick Reference

```json
"Agent": {
  "Enabled": true,
  "Chat": {
    "LlmEnabled": true,
    "LlmProvider": "ollama",
    "LlmModel": "llama3.2",
    "LlmBaseUrl": "",
    "LlmApiKey": null,
    "LlmTimeoutSeconds": 10,
    "PlayerCooldownSeconds": 3,
    "GlobalPerMinuteMax": 5,
    "MaxMessageLength": 1024,
    "MaxResponseDistanceBlocks": 64.0,
    "ConversationWindowSeconds": 60
  }
}
```

For cloud providers:
```json
"LlmProvider": "openai",  "LlmApiKey": "sk-...",   "LlmModel": "gpt-4o"
"LlmProvider": "anthropic","LlmApiKey": "sk-ant-...","LlmModel": "claude-3-5-sonnet-20241022"
"LlmProvider": "deepseek", "LlmApiKey": "...",       "LlmModel": "deepseek-chat"
"LlmProvider": "openrouter","LlmApiKey": "sk-or-...", "LlmModel": "mistralai/mistral-7b-instruct"
"LlmProvider": "gemini",   "LlmApiKey": "AIza...",   "LlmModel": "gemini-2.0-flash"
```

---

## Phase 6 Priority Items

| # | Item | Why |
|---|------|-----|
| 1 | Move LLM call off event loop | 5s stall blocks health/death events |
| 2 | MinecraftAdapter reconnect | Single disconnect ends the session |
| 3 | CraftItemTool pathfinding | 4-block range too tight |
| 4 | Crafting chain in DecomposeBuild | logs → planks → house automation |
| 5 | IItemRegistry TTL cache | 330 builds × HTTP per block = overload |
| 6 | Typed world events | Eliminate untyped Dictionary payloads |
| 7 | FindFlatAreaTool | Auto-select build origin |
| 8 | SignalR push | Real-time dashboard updates |
| 9 | ChatRateLimiter.Prune() call | Memory growth in long sessions |
| 10| LLM chat history | Multi-turn context window |