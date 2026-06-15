# MemorySmith.Agent

A modular autonomous agent framework that treats Minecraft as a *world adapter* and uses the [MemorySmith](https://github.com/TheMasonX/MemorySmith) wiki as long-term memory.

## Architecture

Three bounded contexts, deep-module design:

| Context | Projects |
|---|---|
| **Agent Core** | `Agent.Core`, `Agent.Planning`, `Agent.Personality`, `Agent.Tools` |
| **Knowledge** | [MemorySmith](https://github.com/TheMasonX/MemorySmith) (external wiki engine) |
| **World** | `Agent.World.Minecraft` + `MineflayerAdapter/` (Node.js) |

Supporting: `Agent.Vision`, `Agent.Construction`, `WebUI.Blazor`, `MemorySmith.Agent.Tests`

## Solution

```
MemorySmith.Agent.slnx     # VS 2022 / dotnet CLI solution
```

Requires **.NET 10 SDK**.

## Wiki

This repo is self-documenting — wiki pages in `Data/Pages/` are served by a co-deployed MemorySmith instance. Start with [home](Data/Pages/home.md) or browse by topic:

- [Architecture](Data/Pages/architecture.md)
- [Planner (HTN/GOAP)](Data/Pages/planner.md)
- [Tool Registry](Data/Pages/tool-registry.md)
- [Memory](Data/Pages/memory.md)
- [Vision](Data/Pages/vision.md)
- [Blueprints](Data/Pages/blueprints.md)
- [Roadmap](Data/Pages/roadmap.md)
- [Decisions](Data/Pages/decisions.md)

## Quick Start (Phase 0 — skeleton)

```bash
dotnet restore MemorySmith.Agent.slnx
dotnet build   MemorySmith.Agent.slnx --configuration Release
dotnet test    MemorySmith.Agent.slnx --configuration Release
```

The Node.js adapter requires Node 22+ and Mineflayer:

```bash
cd MineflayerAdapter
npm install
# Set env vars: MC_HOST, MC_PORT, MC_USERNAME, WS_PORT
node index.js
```

## Roadmap

| Phase | Scope | Status |
|---|---|---|
| 0 — Skeleton | Interfaces, wiki, CI | ✅ Done |
| 1 — Core MVP | WebSocket bridge, movement tools, Blazor UI | ⬜ |
| 2 — Memory + LLM | MemorySmith gateway, Ollama/OpenAI | ⬜ |
| 3 — Planner | HTN/GOAP, predefined tasks, blueprints | ⬜ |
| 4 — Vision | Spatial analysis, aesthetic critique | ⬜ |
| 5 — Advanced | Multi-agent, vector search, CI/CD | ⬜ |

See [roadmap.md](Data/Pages/roadmap.md) for detailed task tracking.

## License

MIT
