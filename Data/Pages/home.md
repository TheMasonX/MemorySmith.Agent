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

## Repository

Source: https://github.com/TheMasonX/MemorySmith.Agent  
Wiki engine: https://github.com/TheMasonX/MemorySmith  

## Status

Phase 0 — skeleton committed. All interfaces defined; implementations pending Phase 1–5.
See [Roadmap](roadmap.md) for current milestone and task tracking.

## Solution Structure

```
MemorySmith.Agent.slnx
├── Agent.Core              Domain models, core interfaces
├── Agent.Planning          HTN/GOAP planner
├── Agent.Personality       Chat/voice, agent profile
├── Agent.Tools             MCP tool registry and engine
├── Agent.Vision            Spatial and aesthetic vision
├── Agent.Construction      Blueprints, IArchitect
├── Agent.World.Minecraft   Mineflayer/Node.js adapter
├── WebUI.Blazor            Dashboard + REST API host
└── MemorySmith.Agent.Tests NUnit test suite
```

## Node.js Adapter

The Minecraft connection runs as a separate Node.js subprocess (`MineflayerAdapter/index.js`).
The C# host spawns it on connect and communicates over WebSocket using a JSON command/event protocol.
