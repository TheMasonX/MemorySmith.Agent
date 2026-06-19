# Architecture

MemorySmith.Agent is structured as three bounded contexts plus interface bridges. This keeps Minecraft-specific code isolated in the adapter module while higher-level agent logic remains game-agnostic.

## Bounded Contexts

| Context | Projects | Responsibility |
|---|---|---|
| **Agent Core** | `Agent.Core`, `Agent.Planning`, `Agent.Personality`, `Agent.Tools` | Goals, planning, memory use, tool invocation, action queue, journal, world model. The "brain". |
| **MemorySmith (Knowledge)** | external — `TheMasonX/MemorySmith` | Persistent wiki pages (facts, plans, blueprints), hybrid search (BM25 + embeddings), REST/MCP API. |
| **World Adapters** | `Agent.World.Minecraft`, `MineflayerAdapter/` | Exposes world state and executes low-level actions. Swappable without changing agent logic. |

Plus supporting projects:

| Project | Responsibility |
|---|---|
| `Agent.Vision` | ISpatialAnalyzer, aesthetic analysis via vision models (Phase 4) |
| `Agent.Construction` | IArchitect, IBlueprintRepository, blueprint schema |
| `WebUI.Blazor` | Blazor Server dashboard, SignalR real-time updates, REST control API, DI root |
| `MemorySmith.Agent.Tests` | NUnit tests — 200+ passing |

## Key Interfaces

```
IAgent                  — top-level agent lifecycle (Run, SetGoal, Stop)
IGoal                   — goal evaluation (IsComplete, HasFailed, FailureReason, DamageInterruptThresholdHp)
IPlan                   — ordered action sequence from planner
IMemoryGateway          — MemorySmith search/read/write
ITool                   — MCP tool (Name, Description, InputSchema, Execute)
IWorldAdapter           — world comms (Connect, SendAction, ReceiveEvents)
IPlanner                — HTN plan generation, replanning
IAgentJournal           — append-only bounded event ring (1000 entries, 11 event types)
IWorldModel             — observe/predict/reconcile/uncertainty for world state
IGoalDecomposer         — pluggable goal decomposition (CanHandle + Decompose)
IReplanGovernor         — stall detection (ACTIVE/STALLED states, inventory-delta progress)
IChatInterpreter        — Minecraft chat → agent intent (pattern-first, LLM fallback)
ISpatialAnalyzer        — environmental metric computation (Phase 4)
IVisionModel            — multimodal aesthetic critique (Phase 4)
IArchitect              — blueprint generation from style requirements
IBlueprintRepository    — blueprint CRUD backed by MemorySmith pages
```

## Runtime Flow

```
Blazor UI / REST API
    → AgentBackgroundService (hosted service)
        → IReplanGovernor (stall detection before plan)
        → IPlanner (PlannerRouter → DecomposerRegistry → HTN fallback)
        → ToolDispatcher (single consolidated dispatcher)
            ├── validates args against InputSchema (JSON Schema)
            ├── IMemoryGateway[agent]  ← GetPage
            ├── IMemoryGateway[world]  ← SearchMemory, CreatePage
            └── IWorldAdapter (MinecraftAdapter)
                → WebSocketBridge → Node.js/Mineflayer → Minecraft server
        → IAgentJournal (append on every significant event)
        → IWorldModel (predict before action, reconcile after GetStatus)
        → DamageInterrupt (ProcessEventsAsync → TryInterruptOnDamage)
        → IChatInterpreter (HandleChatEventAsync → LlmChatInterpreter)
```

## Dual Memory Gateway

Since Sprint 22, two `IMemoryGateway` instances are registered:

| Key | Purpose | Default URL |
|-----|---------|-------------|
| (default) | Agent KB — codebase, guides, architecture | `http://localhost:5001` |
| `"world"` | World KB — world facts, exploration log | `WorldKbUrl` (null = disabled) |

Tool routing (Sprint 23):
- `SearchMemoryTool`, `CreatePageTool` → World KB (world facts and events)
- `GetPageTool` → Agent KB (codebase knowledge and guides)

## Agent Safety Systems

**Damage Interrupt (Sprint 23):** `ProcessEventsAsync` synthesizes `DamageTakenEvent` from consecutive health deltas. When health drops below `DamageInterruptThresholdHp` (default 6 HP), `TryInterruptOnDamage` atomically clears the action queue and enqueues `GetStatus`. Per-goal override possible (0 = never interrupt). 3s cooldown prevents thrash.

**Replan Governor (Sprint 19–20):** `ReplanGovernor` tracks plan fingerprints and inventory changes. Three identical fingerprints with no inventory delta → STALLED. During STALL: 10s delay, no `PlanAsync`, auto-recovery after 60s.

**Inventory Freshness (Sprint 21–22):** `WorldState.IsInventoryStale` is set on `SetGoal` and cleared when `ApplyStatus` processes a `GetStatus` result. `GenericGatherGoal.IsComplete` and `CraftItemGoal.IsComplete` return false when stale, preventing false completion after admin `/clear`.

**Tool Validation (Sprint 5):** `ToolDispatcher.CallAsync` checks all args against `ITool.InputSchema` (type/required/properties) before execution. Unknown tool names are rejected at the `/api/agent/command` endpoint.

## Design Principles

**Deep modules**: each module has a small interface that hides significant complexity. `MoveToTool` encapsulates pathfinding internals — the LLM only sees `MoveTo(x, y, z)`.

**Deterministic first** (ADR D-003): LLM is used sparingly. `CraftRegex` resolves "craft an iron pickaxe" without touching Ollama. All sub-task decomposition runs deterministically; LLM is fallback for novel/ambiguous goals.

**Single-host model (WebUI.Blazor)**: the Blazor app hosts the REST API, SignalR hub, and agent loop in one process. No separate queue, database, or broker.

**Game-agnostic agent logic**: only `Agent.World.Minecraft` knows about Mineflayer. A future `Agent.World.Factorio` adapter implements `IWorldAdapter` and plugs in without changes to the planner or tool engine.

**No magic numbers** (AGENTS.md): all timeouts, radii, TTLs are named constants or `*Options` properties. `TreatWarningsAsErrors=true` in `Directory.Build.props`.
