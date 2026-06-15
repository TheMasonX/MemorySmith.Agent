# Phased Roadmap

Five development phases, each with deliverables and confidence scores from the Executive Summary.

## Phase 0 — Skeleton ✅ COMPLETE (2026-06-15)

**Scope**: All interfaces defined, solution structure, wiki pages, CI workflow, NUnit test project.

**Delivered**:
- `MemorySmith.Agent.slnx` — 9-project solution (8 libs + tests)
- All core interfaces: `IAgent`, `IGoal`, `IPlan`, `IMemoryGateway`, `ITool`, `IWorldAdapter`, `IPlanner`, `ISpatialAnalyzer`, `IVisionModel`, `IArchitect`, `IBlueprintRepository`
- 10 wiki pages seeded from Executive Summary
- `MineflayerAdapter/` Node.js stub
- GitHub Actions CI: build + test green

---

## Phase 1 — Core Agent MVP (2–3 weeks, confidence 0.95) 🔄 IN PROGRESS

**Scope**: AgentHost scaffolding, WebSocket bridge, basic movement tools, simple goal (gather wood), Blazor UI (status, start bot). No LLM — hardcode a small state machine.

**Delivered so far**:
- `GlobalUsings.cs` — prevents per-file NUnit boilerplate
- `MockMemoryGateway` — test isolation for memory operations
- `MoveToTool`, `StatusTool` — Phase 1 movement tools
- `MinecraftAdapterConfig` — typed config for the adapter
- `MinecraftAdapter.ConnectAsync` — subprocess launch + port-wait
- `AgentBackgroundService` — hosted agent loop (event processing + action dispatch)
- `MineflayerAdapter/package-lock.json` — deterministic npm installs
- 18 passing tests (7 domain + 7 gateway + 4 tool engine)

**Remaining Phase 1 tasks**:
- [ ] Wire `AgentBackgroundService` into `WebUI.Blazor/Program.cs` DI
- [ ] Add `MockWorldAdapter` for test isolation
- [ ] Integration test: `MinecraftAdapter` → Node.js → event round-trip
- [ ] Blazor status panel with SignalR `BotStatusUpdated` push
- [ ] `/api/agent/command` → `AgentBackgroundService.Enqueue` wiring
- [ ] `RestMemoryGateway` stub against live MemorySmith instance

---

## Phase 2 — Memory & Basic LLM (3–4 weeks, confidence 0.85)

**Scope**: Integrate MemorySmith in-process and via MCP. Add Ollama/OpenAI via Microsoft.Extensions.AI. Implement tool registry with memory tools. Sample memory pages.

**Planned deliverables**:
- `IMemoryGateway` + MemorySmith connection (`RestMemoryGateway`)
- `IChatClient` via Microsoft.Extensions.AI (Ollama + OpenAI)
- Sample tool definitions and one-shot chat loop
- Example memory pages (VillageX, AgentProfile)

---

## Phase 3 — Planner & Tasks (4–5 weeks, confidence 0.80)

**Scope**: Build HTN/GOAP planner core. Define goal/task classes. Add LLM-call triggers only for high-level planning. More tools (crafting, building). Blueprint repository V1.

**Planned deliverables**:
- Planner engine (HTN/GOAP)
- Several predefined tasks (`GatherWoodGoal`, `BuildHouseGoal`)
- Demonstration: agent builds small structure from blueprint

---

## Phase 4 — Vision & Aesthetics (4–5 weeks, confidence 0.60)

**Scope**: Integrate vision tools. Add `TakeScreenshot` tool, call Ollama/Gemma for aesthetic critique. Link vision output to plan refinement.

---

## Phase 5 — Advanced Features (4–6 weeks, confidence 0.50)

**Scope**: Multi-agent support, persona plugin, analytics, fallback improvements. Vector embeddings search in Memory. Multiple LLM providers. CI/CD pipelines.

---

## CI Health

| Commit | Status | Tests |
|---|---|---|
| `ec623e6` (2026-06-15) | ✅ build-and-test green | 7/7 |

See [council review](council/phase0-bootstrap-phase1-kickoff-council-20260615.md) for Phase 0 acceptance criteria and Phase 1 priorities.
