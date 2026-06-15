# Phased Roadmap

Five development phases, each with deliverables and confidence scores from the Executive Summary.

## Phase 1 — Core Agent MVP (2–3 weeks, confidence 0.95)

**Scope**: AgentHost scaffolding, WebSocket bridge, basic movement tools, simple goal (gather wood), Blazor UI (status, start bot). No LLM — hardcode a small state machine.

**Deliverables**:
- C# AgentHost project (this skeleton)
- Node.js adapter with WebSocket + MoveTo/Collect tools
- Basic Blazor UI with status and SignalR panels

**Tasks**:
- [ ] Implement `MinecraftAdapter.ConnectAsync` (spawn Node subprocess)
- [ ] Wire `WebSocketBridge` send/receive
- [ ] Add `MoveToTool` and `CollectTool` to `ToolRegistry`
- [ ] Implement `ActionQueue` polling loop in AgentHost
- [ ] Blazor UI: status panel, connect/stop buttons, goal display
- [ ] SignalR hub: push `BotStatusUpdated`, `InventoryChanged` events

## Phase 2 — Memory & Basic LLM (3–4 weeks, confidence 0.85)

**Scope**: Integrate MemorySmith in-process and via MCP. Add Ollama/OpenAI via Microsoft.Extensions.AI. Implement tool registry with memory tools. Sample memory pages.

**Deliverables**:
- `IMemoryGateway` + MemorySmith connection
- `IChatClient` via Microsoft.Extensions.AI (Ollama + OpenAI)
- Sample tool definitions and one-shot chat loop
- Example memory pages (VillageX, AgentProfile)

**Tasks**:
- [ ] Implement `MemorySmithGateway : IMemoryGateway` (REST client)
- [ ] Register `SearchMemory`, `GetPage`, `CreatePage` tools
- [ ] Wire `IChatClient` via `Microsoft.Extensions.AI`
- [ ] Add `MockProvider` for test isolation
- [ ] Seed wiki with initial memory pages from this repo

## Phase 3 — Planner & Tasks (4–5 weeks, confidence 0.80)

**Scope**: Build HTN/GOAP planner core. Define goal/task classes. Add LLM-call triggers only for high-level planning. More tools (crafting, building). Blueprint repository V1.

**Deliverables**:
- Planner engine (HTN/GOAP)
- Several predefined tasks (`GatherWoodGoal`, `BuildHouseGoal`)
- Demonstration: agent builds small structure from blueprint

## Phase 4 — Vision & Aesthetics (4–5 weeks, confidence 0.60)

**Scope**: Integrate vision tools. Add `TakeScreenshot` tool, call Ollama/Gemma for aesthetic critique. Link vision output to plan refinement.

**Deliverables**:
- Vision tool implemented
- Aesthetic prompt to LLM producing feedback
- Demonstration: agent improves a built structure per vision feedback

## Phase 5 — Advanced Features (4–6 weeks, confidence 0.50)

**Scope**: Multi-agent support, persona plugin, analytics, fallback improvements. Vector embeddings search in Memory. Multiple LLM providers. CI/CD pipelines.

**Deliverables**:
- Distributed agent management
- UI for blueprint editing
- Vector search integration (v2)
- Load testing and deployment scripts

## Current Status

**Phase 0 complete**: solution skeleton, all interfaces defined, wiki pages seeded from Executive Summary. CI workflow active.

Next milestone: **Phase 1** — wire the WebSocket bridge and implement basic movement tools.
