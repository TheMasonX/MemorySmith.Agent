# Guide: Configuring MemorySmith Integration

MemorySmith.Agent uses up to **two** MemorySmith instances as memory backends:

| Instance | Role | Default Port |
|----------|------|-------------|
| **Agent KB** | Codebase knowledge — guides, architecture, blueprints, agent profile | 5001 |
| **World KB** | World knowledge — block locations, exploration log, world facts | 6869 |

---

## Part 1: Agent KB Setup

### Prerequisites

1. Clone and run MemorySmith:

```bash
git clone https://github.com/TheMasonX/MemorySmith
cd MemorySmith
dotnet run --project MemorySmith.App --urls http://localhost:5001
```

### Configure in appsettings.json

```json
{
  "Agent": {
    "Enabled": true,
    "Memory": {
      "BaseUrl": "http://localhost:5001",
      "ApiKey": "",
      "TimeoutSeconds": 30
    }
  }
}
```

| Field | Description |
|---|---|
| `BaseUrl` | URL of the agent KB MemorySmith instance |
| `ApiKey` | Optional API key (must match MemorySmith's `ApiKey` setting) |
| `TimeoutSeconds` | HTTP timeout for all memory requests |

### Seeding the Wiki

The repo includes pages in `Data/Pages/` that serve as the agent's self-knowledge base. Upload them via the MemorySmith import API or copy them into its data directory.

### Verify Connectivity

```bash
curl http://localhost:5001/api/search?query=architecture
```

Should return matching pages. If you get a 401, configure the `ApiKey`.

---

## Part 2: World KB Setup

The World KB stores the agent's observations about the Minecraft world — block locations, exploration events, world facts. It's separate from the Agent KB so world facts don't pollute codebase knowledge.

### Run a Second MemorySmith Instance

```bash
# From a second terminal / different working directory
cd /path/to/world-kb
git clone https://github.com/TheMasonX/MemorySmith .
dotnet run --project MemorySmith.App --urls http://localhost:6869
```

### Configure WorldKbUrl

```json
{
  "Agent": {
    "WorldKb": {
      "WorldKbUrl": "http://localhost:6869",
      "WorldApiKey": "",
      "WorldTimeoutSeconds": 30
    }
  }
}
```

| Field | Description |
|---|---|
| `WorldKbUrl` | URL of the world KB instance. `null` = disabled (warning logged at startup) |
| `WorldApiKey` | Optional API key for world KB |
| `WorldTimeoutSeconds` | HTTP timeout for world KB requests |

### Startup Warning

If `WorldKbUrl` is null or empty, the agent logs at startup:

```
[Warning] WorldKbUrl is not configured — world knowledge base is disabled. 
SearchMemory and CreatePage tools will return empty results.
```

The agent continues to operate; world KB tools gracefully degrade.

### Tool Routing

Once configured, tool calls are routed automatically:
- `SearchMemory` → World KB (searches world observations)
- `CreatePage` → World KB (saves new world observations)
- `GetPage` → Agent KB (retrieves codebase guides)

See [World KB Guide](world-kb.md) for details.

---

## Part 3: Authentication

If MemorySmith is configured with API key auth:

```json
// MemorySmith appsettings.json
{ "ApiKey": "my-secret-key" }

// MemorySmith.Agent appsettings.json
{ "Agent": { "Memory": { "ApiKey": "my-secret-key" } } }
```

The agent adds `X-Api-Key: {key}` to every request automatically.

---

## IMemoryGateway Patterns

The agent supports three integration modes (all use the same interface):

| Mode | When to use |
|---|---|
| **RestApi** (default) | MemorySmith runs separately on its own port |
| **MockGateway** | Testing — use `MockMemoryGateway` in test projects |
| **InProcess** | Agent and MemorySmith in same process (future optimization) |

---

## Troubleshooting

| Error | Cause | Fix |
|---|---|---|
| `Connection refused on :5001` | Agent KB not running | Start MemorySmith on port 5001 |
| `Connection refused on :6869` | World KB not running | Start second MemorySmith on port 6869 |
| `401 Unauthorized` | Missing ApiKey | Set `ApiKey` in both configs |
| `404` on GetPage | Page doesn't exist | Create it first via POST /api/pages |
| `Timeout` | Slow MemorySmith | Increase `TimeoutSeconds` |
| Startup warning: WorldKbUrl not configured | No World KB URL | Set `WorldKbUrl` or ignore if World KB not needed |

---

## Remote MemorySmith (Production)

```json
{
  "Agent": {
    "Memory": {
      "BaseUrl": "https://agentmemory.myserver.com",
      "ApiKey": "prod-key-here"
    },
    "WorldKb": {
      "WorldKbUrl": "https://worldmemory.myserver.com",
      "WorldApiKey": "world-prod-key-here"
    }
  }
}
```

Enable `AllowRemoteApi` in MemorySmith for remote access.
