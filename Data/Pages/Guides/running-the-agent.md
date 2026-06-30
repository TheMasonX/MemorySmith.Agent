# Running the Agent

This guide walks through getting MemorySmith.Agent up and running against a Minecraft server. The primary path is **direct hosting** — running the .NET process and Node.js adapter on your local machine. A secondary Docker path is documented at the end for users who prefer container isolation.

## Prerequisites

Direct hosting (recommended) requires:

- **.NET SDK 10.0** or newer — `dotnet --version` should print a `10.x` value
- **Node.js 20 LTS or newer** — required by the Mineflayer adapter (`node --version`)
- **A reachable Minecraft Java Edition server** — 1.20+ recommended; either a local server you control or a remote one with `online-mode=false` if you don't have a paid account
- **A running MemorySmith instance** — the agent's memory backend, default `http://localhost:5000`
- **A running LLM endpoint** — Ollama on `http://localhost:11434` is the default; any OpenAI-compatible chat-completions endpoint also works
- **(Optional) A second MemorySmith instance** for World KB separation — see [world-kb-deployment.md](world-kb-deployment.md)

## Primary Path: Direct Hosting

### 1. Clone the repository

```bash
git clone https://github.com/your-org/MemorySmith.Agent.git
cd MemorySmith.Agent
```

### 2. Configure appsettings.json

Edit `WebUI.Blazor/appsettings.json` (or use `appsettings.Development.json` for a local override that won't be committed):

```jsonc
{
  "Agent": {
    "Enabled": true,
    "Minecraft": {
      "ServerHost": "localhost",
      "ServerPort": 25565,
      "BotUsername": "MemorySmithBot"
    },
    "Llm": {
      "Endpoint": "http://localhost:11434",
      "Model": "llama3.1:8b",
      "LlmTimeoutSeconds": 60,
      "PlayerCooldownSeconds": 2,
      "GlobalPerMinuteMax": 30
    },
    "Memory": {
      "BaseUrl": "http://localhost:5000",
      "ApiKey": null,
      "TimeoutSeconds": 30,
      // WorldKbUrl defaults to null. Set explicitly to enable
      // world KB separation. When null, world observations land in the agent KB
      // (BaseUrl) and a startup warning is logged.
      "WorldKbUrl": "http://localhost:6869",
      "WorldApiKey": null,
      "WorldTimeoutSeconds": 30
    },
    "Runtime": {
      "ReplanIntervalSeconds": 2,
      "ActionTimeoutSeconds": 30
    }
  }
}
```

Key fields:

- `Minecraft.ServerHost` / `ServerPort` — your Minecraft server's address
- `Minecraft.BotUsername` — the in-game username the agent will join as
- `Memory.BaseUrl` — agent knowledge base (sprint docs, design notes, code refs)
- `Memory.WorldKbUrl` — world knowledge base (in-game observations); see [world-kb-deployment.md](world-kb-deployment.md)
- `Llm.Endpoint` / `Llm.Model` — your LLM backend

### 3. Install Node.js adapter dependencies

The agent talks to Minecraft via a Node.js Mineflayer adapter. Install its dependencies once:

```bash
cd MineflayerAdapter
npm install
cd ..
```

### 4. Start the Node.js adapter

In a terminal dedicated to the adapter:

```bash
cd MineflayerAdapter
npm start
```

The adapter listens on a local socket for the .NET process. Leave this terminal running.

### 5. Start the agent (.NET host)

In a separate terminal:

```bash
cd WebUI.Blazor
dotnet run
```

You should see Serilog output similar to:

```
=== Agent config: bot=MemorySmithBot mc=localhost:25565 | llmTimeout=60s rateCooldown=2s maxPerMin=30 | memory=http://localhost:5000 actionTimeout=30s replanInterval=2s ===
```

If `WorldKbUrl` is unset (Sprint 23 default), you will also see:

```
World KB URL is not configured (WorldKbUrl is null). World observations will be stored in agent KB. Set WorldKbUrl in Agent:Memory:WorldKbUrl to enable world KB separation. See Data/Pages/Guides/world-kb-deployment.md
```

This is informational — the agent still runs; observations just share the agent KB until you configure the world KB.

### 6. Open the WebUI

Browse to `https://localhost:5001` (or whichever port the `dotnet run` output lists). From the dashboard you can:

- Watch live world state, action queue, and event stream
- Set or cancel a goal from the goal picker
- Inspect recent LLM prompts and tool results

### 7. Verify the bot joined

Switch to your Minecraft client and look for `MemorySmithBot` (or whatever `BotUsername` you set) in the player list. Try `/tell MemorySmithBot hello` to confirm the chat path is wired.

### Logs

All structured logs go to `WebUI.Blazor/logs/` (Serilog file sink, added in Sprint 19). Daily-rolled files named `agent-YYYYMMDD.log` capture full debug output even when the console is closed. Tail in real time with:

```bash
tail -f WebUI.Blazor/logs/agent-$(date +%Y%m%d).log
```

### Stopping

Ctrl+C in each terminal. The bot disconnects cleanly on shutdown.

## Secondary Path: Docker Setup

> **Note:** Direct hosting is the recommended path. It compiles faster, attaches a debugger trivially, and avoids the JIT-warm-up tax containers pay on cold start. The Docker path below is for users who want strict isolation, reproducible CI runs, or who already operate a Docker-based deploy.

### Alternative: Docker Compose

A `docker-compose.yml` at the repo root brings up the agent, the Node.js adapter, and a MemorySmith instance together.

```bash
# Build images and start the stack
docker compose up --build

# Or run detached and tail logs
docker compose up -d
docker compose logs -f agent
```

Configuration is layered over the in-image `appsettings.json` via environment variables:

```bash
AGENT__MINECRAFT__SERVERHOST=mc.example.net \
AGENT__MINECRAFT__SERVERPORT=25565 \
AGENT__MEMORY__BASEURL=http://memory:5000 \
AGENT__MEMORY__WORLDKBURL=http://world-memory:6869 \
docker compose up
```

ASP.NET binds inside the container to port 8080 by default. The compose file forwards `8080 -> 8080` so the WebUI is at `http://localhost:8080`.

### Alternative: Docker (manual)

If you'd rather wire containers yourself:

```bash
docker build -t memorysmith-agent -f WebUI.Blazor/Dockerfile .
docker run --rm -p 8080:8080 \
  -e AGENT__MINECRAFT__SERVERHOST=host.docker.internal \
  -e AGENT__MEMORY__BASEURL=http://host.docker.internal:5000 \
  -e AGENT__MEMORY__WORLDKBURL=http://host.docker.internal:6869 \
  memorysmith-agent
```

You will still need a separately running Node.js adapter container (or local process) reachable from the agent container.

## Troubleshooting

- **Bot never joins** — check Mineflayer adapter logs; the .NET process talks to it over a local socket and silently retries on adapter restart
- **LLM timeouts** — bump `Llm.LlmTimeoutSeconds`; small local models on CPU can take 30+ seconds per response
- **"World KB URL is not configured" warning** — expected if you haven't set up a second MemorySmith instance yet; see [world-kb-deployment.md](world-kb-deployment.md)
- **Tool errors after goal change** — the action queue is cleared on goal change (Sprint 12) and on damage interrupt (Sprint 23); errors from the previous plan are expected during the transition

## Next steps

- Read [features-reference.md](features-reference.md) for the full list of goals, tools, and runtime knobs
- Set up world KB separation with [world-kb-deployment.md](world-kb-deployment.md)
- Browse `Data/Pages/SprintLog/` for per-sprint design notes
