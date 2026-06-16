# Chat Interpretation System

**Introduced:** Phase 5b (TSK-0013)  
**Status:** Active  
**ADR compliance:** D-003 (deterministic-first; LLM is opt-in with graceful fallback)

---

## Overview

The MemorySmith.Agent interprets Minecraft in-game chat and turns it into agent goals.
All chat received by the Mineflayer bot is forwarded to C# as a `chat` WorldEvent.
The `AgentBackgroundService` routes it through the `IChatInterpreter` pipeline.

The agent responds in-chat when it determines the message is for it, and can ask
clarifying questions when uncertain. Multiple bot instances use player proximity to
decide which bot should respond.

---

## Pipeline

```
Player speaks in Minecraft chat
         ↓
Mineflayer bot.on('chat', ...)
  • includes: username, message, onlinePlayers, playerX/Y/Z
         ↓
WebSocketBridge parses JSON → WorldEvent{EventType="chat"}
         ↓
AgentBackgroundService.HandleChatEventAsync
  • extracts playerPosition from payload
         ↓
IChatInterpreter.InterpretAsync(username, message, botName, onlinePlayers, botPos, playerPos, state)
         ↓
    LlmChatInterpreter (default)
    ├─ 1. Distance gate: if player > 64 blocks away AND not named this bot → NotAddressed
    ├─ 2. Pattern fast-path: if ChatInterpreter returns confident result (CreateGoal/CancelGoal/Help) → use it
    ├─ 3. Rate limit check: per-player 3s + global 1s
    ├─ 4. OllamaLlmClient.EvaluateAsync (5-second timeout)
    │      └─ POST http://localhost:11434/api/chat → structured JSON response
    └─ 5. Fallback: ChatInterpreter pattern-matching
         ↓
ChatInterpretation { IntentType, GoalName, GoalParameters, Response }
         ↓
AgentBackgroundService acts:
  • Queues Chat action with Response text
  • SetGoal / CancelGoal / MoveTo etc.
```

---

## Directed-at-Bot Heuristics

The bot uses these rules to determine if a message is addressed to it.
Any one condition being true is sufficient to proceed with interpretation.

| Condition | Rule |
|-----------|------|
| Solo play | `onlinePlayers <= 1` → always addressed |
| Named explicitly | Message starts with bot username (case-insensitive), e.g. "AgentBot, help" |
| Active conversation | Bot spoke within the last 60 seconds |

When using the LLM path, the model also evaluates intent = "clarify" for messages
that *might* be intended for the bot but aren't clear. In that case the bot asks:
"Did you mean me?" rather than silently ignoring.

---

## Supported Commands (Pattern Matching)

| Player says | Bot does |
|-------------|----------|
| `get/gather/mine/collect [N] <item>` | GatherItem:{itemId} goal |
| `build [a/the] <blueprint>` | Build:{blueprintId} goal |
| `stop`, `cancel`, `quit`, `abort` | Cancel current goal |
| `status`, `what are you doing` | Report health + current goal |
| `help`, `commands` | List available commands |
| `come here`, `follow me` | MoveTo player position |
| `go to X Y Z` | MoveTo coordinates |

Common item aliases:
`wood/log → oak_log`, `cobble/stone → cobblestone`, `iron → iron_ore`,
`coal → coal_ore`, `diamond`, `sand`, `gravel`, `planks → oak_planks`

Common blueprint aliases:
`house/shelter/home → small-house`

---

## LLM Integration (Ollama)

### Configuration (appsettings.json)

```json
{
  "Agent": {
    "Llm": {
      "Enabled": true,
      "OllamaUrl": "http://localhost:11434",
      "Model": "llama3.2"
    }
  }
}
```

### Setup

```bash
# Install Ollama (https://ollama.com)
curl -fsSL https://ollama.com/install.sh | sh

# Pull a model (3B parameter, fast on CPU)
ollama pull llama3.2

# Optionally use a larger model for better understanding
ollama pull mistral
```

When `Enabled: false` (default), `OllamaLlmClient` always returns null and the
system falls back to pattern matching. Changing this setting requires an app restart.

### LLM response schema

The model is instructed to return ONLY this JSON:

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

`addressed = "maybe"` → bot responds with a clarifying question.
`addressed = "no"` → message is ignored entirely.

---

## Rate Limiting

Two limits are enforced to prevent overloading Ollama:

| Limit | Value |
|-------|-------|
| Per-player cooldown | 3 seconds |
| Global minimum interval | 1 second |

When rate-limited, the system falls back to pattern matching without delay.
Rate limit state is held in-memory (resets on app restart).

---

## Closest-Agent Routing

When multiple bots are running (future: multi-agent support), the agent closest to
the player should respond to prevent all bots answering at once.

**Phase 5b implementation:** Distance gate in `LlmChatInterpreter`.
- If `playerPosition` is available (from Mineflayer's `bot.players[username].entity`)
  AND the player is > 64 blocks from the bot AND the bot was not named → `NotAddressed`.
- Player position is included in all chat events as `playerX/Y/Z` (null if player is
  too far for their entity to be loaded).

**Phase 6:** Shared `ChatCoordinator` service with "first to claim wins" semaphore
for in-process multi-agent coordination.

---

## Known Limitations (Phase 5b)

1. **No push to dashboard:** Chat messages received in-game are not displayed in the
   web dashboard. Would require SignalR/SSE. Phase 6.
2. **Player position may be null:** At distances > ~128 blocks, Mineflayer doesn't
   load the player entity. Distance gate falls back to standard heuristics.
3. **LLM model quality:** Smaller models (llama3.2 3B) may misclassify ambiguous
   messages. Use `mistral` or `llama3.2:11b` for better results.
4. **No multi-bot claim coordination:** Multiple bots within 64 blocks may both
   respond. Use bot names to disambiguate: "AgentBot1, get me wood".
5. **Crafting/smelting via chat:** Player can say "craft oak_planks" but this creates
   a plain CraftItem tool action, not a full crafting-chain goal. Phase 6.
