# MemorySmith.Agent

MemorySmith.Agent is a **modular autonomous agent framework** that treats Minecraft (and other games) as a *world adapter* while using the MemorySmith wiki as its long-term memory. The design follows Domain-Driven, deep-module principles — each bounded context exposes a small, stable interface and hides its complexity.

**Current version: v0.23.0** | **Sprint 41 in progress** | **63+ tests**

## Quick Links

| Page | Contents |
|---|---|
| [Architecture](architecture.md) | Bounded contexts, project map, runtime flow |
| [Planner](planner.md) | HTN planner, decomposers, replan governor, agent loop |
| [Tool Registry](tool-registry.md) | ToolDispatcher, MCP tool catalog, JSON schemas, validation |
| [Memory](memory.md) | IMemoryGateway, World KB, StructuredFacts, WorldFact resolver |
| [Chat System](chat-system.md) | In-game chat interpretation, LLM pipeline, CraftRegex |
| [Vision](vision.md) | World, spatial, and aesthetic vision subsystem |
| [Blueprints](blueprints.md) | Blueprint schema, IArchitect, construction |
| [Roadmap](roadmap.md) | Sprint history and current status |
| [Agent Profile](agent-profile.md) | Agent identity and personality |
| [Decisions](decisions.md) | Architectural decision log |

## Feature Deep-Dives

Comprehensive wiki pages for every major subsystem. New agents should start here.

| Feature | Description | Core Memory |
|---------|-------------|-------------|
| [Agent Runtime](Features/agent-runtime.md) | BackgroundService lifecycle, 3 concurrent loops | [Runtime Lifecycle](memories/Core/agent-runtime-lifecycle.json) |
| [Chat Interpretation](Features/chat-interpretation.md) | Two-path chat → goal pipeline (deterministic + LLM) | [Chat Pipeline](memories/Core/agent-chat-interpretation-pipeline.json) |
| [Planning System](Features/planning-system.md) | HTN planner, decomposers, goal routing | [Planner Architecture](memories/Core/agent-planner-architecture.json) |
| [Blueprint System](Features/blueprint-system.md) | Markdown blueprint → parse → resolve → build | [Blueprint System](memories/Core/agent-blueprint-system.json) |
| [World Event System](Features/world-event-system.md) | 25 event types, projector, inventory staleness | [Event Catalog](memories/Core/agent-world-event-catalog.json) |
| [Mineflayer Adapter](Features/mineflayer-adapter.md) | Node.js ↔ C# bridge, all command handlers | [Adapter State](memories/Core/agent-mineflayer-adapter-state.json) |
| [Recovery & Safety](Features/recovery-safety.md) | Damage interrupt, stall, error recovery, timeouts | [Recovery System](memories/Core/agent-recovery-system.json) |
| [Memory/Wiki Integration](Features/memory-wiki-integration.md) | Dual-gateway IMemoryGateway, local fallback | [Wiki Integration](memories/Core/agent-wiki-integration.json) |
| [Dashboard & Monitoring](Features/dashboard-monitoring.md) | REST API, SignalR, Blazor components | [Dashboard Integration](memories/Core/agent-dashboard-integration.json) |
| [Emergency Stop](Features/emergency-stop.md) | Bypass queue, immediate halt | [Emergency Stop](memories/Core/agent-emergency-stop.json) |

## Developer Guides

| Guide | Description |
|---|---|
| [Getting Started](guides/getting-started.md) | Prerequisites, build, first goal |
| [Adding a Goal](guides/adding-a-goal.md) | How to extend the planner with new goals |
| [Adding a Tool](guides/adding-a-tool.md) | How to add tools to the MCP registry |
| [MemorySmith Setup](guides/memorysmith-setup.md) | Configure Agent KB and World KB connections |
| [API Reference](guides/api-reference.md) | All REST endpoints with examples |
| [Development Guide](guides/development.md) | CI, testing conventions, sandbox notes |
| [Logging Guide](guides/logging.md) | Serilog setup, log file locations, key messages |
| [Troubleshooting](guides/troubleshooting.md) | Common problems and fixes |

## Feature Guides

| Guide | Description |
|---|---|
| [Agent Journal](guides/agent-journal.md) | IAgentJournal, event types, bounded ring buffer |
| [World Model](guides/world-model.md) | ObservationState, BeliefState, PredictionState, uncertainty |
| [Replan Governor](guides/replan-governor.md) | Stall detection, ACTIVE/STALLED states, auto-recovery |
| [Damage Interrupt](guides/damage-interrupt.md) | Real-time damage response, per-goal thresholds, cooldowns |
| [World KB](guides/world-kb.md) | Dual-gateway setup, tool routing, second MemorySmith instance |

## Task Tracking

| Page | Description |
|---|---|
| [Active Sprint Handoffs](Tasks/) | Sprint handoff documents and implementation plans |
| [Task Records](../../Data/Tasks/) | Structured task definitions (tsk-*) |

## Council Reviews

| Review | Topic |
|---|---|
| [Phase 0/1 Kickoff](council/phase0-bootstrap-phase1-kickoff-council-20260615.md) | Skeleton, Phase 1 priorities |
| [Phase 2 Memory](council/phase2-memory-integration-council-20260615.md) | IMemoryGateway patterns |
| [Phase 3 Planner](council/phase3-planner-architecture-council-20260615.md) | HTN/GOAP design |
| [Sprint 4b Audit](council/sprint4b-audit-council-20260616.md) | Full codebase audit, tool safety priorities |
| [Sprint 6](council/sprint6-council-20260617.md) | Journal, World Model, Decomposers |
| [Sprint 19](council/sprint19-council-20260618.md) | Logging, gather rework, replan governor |
| [Sprint 20 Audit](council/sprint20-audit-20260618.md) | Runtime failure recovery |
| [Sprint 21](council/sprint21-council-20260618.md) | Inventory freshness, governor pre-plan |
| [Sprint 22](council/sprint22-council-20260618.md) | Planner completeness, World KB separation |
| [Sprint 23](council/sprint23-council-20260619.md) | Damage interrupt, World KB routing |
| [Sprint 26+](council/) | Full council review archive |

## Project Wiki Memory Store

The project maintains **28 Core memories** covering all critical codebase areas. These are structured JSON files that serve as long-term agent memory:

**Agent Runtime & Lifecycle (4):** [Runtime Lifecycle](memories/Core/agent-runtime-lifecycle.json) · [Goal Types](memories/Core/agent-goal-types-catalog.json) · [Action Correlation](memories/Core/agent-action-correlation.json) · [Recovery](memories/Core/agent-recovery-system.json)

**World & Events (3):** [WorldState Projector](memories/Core/agent-worldstate-projector.json) · [Event Catalog](memories/Core/agent-world-event-catalog.json) · [WebSocket Bridge](memories/Core/agent-websocket-bridge.json)

**Planning & Goals (3):** [Planner Architecture](memories/Core/agent-planner-architecture.json) · [Blueprint System](memories/Core/agent-blueprint-system.json) · [Chat Pipeline](memories/Core/agent-chat-interpretation-pipeline.json)

**Infrastructure (3):** [Mineflayer Adapter](memories/Core/agent-mineflayer-adapter-state.json) · [Other Handlers](memories/Core/agent-mineflayer-other-handlers.json) · [Testing](memories/Core/agent-testing-infrastructure.json) · [Emergency Stop](memories/Core/agent-emergency-stop.json)

**Integration (2):** [Wiki Integration](memories/Core/agent-wiki-integration.json) · [Dashboard](memories/Core/agent-dashboard-integration.json)

**Baseline (6):** [Architecture](memories/Core/agent-architecture-bounded-contexts.json) · [Build Pipeline](memories/Core/agent-build-pipeline-state.json) · [CI Status](memories/Core/agent-ci-baseline-status.json) · [Council Reviews](memories/Core/agent-council-reviews.json) · [Intent Issues](memories/Core/agent-intent-parsing-issues.json) · [Memory Gateway](memories/Core/agent-memorygateway-integration.json) · [Phase Status](memories/Core/agent-phase-status-current.json) · [Planner Tasks](memories/Core/agent-planner-task-library.json) · [Sprint 40 Status](memories/Core/agent-sprint40-p0-implementation-status.json) · [Tech Stack](memories/Core/agent-technology-stack.json) · [Game Testing](memories/Core/agent-game-testing-readiness.json) · [MCP Baseline](memories/Core/stevebot-mcp-verified-baseline.json)

## Repository & Dashboard

Source: https://github.com/TheMasonX/MemorySmith.Agent  
Wiki engine: https://github.com/TheMasonX/MemorySmith  
Dashboard About: http://localhost:5000/about  
API Status: http://localhost:5000/api/agent/status

## Current Status

**Sprint 41 in progress** — Phase 5 (intent reliability, adaptive execution). 63+ tests.

**Delivered across Sprints 1–41:**
- ✅ Full HTN planner with goal decomposers (Build, Gather, CraftItem, SurviveNight, Navigate)
- ✅ MemorySmith memory integration (Agent KB + World KB dual gateway)
- ✅ In-game chat interpretation (deterministic fast-path + LLM fallback)
- ✅ Serilog structured logging (file + console, ms precision)
- ✅ Replan governor (stall detection, graduated retry [10,20,30,60]s)
- ✅ Agent journal (bounded 1000 entries, 11 event types)
- ✅ World model (predict/reconcile/uncertainty for 9 tools)
- ✅ Damage interrupt (real-time, per-goal thresholds, cooldowns, debounce)
- ✅ Tool validation (JSON Schema against InputSchema)
- ✅ Inventory freshness gate, health-critical monitoring
- ✅ Emergency stop (bypass queue, immediate halt)
- ✅ Blueprint system (markdown → parse → resolve → execute)
- ✅ Action correlation (CAS-based lifecycle tracking)
- ✅ Dual-gateway wiki integration + local file fallback
- ✅ Blazor dashboard with SignalR real-time monitoring
- ✅ 28 core memory files covering all subsystems
- ✅ Feature wiki pages for every major subsystem

Active: Sprint 41 — intent parsing reliability, goto() timeout safety, path_update wiring, stale-inventory guard at goal-creation.

## Solution Structure

```
MemorySmith.Agent.slnx
├── Agent.Core              Domain models, core interfaces, WorldState, ActionQueue
├── Agent.Memory            RestMemoryGateway (IMemoryGateway), World KB support
├── Agent.Planning          HTN planner, goals, GoalFactory, decomposers, governor
├── Agent.Personality       Chat interpretation, LLM pipeline, rate limiting
├── Agent.Tools             ToolDispatcher (single dispatcher), MCP tool implementations
├── Agent.Vision            Spatial and aesthetic vision (future)
├── Agent.Construction      Blueprints, IArchitect
├── Agent.World.Minecraft   Mineflayer/Node.js adapter, WebSocket bridge
├── WebUI.Blazor            Dashboard + REST API host, DI root
└── MemorySmith.Agent.Tests NUnit test suite (200+ tests)
MineflayerAdapter/          Node.js Mineflayer bot + logStructured file logger
Data/Pages/                 This wiki — served by MemorySmith as long-term memory
```
