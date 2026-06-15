# MemorySmith.Agent

MemorySmith.Agent is a **modular autonomous agent framework** that treats Minecraft (and other games) as a *world adapter* while using the MemorySmith wiki as its long-term memory. The design follows Domain-Driven, deep-module principles — each bounded context exposes a small, stable interface and hides its complexity.

## Quick Links

| Page | Contents |
|---|---|
| [Architecture](architecture.md) | Bounded contexts, project map, runtime flow |
| [Planner](planner.md) | HTN/GOAP hybrid planner and agent loop |
| [Tool Registry](tool-registry.md) | MCP tool catalog and JSON schemas |
| [Vision](vision.md) | World, spatial, and aesthetic vision subsystem |
| [Blueprints](blueprints.md) | Blueprint schema, IArchitect, construction |
| [Memory](memory.md) | IMemoryGateway, MemorySmith integration patterns |
| [Roadmap](roadmap.md) | 5-phase development plan |
| [Agent Profile](agent-profile.md) | Agent identity and personality |
| [Decisions](decisions.md) | Architectural decision log |

## Developer Guides

| Guide | Description |
|---|---|
| [Getting Started](guides/getting-started.md) | Prerequisites, build, first goal |
| [Adding a Goal](guides/adding-a-goal.md) | How to extend the planner with new goals |
| [Adding a Tool](guides/adding-a-tool.md) | How to add tools to the MCP registry |
| [MemorySmith Setup](guides/memorysmith-setup.md) | Configure the MemorySmith connection |
| [API Reference](guides/api-reference.md) | All REST endpoints with examples |
| [Development Guide](guides/development.md) | CI, testing conventions, sandbox notes |

## Task Tracking

| Page | Description |
|---|---|
| [Phase 3 Tasks](Tasks/phase3-tasks.md) | HTN planner — complete |
| [Phase 4 Tasks](Tasks/phase4-tasks.md) | Vision & adaptive execution — in progress |

## Council Reviews

| Review | Topic |
|---|---|
| [Phase 0/1 Kickoff](council/phase0-bootstrap-phase1-kickoff-council-20260615.md) | Skeleton acceptance, Phase 1 priorities |
| [Phase 2 Memory Integration](council/phase2-memory-integration-council-20260615.md) | IMemoryGateway patterns, 3 fixes applied |
| [Phase 3 Planner Architecture](council/phase3-planner-architecture-council-20260615.md) | HTN/GOAP design, 1 fix applied |

## Repository & Dashboard

Source: https://github.com/TheMasonX/MemorySmith.Agent
Wiki engine: https://github.com/TheMasonX/MemorySmith
Dashboard About: http://localhost:5000/about

## Current Status

**Phase 3 complete** — skeleton + memory + planner committed, CI green (42+ tests).

Next: **Phase 4** — adaptive execution (SearchMemory result → MoveTo coords), ISpatialAnalyzer, GOAP fallback, IVisionModel.

## Solution Structure

```
MemorySmith.Agent.slnx
├── Agent.Core              Domain models, core interfaces
├── Agent.Memory            RestMemoryGateway (IMemoryGateway)
├── Agent.Planning          HTN/GOAP planner, goals, GoalFactory
├── Agent.Personality       Chat/voice, agent profile
├── Agent.Tools             MCP tool registry and engine
├── Agent.Vision            Spatial and aesthetic vision
├── Agent.Construction      Blueprints, IArchitect
├── Agent.World.Minecraft   Mineflayer/Node.js adapter
├── WebUI.Blazor            Dashboard + REST API host
└── MemorySmith.Agent.Tests NUnit test suite (42+ tests)
```
