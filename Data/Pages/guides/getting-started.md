# Getting Started

This guide walks you through running MemorySmith.Agent from a fresh clone.

**Current version: v0.55.0** | 746+ tests

## Prerequisites

| Requirement | Version | Notes |
|---|---|---|
| .NET SDK | 10.0+ | `dotnet --version` |
| Node.js | 22+ | For the Mineflayer adapter |
| MemorySmith | Latest | https://github.com/TheMasonX/MemorySmith |
| Minecraft server | 1.21+ | Optional for real play |
| Ollama | Latest | Optional — for LLM chat interpretation |

## Quick Start (REST API only, no Minecraft)

```bash
git clone https://github.com/TheMasonX/MemorySmith.Agent
cd MemorySmith.Agent
dotnet restore MemorySmith.Agent.slnx
dotnet build   MemorySmith.Agent.slnx --configuration Release
dotnet run     --project WebUI.Blazor
```

The host starts on `http://localhost:5000`. Visit `/about` for version info or `/api/agent/status` to verify it's running.

## Enable the Agent (with Minecraft)

### 1. Start MemorySmith (Agent KB)

```bash
git clone https://github.com/TheMasonX/MemorySmith
cd MemorySmith
dotnet run --project MemorySmith.App
```

Default: `http://localhost:5001`

### 2. Start a Minecraft server

Default: `localhost:25565` (vanilla 1.21+ or Paper)

### 3. Configure appsettings.json

```json
{
  "Agent": {
    "Enabled": true,
    "Memory": {
      "BaseUrl": "http://localhost:5001"
    },
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

### 4. Install Node.js adapter dependencies

```bash
cd MineflayerAdapter && npm install
```

### 5. Run the agent

```bash
dotnet run --project WebUI.Blazor
```

## Optional: World KB (second MemorySmith instance)

For world exploration memory, run a second MemorySmith instance on port 6869 and add to config:

```json
{
  "Agent": {
    "WorldKb": {
      "WorldKbUrl": "http://localhost:6869"
    }
  }
}
```

If `WorldKbUrl` is omitted, world KB tools still work but log a startup warning. See [World KB Guide](world-kb.md).

## Optional: LLM Chat (Ollama)

```bash
# Install Ollama (https://ollama.com)
curl -fsSL https://ollama.com/install.sh | sh
ollama pull llama3.2

# Enable in appsettings.json
{
  "Agent": {
    "Llm": {
      "Enabled": true,
      "Model": "llama3.2"
    }
  }
}
```

Without Ollama, chat interpretation uses deterministic pattern matching (CraftRegex + rules). Most common commands work without LLM.

## Send the first goal

```bash
# Enqueue a named goal
curl -X POST http://localhost:5000/api/agent/plan \
  -H "Content-Type: application/json" \
  -d '{"goalName":"GatherWood","parameters":{"count":10}}'

# Send a raw tool command
curl -X POST http://localhost:5000/api/agent/command \
  -H "Content-Type: application/json" \
  -d '{"command":"GetStatus"}'
```

## Check the logs

Log files are written to `logs/agent-.log` (rolling daily). Serilog structured JSON also writes to `logs/agent-structured-.json`. See [Logging Guide](logging.md) for details.

## Run tests

```bash
dotnet test MemorySmith.Agent.slnx --configuration Release
```

Expected: **746+ tests passed, 0 failed** (10 CUDA/ONNX skips are expected in non-GPU environments).

## Next steps

- [Architecture](../architecture.md) — understand the bounded contexts
- [API Reference](api-reference.md) — all REST endpoints
- [Logging Guide](logging.md) — read agent logs
- [Troubleshooting](troubleshooting.md) — common problems and fixes
- [Adding a Goal](adding-a-goal.md) — extend the planner
- [Adding a Tool](adding-a-tool.md) — extend the tool catalog
