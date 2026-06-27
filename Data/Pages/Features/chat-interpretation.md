# Chat Interpretation

**Feature ID:** F-CHAT  
**Status:** Core (Stable, with known issues)  
**Location:** `Agent.Planning/LlmChatInterpreter.cs`, `Agent.Planning/ChatInterpreter.cs`, `Agent.Planning/IntentManager.cs`

The chat interpretation system converts in-game Minecraft chat messages into structured agent goals. It uses a **two-path architecture**: a fast deterministic path and an LLM-powered fallback.

## Architecture

```
Player Chat → IChatInterpreter
                  ├── ChatInterpreter (fast-path, deterministic pattern matching)
                  └── LlmChatInterpreter (LLM-powered, Ollama/llama3.2:3b)
                      ↓
              IntentDraft (structured intent: gather/build/craft/navigate/cancel/status)
                      ↓
              IntentManager.BuildGoalRequest
                      ↓
              GoalFactory.CreateAsync
                      ↓
              IGoal (build/gather/craft/survive/navigate)
```

## The Two Paths

### Fast-Path (ChatInterpreter)
Uses regex patterns for common commands:
- `"gather N item"` → gather intent
- `"build blueprint"` → build intent (with alias resolution: "house" → "small-house")
- `"craft item"` → craft intent
- `"cancel"`, `"status"`, `"help"` → direct actions

### LLM Path (LlmChatInterpreter)
Called when the fast-path doesn't match. The LLM receives:
- The chat message and sender
- The IntentDraft JSON schema
- Current WorldState summary
- Online players and positions

Returns a JSON IntentDraft. If confidence < threshold (0.6) AND clarification question exists → bot asks for clarification.

## Intent Routing

| Intent | Action |
|--------|--------|
| `cancel` | Cancel current goal |
| `status` / `help` | Log response |
| `continue` / `resume` | Reset governor, force replan |
| `gather` / `build` / `craft` | Create goal via IntentManager |
| `navigate` | Cancel goal + enqueue MoveTo |
| `clarify` | LLM clarification question |

## CRITICAL Rule (AGENTS.md A-1)

**Parsers never create goals.** The interpreter returns `IntentDraft`. Only `AgentBackgroundService` (via `IntentManager` + `GoalFactory`) creates `IGoal` objects. This prevents fast-path LLM bypass and ensures all goals go through confidence scoring and context enrichment.

## Known Issues

- **llama3.2:3b is too small** — frequently misclassifies disagreement as "ignore"
- **System message filter leaking** — `/clear` responses with formatting variations reach LLM
- **Clarify uses hardcoded response** instead of LLM-generated question

## Related

- [Intent Parsing Issues](../memories/Core/agent-intent-parsing-issues.json)
- [Chat Pipeline Memory](../memories/Core/agent-chat-interpretation-pipeline.json)
- [Chat System Wiki Page](../chat-system.md)
