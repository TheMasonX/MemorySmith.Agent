# Features Reference

**Page:** `Data/Pages/Guides/features-reference.md`  
**Last updated:** 2026-06-17 (Sprint 9)

---

## Architecture Overview

```
Player (in-game chat) ──────────────────────────────────────┐
                                                             ↓
Minecraft Server ←── Mineflayer (Node.js) ←──WebSocket──→ C# Host
                                                      │
                    ┌─────────────────────────────────┤
                    │         C# Host layers          │
                    │  WebUI.Blazor (orchestration)   │
                    │    AgentBackgroundService        │
                    │    LlmChatInterpreter            │
                    │    HtnPlanner + DecomposerRegistry│
                    │    ToolDispatcher                │
                    │    AgentJournal / WorldModel     │
                    │    RestMemoryGateway → MemorySmith│
                    └─────────────────────────────────┘
```

---

## Goals & Planning

The agent uses **Hierarchical Task Network (HTN) planning**. Goals are decomposed into sequences of atomic tool actions.

### Built-in Goal Types

| Goal Name | Example | What it does |
|-----------|---------|-------------|
| `GatherItem:<id>` | `GatherItem:oak_log` | Mine `count` of the given block |
| `Build:<id>` | `Build:small-house` | Gather materials → craft → navigate → place |
| `FindFlatArea` | — | Scan terrain and auto-set build origin |
| `GatherWood` | — | Mine oak/birch/spruce logs |
| `Explore` | — | Wander + SearchMemory for points of interest |
| `SurviveNight` | — | FindShelter + LightArea |
| `Wander` | — | Pathfind to a random nearby point |

### Starting a Goal

```bash
curl -X POST http://localhost:5001/api/agent/plan \
  -H 'Content-Type: application/json' \
  -d '{"GoalName": "GatherItem:oak_log", "Parameters": {"count": 32}}'
```

### Cancelling a Goal

```bash
curl -X DELETE http://localhost:5001/api/agent/goal
```

---

## Tools (Actions)

ToolDispatcher validates all tool calls against InputSchema before execution. All tools are schema-checked; malformed args return a 400 error with details.

| Tool | Description | Key Args |
|------|-------------|----------|
| `MoveTo` | Pathfind to coordinates | `x`, `y`, `z` |
| `MineBlock` | Mine N blocks of given type | `block`, `count` |
| `PlaceBlock` | Place material at coordinates | `x`, `y`, `z`, `material` |
| `CraftItem` | Craft N of given item (pathfinds to table) | `item`, `count` |
| `SmeltItem` | Smelt N in nearest furnace | `item`, `count`, `fuel` |
| `Wander` | Random pathfind within radius | `radius`, `maxDistanceFromSpawn` |
| `FindFlatArea` | Terrain scan + best build site | `radius`, `minFlatArea`, `yAbove`, `yBelow`, `maxSlope` |
| `SearchMemory` | Query MemorySmith wiki | `query` |
| `GetPage` | Fetch a wiki page | `title` |
| `CreatePage` | Write to wiki | `title`, `content` |
| `Chat` | Send a message to in-game chat | `message` |
| `Status` / `GetStatus` | Fetch bot health/position/inventory | — |

---

## Chat Interpreter

Players interact with Leo via in-game chat. The interpreter has two layers:

### Fast-path (pattern matching, no LLM)
- **CreateGoal:** "get 32 wood" → `GatherItem:oak_log {count:32}`
- **CancelGoal:** "stop", "cancel", "abort"
- **QueryStatus:** "status", "what are you doing"
- **QueryHelp:** "help", "commands"
- **NavigateTo:** "go to 100 64 200", "come here", "follow me"

### LLM path (Ollama / LLM provider)
- Greetings, questions, ambiguous commands
- Goal creation with natural language ("build me a house")
- Structured JSON response parsed into intent + parameters

### Addressing the bot

Leo responds when:
- Solo play (only 1 player online)
- Message contains the bot name ("Leo, come here" or "hello Leo")
- Conversation window is open (bot spoke within 60s)
- Player is within 32 blocks (proximity chat)

Rate limit: 20 LLM calls per player per minute (default).

---

## World Model

The **WorldModel** maintains three layers:
- **ObservationState**: raw ground truth from the Mineflayer adapter
- **BeliefState**: agent's internal model (currently mirrors observations)
- **PredictionState**: what the agent expected before each tool action

**Uncertainty**: running average of prediction-vs-actual deviation across last 20 tool calls. 0.0 = perfect, 1.0 = high uncertainty.

```bash
# Full world model
curl http://localhost:5001/api/agent/worldmodel

# Summary only (omits RecentObservations list)
curl "http://localhost:5001/api/agent/worldmodel?detail=false"
```

---

## Journal

The agent keeps a bounded in-memory journal (last 1000 entries) of all significant events.

### Event Types

| Type | When |
|------|------|
| `AgentStarted` / `AgentStopped` | Connection lifecycle |
| `GoalSet` / `GoalCancel` | Goal changes |
| `PlanCreated` | New HTN plan generated |
| `ActionDispatched` / `ActionCompleted` / `ActionFailed` | Per-tool execution |
| `ReplanTriggered` | Failure threshold hit; goal cleared |
| `Observation` | Named world observations (e.g. FlatAreaFound) |
| `Error` | Unclassified errors |
| `ErrorRecovery` | LLM recovery interpreter invoked |

```bash
# Last 50 entries
curl http://localhost:5001/api/agent/journal

# Only failures
curl "http://localhost:5001/api/agent/journal?type=ActionFailed&limit=20"
```

---

## Build Origin & Flat-Area Scanning

Before building, the agent needs to know WHERE to build.

### Option 1: Set origin manually

```bash
curl -X POST http://localhost:5001/api/agent/origin \
  -H 'Content-Type: application/json' \
  -d '{"BlueprintId": "small-house", "X": 120, "Y": 64, "Z": -80}'
```

### Option 2: Auto-detect via FindFlatArea

```bash
# Trigger a flat-area scan — auto-sets origin when a good site is found
curl -X POST http://localhost:5001/api/agent/plan \
  -d '{"GoalName": "FindFlatArea", "Parameters": {"radius": 30, "minFlatArea": 25}}'
```

When `FlatAreaFoundEvent` fires with `area ≥ 25`, the agent automatically stores the result as the `auto` build origin. The next `Build:*` goal will use it if no explicit origin was set.

### Flat-area scanner tuning

| Arg | Default | Description |
|-----|---------|-------------|
| `radius` | 20 | XZ scan radius in blocks |
| `minFlatArea` | 25 | Minimum qualifying area (cells) — default is 5×5 |
| `yAbove` | 10 | Blocks above bot Y to start scan |
| `yBelow` | 16 | Blocks below bot Y to end scan |
| `maxSlope` | 3 | Max Y-range within a candidate; steeper = rejected |

The scanner uses a composite score: **area (50%) + compactness (30%) + flatness (20%)**.  
Liquid blocks (water, lava) are always rejected.

---

## Memory Integration

MemorySmith wiki is used for long-term memory. The agent queries it via:
- **SearchMemory**: semantic search for location hints, recipes, past decisions
- **GetPage**: fetch a specific wiki page by title
- **CreatePage**: write new observations (e.g. "Found oak forest at 320, 80")

If the MemorySmith server is unavailable, the agent degrades gracefully and logs a warning.

---

## Failure Handling

### Consecutive failure tracking

If a tool fails 3 consecutive times (default), the current goal is abandoned with a `FailureReason`:

| Reason | Trigger |
|--------|---------|
| `ToolTimeout` | Tool exceeded 30s |
| `TargetUnreachable` | `blockNotFound:*` or pathfinding failures |
| `InventoryFull` | Inventory full on mine/craft |
| `RecipeMissing` | Unknown recipe |
| `ConsecutiveFailures` | Hit 3 consecutive non-specific failures |
| `NoValidActions` | Planning produced an empty plan |
| `Unknown` | Unclassified |

### Error recovery

For `blockNotFound` and `recipeMissing` errors, the agent immediately invokes the LLM interpreter with a recovery prompt (includes current inventory + available actions). If the LLM suggests an alternative goal, the agent switches to it.

---

## SignalR Dashboard

A real-time dashboard is available at the SignalR hub (`/agent-hub`). JavaScript clients can subscribe to:
- `StatusUpdated` — position, health, food, goal, uncertainty
- `ChatMessage` — incoming player messages and bot responses
- `GoalUpdate` — goal changes

---

## Configuration Reference

| Key | Default | Description |
|-----|---------|-------------|
| `Agent:Enabled` | `false` | Enable/disable the agent |
| `Agent:Minecraft:Host` | `localhost` | Minecraft server host |
| `Agent:Minecraft:Port` | `25565` | Minecraft server port |
| `Agent:Minecraft:BotUsername` | `AgentBot` | Bot username |
| `Agent:Minecraft:WsPort` | `3000` | WebSocket port for adapter |
| `Agent:Chat:LlmEnabled` | `true` | Enable LLM interpretation |
| `Agent:Chat:LlmProvider` | `ollama` | `ollama` or `openai` |
| `Agent:Chat:LlmModel` | `llama3.2:3b` | Model name |
| `Agent:Chat:LlmBaseUrl` | `http://localhost:11434` | Provider base URL |
| `Agent:Chat:RateLimitPerMinute` | `20` | Max LLM calls per player per minute |
| `Agent:Chat:ConversationWindowSeconds` | `60` | Window in which follow-ups are addressed |
| `Agent:Memory:BaseUrl` | `http://localhost:5000` | MemorySmith API base URL |
| `Agent:Memory:ItemCacheTtlSeconds` | `300` | TTL for item spec cache |
