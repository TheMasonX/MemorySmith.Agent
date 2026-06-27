# Dashboard & Monitoring

**Feature ID:** F-DASHBOARD  
**Status:** In Progress (Sprint 41-42 improvements planned)  
**Location:** `WebUI.Blazor/Components/`, `WebUI.Blazor/Hubs/`

The Blazor Server dashboard provides real-time agent visibility through REST APIs and SignalR streaming. It is the primary human interface for monitoring and controlling the agent.

## REST API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/agent/status` | GET | Current agent state snapshot |
| `/api/agent/plan` | POST | Set a goal |
| `/api/agent/stop` | POST | Stop agent (?emergency=true) |
| `/api/agent/command` | POST | Execute arbitrary action |
| `/api/goals` | GET | List available goal types |
| `/api/tools` | GET | List registered tools with schemas |
| `/about` | GET | Dashboard about page |

## Dashboard Components

| Component | Displays |
|-----------|----------|
| AgentStatusPanel | Connection status, health, food, position, game mode |
| GoalTracker | Current goal name, phases, progress indicator |
| InventoryPanel | Current inventory contents as a table |
| ChatLog | Real-time in-game chat messages |
| ToolConsole | Command input and tool execution history |
| ActionLog | Dispatched actions with status (pending/completed/failed) |

## SignalR Hub (`/hubs/monitor`)

Real-time streaming channels:
- **StatusUpdate**: Periodic agent status snapshot (every 1-2s)
- **ChatMessage**: In-game chat relay (event-driven)
- **GoalUpdate**: Goal lifecycle events (event-driven)
- **LogEntry**: Structured log messages (event-driven)

## Improvement Roadmap

Sprint 41-42 dashboard improvements:
- Event bus decoupling (TSK-0042)
- Snapshot store (TSK-0043)
- Broadcast service (TSK-0044)
- AgentBackgroundService refactor to event bus (TSK-0045)
- SignalR event normalization (TSK-0046)

## Related

- [Dashboard Integration Memory](../memories/Core/agent-dashboard-integration.json)
- [Dashboard Improvement Plan](../Tasks/blazor-dashboard-improvement.md)
- [Dashboard Improvement v2](../Tasks/blazor-dashboard-improvement-v2.md)
