# MemorySmith.Agent

[![CI](https://github.com/TheMasonX/MemorySmith.Agent/actions/workflows/ci.yml/badge.svg)](https://github.com/TheMasonX/MemorySmith.Agent/actions)

A modular autonomous agent framework that treats Minecraft as a *world adapter* and uses the [MemorySmith](https://github.com/TheMasonX/MemorySmith) wiki as long-term memory.

**v0.50.2** — Sprint 50 complete — 731+ tests

---

## Features

- **HTN Planner** — hierarchical task decomposition with pluggable `IGoalDecomposer` registry (Build, Gather, Craft, SurviveNight decomposers + PlannerRouter as IPlanner)
- **Dual Memory Gateway** — Agent KB (codebase) + World KB (world observations) as separate MemorySmith instances; tool routing is automatic
- **In-game Chat Interpretation** — CraftRegex fast-path + LLM fallback (Ollama) with 10s timeout, rate limiting, and truncation recovery
- **Replan Governor** — stall detection via plan-fingerprint + inventory-delta; ACTIVE/STALLED states; 60s auto-recovery
- **Damage Interrupt** — real-time damage response; atomic queue clear + GetStatus; per-goal threshold overrides; 3s cooldown
- **Agent Journal** — bounded 1000-entry ring buffer, 11 event types, queryable via REST
- **World Model** — observe/predict/reconcile/uncertainty for 9 tools; running deviation scoring
- **Serilog Logging** — structured JSON + human-readable text; ms precision; JS adapter file logger
- **Tool Validation** — JSON Schema (type/required/properties) checked before every tool execution
- **Inventory Freshness Gate** — `IsInventoryStale` prevents false goal completion after `/clear`
- **ITimeProvider abstraction** — injectable time provider for deterministic testing of cooldowns and intervals

---

## Architecture

Three bounded contexts, deep-module design (ADR D-003: deterministic first, LLM is opt-in):

| Context | Projects |
|---|---|
| **Agent Core** | `Agent.Core`, `Agent.Planning`, `Agent.Personality`, `Agent.Tools` |
| **Knowledge** | [MemorySmith](https://github.com/TheMasonX/MemorySmith) (external wiki engine, x2 instances) |
| **World** | `Agent.World.Minecraft` + `MineflayerAdapter/` (Node.js/Mineflayer) |

Supporting: `Agent.Vision`, `Agent.Construction`, `WebUI.Blazor` (REST API + SignalR), `MemorySmith.Agent.Tests`

```
MemorySmith.Agent.slnx    # VS 2022 / dotnet CLI (.slnx format)
```

Requires **.NET 10 SDK** and **Node.js 22+**.

---

## Quick Start

### REST API only (no Minecraft)

```bash
git clone https://github.com/TheMasonX/MemorySmith.Agent
cd MemorySmith.Agent
dotnet restore MemorySmith.Agent.slnx
dotnet build   MemorySmith.Agent.slnx --configuration Release
dotnet run     --project WebUI.Blazor
```

Visit `http://localhost:5000/api/about` for version info or `/api/agent/status` to check state.

### With Minecraft + MemorySmith

1. Start [MemorySmith](https://github.com/TheMasonX/MemorySmith) on `http://localhost:5001` (Agent KB)
2. Optionally start a second MemorySmith on `http://localhost:6869` (World KB)
3. Start a Minecraft server on `localhost:25565`
4. Configure `WebUI.Blazor/appsettings.json`:

```json
{
  "Agent": {
    "Enabled": true,
    "Memory": { "BaseUrl": "http://localhost:5001" },
    "WorldKb": { "WorldKbUrl": "http://localhost:6869" },
    "Minecraft": {
      "AutoStartNode": true,
      "NodeScriptPath": "../MineflayerAdapter/index.js",
      "ServerHost": "localhost",
      "ServerPort": 25565,
      "BotUsername": "AgentBot"
    }
  }
}
```

5. Install Node.js dependencies and run:

```bash
cd MineflayerAdapter && npm install
cd .. && dotnet run --project WebUI.Blazor
```

### Send a goal

```bash
# Gather 32 oak logs
curl -X POST http://localhost:5000/api/agent/plan \
  -H "Content-Type: application/json" \
  -d '{"goalName":"GatherItem","parameters":{"item":"oak_log","count":32}}'

# Or send a raw tool command
curl -X POST http://localhost:5000/api/agent/command \
  -H "Content-Type: application/json" \
  -d '{"command":"GetStatus"}'
```

### In-game chat

Once the bot is in-game, speak to it in Minecraft chat:

```
gather 32 oak logs
craft an iron pickaxe
build a small house
stop
status
```

---

## Tests

```bash
dotnet test MemorySmith.Agent.slnx --configuration Release
```

Expected: **501+ passed, 0 failed** (10 CUDA/ONNX skips are expected in non-GPU environments).

---

## Wiki

This repo is self-documenting — `Data/Pages/` contains wiki pages served by the co-deployed MemorySmith instance. Start with [home](Data/Pages/home.md):

**Reference:**
- [Architecture](Data/Pages/architecture.md)
- [Planner](Data/Pages/planner.md)
- [Tool Registry](Data/Pages/tool-registry.md)
- [Memory](Data/Pages/memory.md)
- [Chat System](Data/Pages/chat-system.md)
- [Roadmap](Data/Pages/roadmap.md)

**Guides:**
- [Getting Started](Data/Pages/guides/getting-started.md)
- [API Reference](Data/Pages/guides/api-reference.md)
- [Logging](Data/Pages/guides/logging.md)
- [Troubleshooting](Data/Pages/guides/troubleshooting.md)
- [World KB Setup](Data/Pages/guides/world-kb.md)
- [Replan Governor](Data/Pages/guides/replan-governor.md)
- [Damage Interrupt](Data/Pages/guides/damage-interrupt.md)
- [Agent Journal](Data/Pages/guides/agent-journal.md)
- [World Model](Data/Pages/guides/world-model.md)

---

## Roadmap

| Phase / Sprint | Scope | Status |
|---|---|---|
| Phase 0 — Skeleton | Interfaces, wiki, CI | ✅ Done |
| Phase 1 — Core MVP | WebSocket bridge, movement tools, Blazor UI | ✅ Done |
| Phase 2 — Memory + LLM | MemorySmith gateway, Ollama, chat | ✅ Done |
| Phase 3 — Planner | HTN/GOAP, predefined tasks, blueprints | ✅ Done |
| Sprints 5-27 | Tool safety, journal, world model, logging, governors, damage interrupt, World KB, planner routing, ITimeProvider | ✅ Done |
| Sprints 28-33 | Action lifecycle, SEC-01/02 auth, base64 sweep, build restore, DI logger wiring, Program.cs restore | ✅ Done |
| Sprints 34-35 | Build origin coords, API auth fix, chat announcement, live build gate | ✅ Done |
| Sprints 49-50 | Dashboard Wave 1-3: SignalR push, log sink, live log, status panels, landing page, navigation | ✅ Done |
| Phase 5 — Vision | Spatial analysis, aesthetic critique | ⬜ Planned |
| Phase 6 — Advanced | Multi-agent, vector search, CI/CD | ⬜ Planned |

See [roadmap.md](Data/Pages/roadmap.md) for the full sprint-by-sprint history.

---

## License

MIT
