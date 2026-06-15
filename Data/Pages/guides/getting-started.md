# Getting Started

This guide walks you through running MemorySmith.Agent from a fresh clone.

## Prerequisites

| Requirement | Version | Notes |
|---|---|---|
| .NET SDK | 10.0+ | `dotnet --version` |
| Node.js | 22+ | For the Mineflayer adapter |
| MemorySmith | Latest | https://github.com/TheMasonX/MemorySmith |
| Minecraft server | 1.21+ | Optional for real play |

## Quick Start (REST API only, no Minecraft)

```bash
git clone https://github.com/TheMasonX/MemorySmith.Agent
cd MemorySmith.Agent
dotnet restore MemorySmith.Agent.slnx
dotnet build   MemorySmith.Agent.slnx --configuration Release
dotnet run     --project WebUI.Blazor
```

The host starts on `http://localhost:5000`. Visit `/about` for the dashboard or `/api/agent/status` to verify it's running.

## Enable the Agent (with Minecraft)

1. Start a MemorySmith instance on `http://localhost:5001`
2. Start a Minecraft server on `localhost:25565`
3. Update `WebUI.Blazor/appsettings.json`:

```json
{
  "Agent": {
    "Enabled": true,
    "Memory": { "BaseUrl": "http://localhost:5001" },
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

4. Install Node.js adapter dependencies:

```bash
cd MineflayerAdapter && npm install
```

5. `dotnet run --project WebUI.Blazor`

## Send the first goal

```bash
curl -X POST http://localhost:5000/api/agent/plan \
  -H "Content-Type: application/json" \
  -d '{"goalName":"GatherWood","parameters":{"count":10}}'
```

Expected response:
```json
{
  "goal": "GatherWood",
  "description": "Gather at least 10 wood logs from nearby trees.",
  "actionCount": 4,
  "phases": ["FindTree", "MineWood", "Collect"]
}
```

## Run tests

```bash
dotnet test MemorySmith.Agent.slnx --configuration Release
```

Expected: **42+ tests passed, 0 failed**.

## Next steps

- [Architecture](../architecture.md) — understand the bounded contexts
- [Adding a Goal](adding-a-goal.md) — extend the planner
- [Adding a Tool](adding-a-tool.md) — extend the MCP tool catalog
- [API Reference](api-reference.md) — all REST endpoints
