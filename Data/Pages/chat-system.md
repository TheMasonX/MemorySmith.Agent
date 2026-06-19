# Chat Interpretation System

**Introduced:** Phase 5b (TSK-0013)  
**Status:** Active  
**ADR compliance:** D-003 (deterministic-first; LLM is opt-in with graceful fallback)

---

## Overview

The MemorySmith.Agent interprets Minecraft in-game chat and turns it into agent goals.
All chat received by the Mineflayer bot is forwarded to C# as a `chat` WorldEvent.
`AgentBackgroundService` routes it through the `IChatInterpreter` pipeline.

The agent responds in-chat when it determines the message is for it, and can ask
clarifying questions when uncertain.

---

## Pipeline

```
Player speaks in Minecraft chat
         ↓
Mineflayer bot.on('chat', ...)
  • includes: username, message, onlinePlayers, playerX/Y/Z
         ↓
System message filter (index.js) — 9 SYSTEM_MESSAGE_PATTERNS
  • blocks: teleport, join/leave, server messages, /clear responses, /give responses
  • passes through: normal player chat only
         ↓
WebSocketBridge parses JSON → WorldEvent{EventType="chat"}
         ↓
AgentBackgroundService.HandleChatEventAsync
  • logs resolved intent after interpretation: [chat] <Username> -> CreateGoal (CraftItem:iron_pickaxe)
         ↓
IChatInterpreter.InterpretAsync(username, message, botName, onlinePlayers, botPos, playerPos, state)
         ↓
    LlmChatInterpreter (default)
    ├─ 1. Distance gate: if player > 64 blocks away AND not named this bot → NotAddressed
    ├─ 2. CraftRegex fast-path: "craft/forge/smelt <item>" → CraftItem goal (no LLM call)
    ├─ 3. Pattern fast-path: ChatInterpreter returns confident CreateGoal/CancelGoal/Help → use it
    ├─ 4. Rate limit check: per-player 3s cooldown + global 1s minimum
    │      logs: "Rate limited <username>: cooldown 3s remaining (max 1/min global)"
    ├─ 5. OllamaLlmClient.EvaluateAsync (10-second timeout via linked CancellationToken)
    │      └─ POST http://localhost:11434/api/chat → structured JSON response
    │      (TryParseTruncatedJson recovery if JSON cut off before closing })
    └─ 6. Fallback: ChatInterpreter pattern-matching
         ↓
ChatInterpretation { IntentType, GoalName, GoalParameters, Response }
         ↓
AgentBackgroundService acts:
  • Queues Chat action with Response text
  • SetGoal / CancelGoal / MoveTo etc.
```

---

## CraftRegex Fast-Path (Sprint 11)

Before the LLM is consulted, `LlmChatInterpreter` checks for crafting intent via regex:

```
Pattern: (craft|forge|smelt)\s+(?:an?\s+)?(.+)
Example: "craft an iron pickaxe" → CraftItem:iron_pickaxe (no LLM call)
```

This eliminates the 2-minute Ollama hang when the bot receives simple crafting commands. The regex is always tried even when `Llm.Enabled = false`.

---

## Directed-at-Bot Heuristics

| Condition | Rule |
|-----------|------|
| Solo play | `onlinePlayers <= 1` → always addressed |
| Named explicitly | Message starts with bot username (case-insensitive) |
| Active conversation | Bot spoke within the last 60 seconds |

---

## Supported Commands (Pattern Matching)

| Player says | Bot does |
|-------------|----------|
| `get/gather/mine/collect [N] <item>` | GatherItem:{itemId} goal |
| `craft/forge/smelt <item>` | CraftItem:{itemId} goal (CraftRegex, no LLM) |
| `build [a/the] <blueprint>` | Build:{blueprintId} goal |
| `stop`, `cancel`, `quit`, `abort` | Cancel current goal |
| `status`, `what are you doing` | Report health + current goal |
| `help`, `commands` | List available commands |
| `come here`, `follow me` | MoveTo player position |
| `go to X Y Z` | MoveTo coordinates |

Common item aliases:  
`wood/log → oak_log`, `cobble/stone → cobblestone`, `iron → iron_ore`,  
`coal → coal_ore`, `diamond`, `sand`, `gravel`, `planks → oak_planks`

---

## LLM Integration (Ollama)

### Configuration (appsettings.json)

```json
{
  "Agent": {
    "Llm": {
      "Enabled": true,
      "OllamaUrl": "http://localhost:11434",
      "Model": "llama3.2",
      "LlmTimeoutSeconds": 10,
      "LlmMaxResponseTokens": 300
    }
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `Enabled` | `false` | Enable LLM interpretation |
| `OllamaUrl` | `http://localhost:11434` | Ollama API base URL |
| `Model` | `llama3.2` | Model to use |
| `LlmTimeoutSeconds` | `10` | Timeout per LLM call (Sprint 11) |
| `LlmMaxResponseTokens` | `300` | Max response tokens via `num_predict` (Sprint 20) |

### LLM Response Schema

```json
{
  "addressed": "yes | maybe | no",
  "intent": "gather | build | cancel | status | help | navigate | ignore | clarify",
  "item": "oak_log",
  "blueprint": "small-house",
  "count": 32,
  "x": null, "y": null, "z": null,
  "response": "Ok, gathering 32 oak logs for you!"
}
```

`addressed = "maybe"` → bot asks "Did you mean me?"  
`addressed = "no"` → message ignored entirely

### LLM Truncation Recovery (Sprint 20)

When Ollama truncates the JSON response before the closing `}`, `TryParseTruncatedJson` attempts to extract `intent`, `item`, `count`, `blueprint` from the partial output. `num_predict = 300` prevents most truncation but the recovery handles edge cases.

---

## System Message Filtering (Sprint 19–21)

`SYSTEM_MESSAGE_PATTERNS` in `index.js` blocks server/admin messages from the LLM pipeline:

```js
const SYSTEM_MESSAGE_PATTERNS = [
  /teleported/i,
  /joined the game/i,
  /left the game/i,
  /^\[Server\]/i,
  /^You are now/i,
  /^\/clear/i,           // /clear command echo
  /Removed \d+ item/i,   // /clear response
  /^Cleared\s+(?:\d+|\S+'s|the\s+inventory)/i,  // alternate clear format
  /^\/give/i             // /give command echo
];
```

Matched messages are dropped before reaching the WebSocket bridge and never touch the LLM.

---

## Rate Limiting

| Limit | Value |
|-------|-------|
| Per-player cooldown | 3 seconds |
| Global minimum interval | 1 second |

When rate-limited, the system falls back to pattern matching without delay. Rate limit events are logged: `"Rate limited <username>: 3s cooldown"`.

---

## Logging & Observability (Sprint 11 + 19)

| Log event | Level | Content |
|-----------|-------|---------|
| Intent resolved | `Information` | `[chat] <Username> -> CreateGoal (CraftItem:iron_pickaxe)` |
| "Hmm..." thinking indicator fires | `Information` | `[thinking] LLM slow — EnqueueThinkingIfSlowAsync fired` |
| Rate limit | `Information` | `Rate limited <username>: 3s cooldown (1/min global max)` |
| LLM timeout | `Warning` | `LLM call timed out after 10s for <username>` |

---

## Known Limitations

1. **No push to dashboard:** Chat messages are not displayed in the web dashboard. Phase 6.
2. **Player position may be null:** At distances > ~128 blocks, entity not loaded. Distance gate falls back to standard heuristics.
3. **LLM model quality:** 3B models may misclassify ambiguous messages. Use `mistral` or `llama3.2:11b` for better results.
4. **No multi-bot claim coordination:** Multiple bots within 64 blocks may both respond. Use bot names to disambiguate.
