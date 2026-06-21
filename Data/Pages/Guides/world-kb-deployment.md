# World KB Deployment Guide

**Sprint 22 — MemorySmith.Agent** (updated: 2026-06-19)
**Purpose:** Run a dedicated MemorySmith instance for Minecraft world data, separate from the agent codebase KB.

---

## Why a separate World KB?

The agent uses MemorySmith as long-term memory in two distinct contexts:

| Context | Examples | Instance |
|---------|----------|---------|
| **Agent codebase KB** | Sprint docs, council reviews, architecture notes, API contracts | Default (port 6868) |
| **World KB** | Block locations, biome observations, crafting outcomes, exploration notes | World instance (port 6869) |

Without separation, `SearchMemoryTool` queries for `"oak_log location nearby"` return a mix of world observations AND sprint handoff docs and architecture notes. The world KB eliminates this noise so the bot finds relevant in-game facts quickly.

---

## MemorySmith version

**MemorySmith has no versioned releases.** Clone the default branch directly from:

```
https://github.com/TheMasonX/MemorySmith
```

Requires **.NET 10 SDK** — already present if you run MemorySmith.Agent.

---

## MemorySmith configuration keys

MemorySmith uses **two separate path settings**, not a single data root:

| Config key | Env var (double-underscore) | What it stores | Suggested world-KB value |
|---|---|---|---|
| `MemorySmith:DataPath` | `MemorySmith__DataPath` | Memory/knowledge records | `D:\Minecraft\MemorySmith\TestWorld\Memories` |
| `MemorySmith:PagesPath` | `MemorySmith__PagesPath` | Markdown wiki pages | `D:\Minecraft\MemorySmith\TestWorld\Pages` |
| `MemorySmith:EventLogPath` | `MemorySmith__EventLogPath` | Audit log | `D:\Minecraft\MemorySmith\TestWorld\Events\audit.log` |
| `MemorySmith:ApiKey` | `MemorySmith__ApiKey` | Optional API auth key | `null` (disabled) for local use |
| `MemorySmith:AllowRemoteApi` | `MemorySmith__AllowRemoteApi` | Allow non-loopback API calls | `false` (default; loopback always works) |

> **Loopback note:** Requests from `127.0.0.1` (i.e., the agent on the same machine) always reach the API regardless of `ApiKey` or `AllowRemoteApi`. You only need to configure these for remote access.

---

## Option A: Standalone (recommended for local development)

### 1. Clone and build MemorySmith

```bash
git clone https://github.com/TheMasonX/MemorySmith.git
cd MemorySmith
dotnet build
```

### 2. Create the world data directories

**Windows (default layout):**
```
D:\Minecraft\MemorySmith\
    TestWorld\
        Memories\
        Pages\
        Events\
```

```cmd
mkdir "D:\Minecraft\MemorySmith\TestWorld\Memories"
mkdir "D:\Minecraft\MemorySmith\TestWorld\Pages"
mkdir "D:\Minecraft\MemorySmith\TestWorld\Events"
```

**Linux/macOS:**
```bash
mkdir -p ~/Minecraft/MemorySmith/TestWorld/{Memories,Pages,Events}
```

### 3. Create a world-specific appsettings override

Create a file `appsettings.WorldKb.json` alongside the MemorySmith executable (or in its project dir):

**Windows:**
```json
{
  "MemorySmith": {
    "DataPath":     "D:\\Minecraft\\MemorySmith\\TestWorld\\Memories",
    "PagesPath":    "D:\\Minecraft\\MemorySmith\\TestWorld\\Pages",
    "EventLogPath": "D:\\Minecraft\\MemorySmith\\TestWorld\\Events\\audit.log"
  }
}
```

**Linux/macOS:**
```json
{
  "MemorySmith": {
    "DataPath":     "/home/you/Minecraft/MemorySmith/TestWorld/Memories",
    "PagesPath":    "/home/you/Minecraft/MemorySmith/TestWorld/Pages",
    "EventLogPath": "/home/you/Minecraft/MemorySmith/TestWorld/Events/audit.log"
  }
}
```

### 4. Start the world KB instance

**Using env vars (Windows CMD):**
```cmd
set MemorySmith__DataPath=D:\Minecraft\MemorySmith\TestWorld\Memories
set MemorySmith__PagesPath=D:\Minecraft\MemorySmith\TestWorld\Pages
dotnet run --project MemorySmith.App -- --urls "http://localhost:6869"
```

**Using env vars (PowerShell):**
```powershell
$env:MemorySmith__DataPath  = "D:\Minecraft\MemorySmith\TestWorld\Memories"
$env:MemorySmith__PagesPath = "D:\Minecraft\MemorySmith\TestWorld\Pages"
dotnet run --project MemorySmith.App -- --urls "http://localhost:6869"
```

**Using env vars (Linux/macOS):**
```bash
MemorySmith__DataPath=~/Minecraft/MemorySmith/TestWorld/Memories \
MemorySmith__PagesPath=~/Minecraft/MemorySmith/TestWorld/Pages \
  dotnet run --project MemorySmith.App -- --urls "http://localhost:6869"
```

**Using command-line args:**
```bash
dotnet run --project MemorySmith.App -- \
  --urls "http://localhost:6869" \
  --MemorySmith:DataPath "D:\Minecraft\MemorySmith\TestWorld\Memories" \
  --MemorySmith:PagesPath "D:\Minecraft\MemorySmith\TestWorld\Pages"
```

### 5. Verify the world KB is running

```bash
curl http://localhost:6869/api/health
# or
curl http://localhost:6869/api/pages
```

### 6. Start the agent KB on its usual port (if not already running)

The agent KB uses its own data directory (your existing MemorySmith install). Make sure it's on port 6868 as `appsettings.json` expects:

```bash
dotnet run --project MemorySmith.App -- --urls "http://localhost:6868"
```

### 7. Start MemorySmith.Agent

```bash
dotnet run --project WebUI.Blazor
```

The startup log confirms both KB URLs:
```
[HH:mm:ss] === Agent config: ... memory=http://127.0.0.1:6868 ...
```
The world KB (`http://127.0.0.1:6869`) is registered as a keyed DI service and is active, but tools won't route to it until Sprint 23 wiring.

---

## Option B: Docker

Run two MemorySmith containers with separate volume mounts.

**docker-compose.yml:**
```yaml
version: "3.9"
services:
  memorysmith-agent-kb:
    build:
      context: ./MemorySmith       # cloned repo
      dockerfile: Dockerfile
    ports:
      - "6868:8080"
    volumes:
      - ./data/agent-kb/Memories:/app/data/Memories
      - ./data/agent-kb/Pages:/app/data/Pages
    environment:
      - MemorySmith__DataPath=/app/data/Memories
      - MemorySmith__PagesPath=/app/data/Pages

  memorysmith-world-kb:
    build:
      context: ./MemorySmith
      dockerfile: Dockerfile
    ports:
      - "6869:8080"
    volumes:
      - D:/Minecraft/MemorySmith/TestWorld/Memories:/app/data/Memories
      - D:/Minecraft/MemorySmith/TestWorld/Pages:/app/data/Pages
    environment:
      - MemorySmith__DataPath=/app/data/Memories
      - MemorySmith__PagesPath=/app/data/Pages
```

> **Windows Docker volume mounts:** Docker Desktop accepts Windows paths with forward slashes:
> `D:/Minecraft/MemorySmith/TestWorld/Memories:/app/data/Memories`

---

## Multiple worlds

To switch between Minecraft worlds, either:

**A) Different MemorySmith instances (recommended):**
```
D:\Minecraft\MemorySmith\
    TestWorld\      ← port 6869
    SurvivalServer\ ← port 6870 (second dotnet run with different env vars + port)
    Creative\       ← port 6871
```
Update `WorldKbUrl` in `WebUI.Blazor/appsettings.json` for the active session.

**B) Single instance, swap data dirs:**
Stop the world KB, change env vars to point at the new world's directories, restart on the same port.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|-------------|-----|
| `Connection refused` on port 6869 | World KB not started | Run `dotnet run --project MemorySmith.App -- --urls http://localhost:6869` with data-path env vars |
| Pages appear in wrong KB | Tool routing not updated | Expected until Sprint 23; world KB infra is ready but tools still write to agent KB |
| World KB returns empty results | Fresh instance with no pages | Normal — pages are created as the bot explores; SearchMemory returns empty on first run |
| `ArgumentException: BaseAddress must be an absolute URI` | `WorldKbUrl` is empty string | Set it to a valid URL or `null` in appsettings.json; empty string is not the same as null |
| Both ports return same pages | Both pointing at same data dirs | Check that DataPath and PagesPath env vars differ between the two instances |

---

## Relevant source files

| File | Purpose |
|------|---------|
| `Agent.Memory/RestMemoryGatewayOptions.cs` | `WorldKbUrl`, `WorldApiKey`, `WorldTimeoutSeconds` properties |
| `WebUI.Blazor/Program.cs` | `"memorysmith-world"` named HttpClient + `AddKeyedSingleton<IMemoryGateway>("world", ...)` |
| `WebUI.Blazor/appsettings.json` | Default `WorldKbUrl = http://127.0.0.1:6869` |
| `MemorySmith/MemorySmithOptions.cs` | MemorySmith's own config class (`DataPath`, `PagesPath`, `ApiKey`) |
