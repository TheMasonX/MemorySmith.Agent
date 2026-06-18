# World KB Deployment Guide

**Sprint 22 — MemorySmith.Agent**
**Purpose:** Run a dedicated MemorySmith instance for Minecraft world data, separate from the agent codebase KB.

---

## Why a separate World KB?

The agent uses MemorySmith as long-term memory in two distinct contexts:

| Context | Examples | Instance |
|---------|----------|---------|
| **Agent codebase KB** | Sprint docs, council reviews, architecture notes, API contracts | Default (port 6868) |
| **World KB** | Block locations, biome observations, crafting outcomes, exploration notes | World instance (port 6869) |

Without separation, `SearchMemoryTool` queries for `"oak_log location nearby"` return a mix of world observations AND sprint handoff docs and code architecture notes. The world KB eliminates this noise so the bot finds relevant in-game facts quickly.

---

## System requirements

- MemorySmith installed on the machine running the agent (see https://github.com/TheMasonX/MemorySmith)
- .NET 10 runtime (already present if you run MemorySmith.Agent)
- ~200 MB disk space per world (grows with exploration)
- Port 6869 free (world KB) — agent KB uses 6868 by default

---

## Option A: Standalone (recommended for local development)

### 1. Install and verify MemorySmith

Clone and build MemorySmith, or download a release binary. Confirm it runs:

```bash
cd /path/to/MemorySmith
dotnet run --project MemorySmith.Web -- --urls "http://localhost:6868"
# Verify: curl http://localhost:6868/api/health
```

### 2. Create the world data directory

**Windows (default):**
```
D:\Minecraft\MemorySmith\TestWorld\
```

**Linux/macOS:**
```
~/Minecraft/MemorySmith/TestWorld/
```

The path becomes the MemorySmith data root. Each world should have its own sub-directory; the agent's appsettings points `WorldKbUrl` at whichever world is currently active.

### 3. Start the world KB instance

**Windows:**
```cmd
set MEMORYSMITH_DATA_PATH=D:\Minecraft\MemorySmith\TestWorld
dotnet run --project MemorySmith.Web -- --urls "http://localhost:6869"
```

**Linux/macOS:**
```bash
MEMORYSMITH_DATA_PATH=~/Minecraft/MemorySmith/TestWorld \
  dotnet run --project MemorySmith.Web -- --urls "http://localhost:6869"
```

> **Note:** Check MemorySmith's release notes to confirm the exact env var name for the data path. Common names: `MEMORYSMITH_DATA_PATH`, `DataDirectory`, or `Paths__Data`. Set it in `appsettings.json` under the MemorySmith project if the env var approach doesn't apply to your version.

### 4. Verify the world KB is running

```bash
curl http://localhost:6869/api/health
# Expected: {"status":"healthy"} or similar
```

### 5. Configure MemorySmith.Agent

In `WebUI.Blazor/appsettings.json`, the `Agent:Memory` section already includes the world KB settings (added in Sprint 22):

```json
"Memory": {
  "BaseUrl":            "http://127.0.0.1:6868",
  "ApiKey":             null,
  "WorldKbUrl":         "http://127.0.0.1:6869",
  "WorldApiKey":        null,
  "WorldTimeoutSeconds": 30
}
```

If you change the port or add authentication, update `WorldKbUrl` and `WorldApiKey` accordingly.

### 6. Start MemorySmith.Agent

```bash
dotnet run --project WebUI.Blazor
```

The startup log will show both KB URLs:

```
[HH:mm:ss] === Agent config: bot=Leo ... memory=http://127.0.0.1:6868 ...
```

(World KB URL appears in the keyed service registration; check `DEBUG` logs for confirmation.)

---

## Option B: Docker (multi-instance)

Run two MemorySmith containers side by side using different volume mounts and ports.

**docker-compose.yml:**

```yaml
version: "3.9"
services:
  memorysmith-agent-kb:
    image: theMasonX/memorysmith:latest      # replace with correct image name
    ports:
      - "6868:8080"
    volumes:
      - ./data/agent-kb:/app/data            # codebase KB
    environment:
      - MEMORYSMITH_DATA_PATH=/app/data

  memorysmith-world-kb:
    image: theMasonX/memorysmith:latest
    ports:
      - "6869:8080"
    volumes:
      - D:/Minecraft/MemorySmith/TestWorld:/app/data   # world KB
    environment:
      - MEMORYSMITH_DATA_PATH=/app/data
```

```bash
docker compose up -d
```

> **Windows volume mount:** Use forward slashes in Docker Desktop paths:
> `- D:/Minecraft/MemorySmith/TestWorld:/app/data`

---

## Multiple worlds

To switch between Minecraft worlds (e.g. TestWorld vs SurvivalServer), change `WorldKbUrl` to point at the corresponding MemorySmith instance, or use a single instance and change its `DataDirectory` before starting.

**Recommended layout:**
```
D:\Minecraft\MemorySmith\
    TestWorld\       ← port 6869 for test / dev
    SurvivalServer\  ← port 6870 for production world
    Creative\        ← port 6871 (optional)
```

Update `appsettings.json` for the active session:
```json
"WorldKbUrl": "http://127.0.0.1:6870"  // switch to SurvivalServer
```

---

## Wiring tools to the World KB (future sprint)

As of Sprint 22, the world KB is registered as a keyed DI service (`"world"`) and is ready to use, but existing tools (`SearchMemoryTool`, `CreatePageTool`, `GetPageTool`) still write to the agent KB. Future wiring in Program.cs will look like:

```csharp
// Swap to world KB for in-game observations
d.Register(new SearchMemoryTool([FromKeyedServices("world")] worldMemory));
d.Register(new CreatePageTool([FromKeyedServices("world")] worldMemory));
```

Until that wiring is done, world observations written by the bot go to the agent KB. The world KB instance starts empty and ready to receive pages once tool routing is updated.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|-------------|-----|
| `Connection refused` on port 6869 | World KB not started | Start the second MemorySmith instance |
| Both KBs return same pages | `WorldKbUrl` not set or same as `BaseUrl` | Verify appsettings.json `WorldKbUrl` |
| World KB pages appear in agent KB | Tool routing not updated yet (expected) | Wait for future sprint |
| `ArgumentNullException` on `WorldKbUrl` | Null URI passed to `HttpClient.BaseAddress` | Set `WorldKbUrl` to a valid URL or leave it as the default |

---

## Relevant source files

| File | Purpose |
|------|---------|
| `Agent.Memory/RestMemoryGatewayOptions.cs` | `WorldKbUrl`, `WorldApiKey`, `WorldTimeoutSeconds` properties |
| `WebUI.Blazor/Program.cs` | `"memorysmith-world"` named HttpClient + `AddKeyedSingleton<IMemoryGateway>("world", ...)` |
| `WebUI.Blazor/appsettings.json` | Default `WorldKbUrl = http://127.0.0.1:6869` |
