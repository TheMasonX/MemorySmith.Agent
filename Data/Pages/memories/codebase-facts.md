# Codebase Facts — MemorySmith.Agent

Persistent facts about the codebase for agents working in this repo.

## Repository

- URL: https://github.com/TheMasonX/MemorySmith.Agent
- Default branch: `main`
- CI: GitHub Actions (`build-and-test` job)
- License: MIT

## Solution

- File: `MemorySmith.Agent.slnx` (.slnx format, VS 2022)
- Target: `net10.0`, C# 14, Nullable enabled, ImplicitUsings enabled
- Test runner: NUnit 4.6.1 with coverlet.collector 10.0.1

## Project map

| Project | Namespace | Purpose |
|---|---|---|
| Agent.Core | Agent.Core | Domain models and interfaces |
| Agent.Memory | Agent.Memory | RestMemoryGateway (IMemoryGateway) |
| Agent.Planning | Agent.Planning | HtnPlanner, HtnTaskLibrary, Goals |
| Agent.Personality | Agent.Personality | AgentProfile, IPersonality |
| Agent.Tools | Agent.Tools | ITool implementations, ToolEngine |
| Agent.Vision | Agent.Vision | ISpatialAnalyzer, IVisionModel |
| Agent.Construction | Agent.Construction | IArchitect, IBlueprintRepository |
| Agent.World.Minecraft | Agent.World.Minecraft | MinecraftAdapter, WebSocketBridge |
| WebUI.Blazor | WebUI.Blazor | REST API host, AgentBackgroundService |
| MemorySmith.Agent.Tests | MemorySmith.Agent.Tests | NUnit tests |

## Current phase status

- Phase 0: Complete — skeleton, interfaces, wiki
- Phase 1: Complete — WebSocket bridge, MoveTo/Status tools, agent loop
- Phase 2: Complete — RestMemoryGateway, memory tools, DI wiring
- Phase 3: Complete — HTN planner, GatherWoodGoal, SurviveNightGoal, GoalFactory
- Phase 4: In progress — adaptive execution, vision, GOAP

## Key interfaces

- `IAgent` — top-level agent lifecycle
- `IGoal` — goal evaluation (IsComplete, HasFailed)
- `IPlan` — ordered action sequence
- `IMemoryGateway` — MemorySmith search/read/write
- `ITool` — MCP tool (Name, InputSchema, ExecuteAsync)
- `IWorldAdapter` — world comms (Connect, SendAction, ReceiveEvents)
- `IPlanner` — HTN/GOAP plan generation
- `IGoalFactory` — creates IGoal from string name + params
- `ISpatialAnalyzer` — terrain metrics (Phase 4)
- `IVisionModel` — aesthetic critique via multimodal LLM (Phase 4)

## CI known issues / lessons

- NuGet restore requires authproxy (127.0.0.1:9081) to inject credentials into CONNECT requests
- Must override BOTH `HTTPS_PROXY` and `https_proxy` (lowercase) for dotnet to use the proxy
- Subagents may double-encode JSON in raw string literals — use regular escaped strings for any file with JSON content pushed via GitHub API
- `ToolEngineTests` previously used C# raw string literals that got double-encoded; fixed to regular escaped strings

## MemorySmith API (for RestMemoryGateway)

- Search: `GET /api/search?query={q}&limit=20` → `UnifiedSearchResult[]`
- Get page: `GET /api/pages/{slug}` → `PageDocument { Slug, Title, Body }`
- Create page: `POST /api/pages` with `{ Slug, Title, Body, MinimumRole }`
- Update page: `PUT /api/pages/{slug}` with same body

Note: `SearchResult.Kind` disambiguates `"page"` (slug = GetPage arg) vs `"memory"` (UUID, not a page slug).
