# API Reference

All REST endpoints exposed by `WebUI.Blazor` (default: `http://localhost:5000`).

**Current version: v0.55.0**

---

## Status

### GET /

```
→ 200 "MemorySmith.Agent is running."
```

### GET /api/agent/status

Returns current agent state.

```json
{
  "status": "executing",
  "goal": "GatherItem:oak_log",
  "health": 20,
  "food": 20,
  "position": { "x": 10, "y": 64, "z": -5 },
  "version": "0.55.0"
}
```

| Field | Type | Description |
|---|---|---|
| `status` | string | `"idle"`, `"planning"`, `"executing"`, or `"disabled"` |
| `goal` | string? | Current active goal name, or null |
| `health` | int? | Bot health from last GetStatus event |
| `food` | int? | Bot food level |
| `position` | object? | Last known position |

---

## Agent Control

### POST /api/agent/connect

Triggers a Mineflayer connect attempt.

```json
{ "status": "connected" }
```

### POST /api/agent/stop

Stops the agent and cancels the current goal.

```json
{ "status": "stopped" }
```

---

## Planning & Goals

### POST /api/agent/plan

Enqueues a goal by name. The agent will decompose and execute the plan.

**Request:**

```json
{
  "goalName": "GatherItem",
  "parameters": {
    "item": "oak_log",
    "count": 32
  }
}
```

**Response 200:**

```json
{
  "goal": "GatherItem:oak_log",
  "description": "Gather at least 32 oak_log.",
  "actionCount": 3,
  "phases": ["SearchMemory", "MineBlock", "GetStatus"]
}
```

**Response 400 (unknown goal):**

```json
{
  "error": "Unknown goal 'FlyToMoon'.",
  "available": ["GatherItem", "CraftItem", "Build", "SurviveNight"]
}
```

### POST /api/agent/command

Enqueues a single raw tool call by name. Only registered tool names are accepted (Sprint 5 lockdown).

**Request:**

```json
{ "command": "GetStatus" }
```

**Response 200:**

```json
{ "received": "GetStatus", "status": "queued" }
```

**Response 400 (unknown tool):**

```json
{
  "error": "Unknown tool 'FlyToMoon'.",
  "available": ["GetStatus", "MoveTo", "MineBlock", "SearchMemory", "GetPage", "CreatePage", "CraftItem", "SmeltItem", "Chat", "Wander", "FindFlatArea"]
}
```

### GET /api/goals

Lists all registered goal names.

```json
["GatherItem", "CraftItem", "Build", "SurviveNight"]
```

---

## Knowledge Resolution

### GET /api/agent/resolve

Resolves an item spec through the `LocalKnowledgeResolver` pipeline.

**Request:**

```bash
curl "http://localhost:5000/api/agent/resolve?item=diamond"
```

**Response 200:**

```json
{
  "itemId": "diamond",
  "candidateType": "DirectMineable",
  "confidence": 0.90,
  "sourceBlocks": ["diamond_ore", "deepslate_diamond_ore"],
  "resolvedAt": "2026-06-19T14:00:00Z"
}
```

| `candidateType` | Meaning |
|---|---|
| `Craftable` | Item has a crafting recipe |
| `DirectMineable` | Item is dropped by mining a block |
| `WorldFact` | Found in WorldState.StructuredFacts |
| `Unknown` | Not resolved |

---

## World State

### GET /api/agent/worldstate

Returns the current `WorldState` snapshot.

```json
{
  "position": { "x": 10, "y": 64, "z": -5 },
  "health": 20,
  "food": 20,
  "inventory": { "oak_log": 8, "cobblestone": 16 },
  "isInventoryStale": false,
  "factCount": 12
}
```

---

## Journal

### GET /api/agent/journal

Returns the most recent agent journal entries (up to 100).

```json
[
  {
    "type": "ActionCompleted",
    "toolName": "MineBlock",
    "goalName": "GatherItem:oak_log",
    "timestamp": "2026-06-19T14:00:00Z",
    "details": "mined 8 oak_log"
  }
]
```

**Query parameters:**

| Param | Default | Description |
|---|---|---|
| `limit` | 100 | Max entries to return |
| `type` | (all) | Filter by event type |

**Event types:** `GoalSet`, `GoalComplete`, `GoalFailed`, `GoalCancelled`, `PlanCreated`, `ActionDispatched`, `ActionCompleted`, `ActionFailed`, `ReplanTriggered`, `AgentStarted`, `AgentStopped`

---

## Blueprints

### GET /api/blueprints

Returns the blueprint catalog.

```json
[]
```

*(Population from World KB planned for Phase 4.)*

---

## About

### GET /api/about

Returns project metadata.

```json
{
  "name": "MemorySmith.Agent",
  "version": "0.23.0",
  "description": "Modular autonomous agent framework — Sprint 23",
  "license": "MIT",
  "repository": "https://github.com/TheMasonX/MemorySmith.Agent"
}
```

---

## Error Codes

| Code | Meaning |
|---|---|
| 400 | Bad request — invalid goal/tool name, missing required field, schema validation failure |
| 404 | Resource not found — goal, page, or item not found |
| 500 | Server error — usually `Agent:Enabled = false` or unhandled exception |

---

## SignalR Hub

`/hubs/agent` — real-time agent status updates via SignalR.

**Events pushed to clients:**

| Event | Payload |
|---|---|
| `AgentStatusUpdate` | `{ status, goal, health, food, position }` |
| `ChatMessage` | `{ username, message }` (future) |
