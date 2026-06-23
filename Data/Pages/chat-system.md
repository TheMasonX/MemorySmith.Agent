# Chat Interpretation System

**Status:** Active (Sprint 41)  
**ADR compliance:** D-003 (deterministic-first; LLM is opt-in), D-011 (parsers never create goals)

---

## Overview

The agent interprets Minecraft in-game chat and turns it into agent goals. All chat received by the Mineflayer bot is forwarded to C# as a `ChatEvent`. `AgentBackgroundService.HandleChatEventAsync` routes it through the `IChatInterpreter` pipeline.

The agent responds in-chat when it determines the message is for it, and can ask clarifying questions when uncertain.

---

## Pipeline

```
Player speaks in Minecraft chat
         ↓
Mineflayer bot.on('chat', ...)  [username, message, onlinePlayers, playerPos]
         ↓
System message filter (index.js) — SYSTEM_MESSAGE_PATTERNS
  blocks: teleport, join/leave, server messages, /clear, /give
         ↓
WebSocket → AgentBackgroundService.HandleChatEventAsync
         ↓
IChatInterpreter.InterpretAsync(username, message, botName, onlinePlayers, botPos, playerPos, state)
  → LlmChatInterpreter:
     1. Max length truncation (configurable)
     2. Distance gate (configurable MaxResponseDistanceBlocks)
     3. Fast-path: only cancel/status/help (Sprint 35 P1-B — all other fast-paths removed)
     4. Rate limit check (per-player 3s cooldown, global 1s minimum)
     5. ILlmProvider.CompleteAsync with enriched system prompt
        (tool names from ToolDispatcher.All + top 8 inventory + HP/food + active goal)
     6. ParseDecision → IntentDraft JSON
     7. TryParseTruncatedJson recovery if JSON cut off
     8. Fallback: ChatInterpreter pattern matching
         ↓
  Returns IntentDraft? (null = not addressed to bot)
         ↓
AgentBackgroundService routes by IntentDraft.Intent:
  cancel       → CancelGoal()
  status/help  → Log response
  continue/resume → Reset governor, force replan, enqueue GetStatus
  gather/build/craft → IntentManager.BuildGoalRequest → GoalFactory.CreateAsync → SetGoal
  navigate     → CancelGoal + enqueue MoveTo
  conversation → Log only
  clarify      → LLM clarification question (if confidence < threshold)
```

## CRITICAL Rule (ADR D-011): Parsers Never Create Goals

**The interpreter returns `IntentDraft` — it never creates `IGoal` objects directly.** Only `AgentBackgroundService` (via `IntentManager` + `GoalFactory`) creates goals. This prevents fast-path LLM bypass and ensures all goals go through confidence scoring and context enrichment. The `CraftRegex` and `CreateGoal` fast-paths that directly produced goals were removed in Sprint 35 P1-B/D.

## IntentDraft Schema (Sprint 35 P1-A)

```json
{
  "addressed":       "yes | maybe | no",
  "intent":          "gather | build | craft | navigate | cancel | status | help | conversation | clarify | ignore",
  "item":            "oak_log",
  "blueprint":       "small-house",
  "count":           null | 32,
  "x": null | 100, "y": null | 64, "z": null | 200,
  "confidence":      0.0–1.0,
  "clarificationQuestion": "Did you mean the oak forest near spawn?",
  "response":        "Ok, gathering 32 oak logs!"
}
```

- **`confidence`** — must be present; default 1.0 if absent. `LlmConfidenceThreshold` (default 0.6) gates clarification.
- **No `goalName` field** — goal mapping is done by `IntentManager`, not the interpreter.
- Truncated JSON is recovered via `TryParseTruncatedJson`.

## IntentManager Mapping

`IntentDraft` → typed `GoalRequest` → `GoalFactory.CreateAsync` → `IGoal`:

| Intent | GoalRequest | Details |
|--------|-------------|---------|
| `gather` + item | `GatherGoalRequest` | itemSpec + count |
| `build` + blueprint | `BuildGoalRequest` | blueprint slug (aliases resolved via BlueprintAliases dict), optional coords |
| `craft` + item | `CraftGoalRequest` | itemSpec + count |
| `navigate` + coords | `NavigateGoalRequest` | target x/y/z |

**Blueprint aliases** (Sprint 41): `"house"` → `"small-house"`, `"tower"` → `"wizards-tower"`, etc. Resolved in `IntentManager` before creating `BuildGoalRequest`.

## Fast-Path vs LLM

### Fast-Path (ChatInterpreter — deterministic)
Uses regex patterns. **Only handles these intents directly** (Sprint 35 P1-B):
- `cancel` / `stop` / `quit` / `abort` → CancelGoal
- `status` / `what are you doing` → Status
- `help` / `commands` → Help

All other intents (gather, build, craft, navigate) are routed through the LLM.

### LLM Path (LlmChatInterpreter)
Called when fast-path doesn't match or returns low confidence. The LLM receives:
- Enriched system prompt with registered tool names (Sprint 36 P1-C)
- Current inventory summary (top 8 items) + HP/food + active goal (Sprint 35 P1-C)
- IntentDraft JSON schema as output format
- Chat history context

Returns JSON IntentDraft. If confidence < `LlmConfidenceThreshold` (default 0.6) AND clarificationQuestion exists → bot asks clarifying question.

## Directed-at-Bot Heuristics

| Condition | Rule |
|-----------|------|
| Solo play | `onlinePlayers <= 1` → always addressed |
| Named explicitly | Message starts with bot username (case-insensitive) |
| Active conversation | Bot spoke within the last 60 seconds |

## Configuration (appsettings.json)

```json
{
  "Agent": {
    "Llm": {
      "Enabled": true,
      "OllamaUrl": "http://localhost:11434",
      "Model": "llama3.2",
      "LlmTimeoutSeconds": 10,
      "LlmMaxResponseTokens": 300,
      "PlayerCooldownSeconds": 3,
      "MaxResponseDistanceBlocks": 64,
      "LlmConfidenceThreshold": 0.6
    }
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `Enabled` | `false` | Enable LLM interpretation |
| `Model` | `llama3.2` | Model to use |
| `LlmTimeoutSeconds` | `10` | Timeout per LLM call (Sprint 11) |
| `LlmMaxResponseTokens` | `300` | Max response tokens via `num_predict` (Sprint 20) |
| `PlayerCooldownSeconds` | `3` | Per-player rate limit |
| `MaxResponseDistanceBlocks` | `64` | Distance gate threshold |
| `LlmConfidenceThreshold` | `0.6` | Minimum confidence to skip clarification |

### LLM Truncation Recovery (Sprint 20)

When the LLM truncates JSON before the closing `}`, `TryParseTruncatedJson` regex-extracts `intent`, `item`, `count`, `blueprint` from partial output. `num_predict = 300` prevents most truncation.

## System Message Filtering (index.js)

```js
const SYSTEM_MESSAGE_PATTERNS = [
  /teleported/i, /joined the game/i, /left the game/i,
  /^\[Server\]/i, /^You are now/i,
  /^\/clear/i, /Removed \d+ item/i,
  /^Cleared\s+(?:\d+|\S+'s|the\s+inventory)/i, /^\/give/i
];
```

**Known issue:** Formatting variations can leak through (see Intent Parsing Issues memory).

## Rate Limiting

| Limit | Value |
|-------|-------|
| Per-player cooldown | 3 seconds |
| Global minimum interval | 1 second |

When rate-limited, falls back to pattern matching without delay.

## Logging & Observability

| Event | Level | Content |
|-------|-------|---------|
| Intent resolved | `Information` | `[chat] <Username> -> gather 10 oak_log` |
| Thinking indicator | `Information` | `[thinking] LLM slow — EnqueueThinkingIfSlowAsync fired` |
| Rate limit | `Information` | `Rate limited <username>: 3s cooldown` |
| LLM timeout | `Warning` | `LLM call timed out after 10s for <username>` |

## Known Limitations (Sprint 40-41)

1. **llama3.2:3b is too small** — misclassifies disagreement as "ignore". Upgrade to 7B+ recommended.
2. **System message filter leaks** — `/clear` responses with formatting variations reach LLM.
3. **Clarify uses hardcoded response** instead of LLM-generated question.
4. **"inv" / "inventory" fast-path** can bypass LLM when user wants refresh, not report.

## Related

- [Intent Parsing Issues](Memories/Core/agent-intent-parsing-issues.json)
- [Chat Pipeline Core Memory](Memories/Core/agent-chat-interpretation-pipeline.json)
- [Chat Interpretation Feature Page](Features/chat-interpretation.md)
- [AGENTS.md Rule A-1](../AGENTS.md)
