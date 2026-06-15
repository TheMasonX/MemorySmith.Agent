# Architecture

MemorySmith.Agent is structured as three bounded contexts plus interface bridges. This keeps Minecraft-specific code isolated in the adapter module while higher-level agent logic remains game-agnostic.

## Bounded Contexts

| Context | Projects | Responsibility |
|---|---|---|
| **Agent Core** | `Agent.Core`, `Agent.Planning`, `Agent.Personality`, `Agent.Tools` | Goals, planning, memory use, tool invocation, action queue. The "brain". |
| **MemorySmith (Knowledge)** | external — `TheMasonX/MemorySmith` | Persistent wiki pages (facts, plans, blueprints), hybrid search (BM25 + embeddings), REST/MCP API. |
| **World Adapters** | `Agent.World.Minecraft`, `MineflayerAdapter/` | Exposes world state and executes low-level actions. Swappable without changing agent logic. |

Plus supporting projects:

| Project | Responsibility |
|---|---|
| `Agent.Vision` | ISpatialAnalyzer, aesthetic analysis via vision models |
| `Agent.Construction` | IArchitect, IBlueprintRepository, blueprint schema |
| `WebUI.Blazor` | Blazor Server dashboard, SignalR real-time updates, REST control API |
| `MemorySmith.Agent.Tests` | NUnit tests for domain models, tool engine, planner |

## Key Interfaces

```
IAgent              — top-level agent lifecycle (Run, SetGoal, Stop)
IGoal               — goal evaluation (IsComplete, HasFailed)
IPlan               — ordered action sequence from planner
IMemoryGateway      — MemorySmith search/read/write
ITool               — MCP tool (Name, Description, InputSchema, Execute)
IWorldAdapter       — world comms (Connect, SendAction, ReceiveEvents)
IPlanner            — HTN/GOAP plan generation and replanning
ISpatialAnalyzer    — environmental metric computation
IVisionModel        — multimodal aesthetic critique
IArchitect          — blueprint generation from style requirements
IBlueprintRepository — blueprint CRUD backed by MemorySmith pages
```

## Runtime Flow

```
Blazor UI / REST API
    → AgentHost
        → IPlanner (HTN/GOAP)
        → IToolCaller (ToolEngine)
            → IMemoryGateway (MemorySmith)
            → IWorldAdapter (MinecraftAdapter)
                → WebSocketBridge → Node.js/Mineflayer → Minecraft server
        → IVisionModel (screenshot critique)
```

## Design Principles

**Deep modules**: each module has a small interface that hides significant complexity. `MoveToTool` encapsulates pathfinding internals — the LLM only sees `MoveTo(x, y, z)`.

**Deterministic first**: the LLM is used sparingly — only for novel goals or after repeated failure. All sub-task decomposition, pathfinding, inventory management, and building patterns run deterministically.

**Single-host model (WebUI.Blazor)**: the Blazor app hosts the REST API, SignalR hub, and agent loop in one process. No separate queue, database, or broker until there is clear evidence one is needed.

**Game-agnostic agent logic**: only `Agent.World.Minecraft` knows about Mineflayer. A future `Agent.World.Factorio` adapter would implement `IWorldAdapter` and plug in without changes to the planner or tool engine.
