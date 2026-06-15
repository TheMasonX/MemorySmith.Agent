# API Reference

All REST endpoints exposed by `WebUI.Blazor` (default: `http://localhost:5000`).

## Status

### GET /

```
→ 200 "MemorySmith.Agent is running."
```

### GET /api/agent/status

Returns current agent state.

```json
{
  "status": "idle",
  "goal": "GatherWood",
  "health": 20
}
```

| Field | Type | Description |
|---|---|---|
| `status` | string | `"idle"`, `"planning"`, `"executing"`, or `"disabled"` |
| `goal` | string? | Current active goal name, or null |
| `health` | int? | Bot health from last world event |

---

## Agent Control

### POST /api/agent/connect

Triggers a connect signal (Phase 1 stub).

```json
{ "status": "connected" }
```

### POST /api/agent/stop

Stops the agent (Phase 1 stub).

```json
{ "status": "stopped" }
```

---

## Planning

### POST /api/agent/plan

Enqueues a predefined goal plan. The agent will execute the plan's actions in order.

**Request:**

```json
{
  "goalName": "GatherWood",
  "parameters": {
    "count": 10
  }
}
```

| Field | Required | Description |
|---|---|---|
| `goalName` | YES | Name of a registered goal (see `/api/goals`) |
| `parameters` | NO | Goal-specific parameters (e.g. `count` for GatherWood) |

**Response 200:**

```json
{
  "goal": "GatherWood",
  "description": "Gather at least 10 wood logs from nearby trees.",
  "actionCount": 4,
  "phases": ["FindTree", "MineWood", "Collect"]
}
```

**Response 400 (unknown goal):**

```json
{
  "error": "Unknown goal 'FlyToMoon'.",
  "available": ["GatherWood", "SurviveNight"]
}
```

**Response 500 (agent disabled):**
Set `Agent:Enabled = true` in `appsettings.json`.

---

### POST /api/agent/command

Enqueues a single raw tool call by tool name.

**Request:**

```json
{ "command": "GetStatus" }
```

**Response 200:**

```json
{ "received": "GetStatus", "status": "queued" }
```

---

### GET /api/goals

Lists all registered goal names.

```json
["GatherWood", "SurviveNight"]
```

---

## Blueprints

### GET /api/blueprints

Returns the blueprint catalog (Phase 3 stub — returns empty array).

```json
[]
```

---

## About

### GET /api/about

Returns project metadata including version, license, and phase status.

```json
{
  "name": "MemorySmith.Agent",
  "version": "0.3.0",
  "phase": "Phase 3 — HTN/GOAP Planner",
  "license": "MIT",
  "repository": "https://github.com/TheMasonX/MemorySmith.Agent"
}
```

---

## Error codes

| Code | Meaning |
|---|---|
| 400 | Bad request — invalid goal name or missing required fields |
| 500 | Server error — usually means `Agent:Enabled = false` |

---

## Future endpoints (Phase 4+)

- `GET /api/worldstate` — current bot position, inventory, facts
- `GET /api/plan/current` — active plan with remaining actions
- `POST /api/goal/cancel` — cancel the active goal
- `WS /ws/events` — real-time world events via WebSocket
