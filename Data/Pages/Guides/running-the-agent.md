# Running MemorySmith.Agent

**Page:** `Data/Pages/Guides/running-the-agent.md`  
**Last updated:** 2026-06-17 (Sprint 9)

---

## Overview

MemorySmith.Agent has two runtimes that must both be running for the bot to connect to Minecraft:

| Runtime | What it does |
|---------|-------------|
| **C# host** (`WebUI.Blazor`) | Agent loop, planner, memory, LLM chat, REST API, SignalR dashboard |
| **Node.js adapter** (`MineflayerAdapter/`) | Mineflayer bot — bridges Minecraft wire protocol to the C# host via WebSocket |

---

## Prerequisites

| Tool | Version | Notes |
|------|---------|-------|
| .NET SDK | 10.0+ | `dotnet --version` |
| Node.js | 18+ | `node --version` |
| npm | 9+ | Included with Node.js |
| Minecraft Java server | 1.21+ | Local or remote |
| Ollama (optional) | latest | Local LLM; set `Agent:Chat:LlmEnabled=false` to disable |

---

## Quickstart

### 1 — Clone and restore

```bash
git clone https://github.com/TheMasonX/MemorySmith.Agent.git
cd MemorySmith.Agent

# Install Node.js adapter dependencies
cd MineflayerAdapter && npm install && cd ..

# Restore .NET packages
dotnet restore MemorySmith.Agent.slnx
```

### 2 — Configure

Copy `WebUI.Blazor/appsettings.json` to `WebUI.Blazor/appsettings.Development.json` and edit:

```json
{
  "Agent": {
    "Enabled": true,
    "Minecraft": {
      "Host": "localhost",
      "Port": 25565,
      "BotUsername": "Leo",
      "WsPort": 3000
    },
    "Chat": {
      "LlmEnabled": true,
      "LlmProvider": "ollama",
      "LlmModel": "llama3.2:3b",
      "LlmBaseUrl": "http://localhost:11434",
      "RateLimitPerMinute": 20
    },
    "Memory": {
      "BaseUrl": "http://localhost:5000",
      "ApiKey": ""
    }
  }
}
```

**Minimal config** (offline mode, no LLM, no memory):
```json
{
  "Agent": {
    "Enabled": true,
    "Chat": { "LlmEnabled": false },
    "Memory": { "BaseUrl": "http://localhost:5000" }
  }
}
```

### 3 — Start the Node.js adapter

```bash
# From repo root:
cd MineflayerAdapter
MC_HOST=localhost MC_PORT=25565 MC_USERNAME=Leo WS_PORT=3000 node index.js
```

The adapter logs `[ws] listening on port 3000` when ready.

### 4 — Start the C# host

```bash
# From repo root:
dotnet run --project WebUI.Blazor --launch-profile WebUI.Blazor
```

Or in Visual Studio 2022: open `MemorySmith.Agent.slnx` → set `WebUI.Blazor` as startup → F5.

When connected you'll see:
```
[HH:mm:ss] World adapter connected.
[HH:mm:ss] Chat LLM config: enabled=True, provider=ollama, model=llama3.2:3b
```

---

## Environment Variables (Node adapter)

| Variable | Default | Description |
|----------|---------|-------------|
| `MC_HOST` | `localhost` | Minecraft server host |
| `MC_PORT` | `25565` | Minecraft server port |
| `MC_USERNAME` | `AgentBot` | Bot account username (offline mode only) |
| `MC_VERSION` | _(auto)_ | Force a specific Minecraft version |
| `WS_PORT` | `3000` | WebSocket port the C# host connects to |
| `WS_TOKEN` | _(none)_ | Optional auth token for WebSocket |

---

## Running Tests

```bash
dotnet test MemorySmith.Agent.Tests
```

Expected output: all green, ~0 failures (10 skips for CUDA/ONNX tests are normal).

---

## Ollama Setup (LLM Chat)

```bash
# Install Ollama (https://ollama.com)
ollama pull llama3.2:3b        # default model
ollama serve                    # starts on http://localhost:11434
```

For better quality: `ollama pull llama3.1:8b` and update `LlmModel` in config.

---

## REST API Quick Reference

The C# host exposes a REST API on the configured port (default `http://localhost:5001`):

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/agent/status` | Agent status, goal, health, uncertainty |
| POST | `/api/agent/plan` | Start a goal: `{ "GoalName": "GatherItem:oak_log", "Parameters": { "count": 32 } }` |
| DELETE | `/api/agent/goal` | Cancel current goal |
| GET | `/api/agent/journal?limit=50&type=ActionFailed` | Recent journal entries |
| GET | `/api/agent/worldmodel` | Belief state + uncertainty |
| POST | `/api/agent/origin` | Set build origin: `{ "BlueprintId": "small-house", "X": 0, "Y": 64, "Z": 0 }` |
| POST | `/api/agent/chat` | Queue a chat message |
| GET | `/api/goals` | List registered goal types |

---

## Common Issues

### Bot won't connect
- Check Minecraft server allows offline-mode connections (`online-mode=false` in `server.properties` for offline bots).
- Check `MC_HOST`, `MC_PORT`, and `MC_USERNAME` match your server.
- Verify `WS_PORT` matches `Agent:Minecraft:WsPort` in config.

### LLM not responding
- Check Ollama is running: `curl http://localhost:11434/api/tags`
- Try `LlmEnabled=false` to fall back to the pattern-matching interpreter.
- Rate limit: default 20 requests/minute per player. Check `RateLimitPerMinute`.

### Build doesn't start
- Use `POST /api/agent/origin` to set the build origin before dispatching a build goal.
- Or trigger `POST /api/agent/plan` with `GoalName: "FindFlatArea"` to auto-detect a site.
- Wait for the `/api/agent/status` `Goal` field to show the build goal as active.

### Memory/MemorySmith not connected
- The agent degrades gracefully: `SearchMemoryTool` returns empty results but the agent continues.
- Point `Agent:Memory:BaseUrl` at a running MemorySmith instance.

---

## Logs

| Output | Location |
|--------|---------|
| Console | `[HH:mm:ss] message` format (no level prefix by design) |
| File | `logs/memorysmith-agent-YYYY-MM-DD.log` |
| Journal (in-memory) | `GET /api/agent/journal` |
| Dashboard (real-time) | SignalR hub at `/agent-hub` |
