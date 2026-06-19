# World KB Guide

The World KB (World Knowledge Base) is a second MemorySmith instance that stores the agent's observations about the Minecraft world — block locations, exploration events, discovered resources, world facts.

It is kept **separate from the Agent KB** (which stores codebase knowledge, guides, and blueprints) so world observations don't pollute architectural knowledge.

**Introduced:** Sprint 22 (separation), Sprint 23 (tool routing)

---

## Architecture

```
Agent KB (default)          World KB ("world" key)
  BaseUrl: :5001              WorldKbUrl: :6869
  ↑                           ↑
  GetPage tool               SearchMemory tool
  (codebase knowledge)       CreatePage tool
                             (world observations)
```

Both instances run as separate MemorySmith processes. The agent's `Program.cs` registers them as named `IMemoryGateway` singletons:

```csharp
builder.Services.AddRestMemoryGateway(agentOptions);                    // default
builder.Services.AddKeyedSingleton<IMemoryGateway>("world", worldGw);  // "world" key
```

---

## Setup

### 1. Start the Agent KB (port 5001)

```bash
cd /path/to/memorysmith-agent-kb
dotnet run --project MemorySmith.App --urls http://localhost:5001
```

### 2. Start the World KB (port 6869)

```bash
cd /path/to/memorysmith-world-kb
dotnet run --project MemorySmith.App --urls http://localhost:6869
```

The two instances need separate data directories. Clone MemorySmith to two different directories, or configure separate `DataPath` values in each instance's `appsettings.json`.

### 3. Configure the Agent

In `WebUI.Blazor/appsettings.json`:

```json
{
  "Agent": {
    "Memory": {
      "BaseUrl": "http://localhost:5001",
      "ApiKey": "",
      "TimeoutSeconds": 30
    },
    "WorldKb": {
      "WorldKbUrl": "http://localhost:6869",
      "WorldApiKey": "",
      "WorldTimeoutSeconds": 30
    }
  }
}
```

---

## Tool Routing

Once the World KB is configured, tool calls are automatically routed:

| Tool | Routes to | Purpose |
|---|---|---|
| `SearchMemory` | World KB | Search for block locations, world events |
| `CreatePage` | World KB | Save new world observations |
| `GetPage` | Agent KB | Retrieve codebase guides and architecture |

This routing is configured in `Program.cs` at registration time — the tools receive the correct `IMemoryGateway` instance via constructor injection.

The LLM sees updated tool descriptions that explain this routing:

```
SearchMemory: "Semantic and keyword search in the World KB wiki (block locations, events, world facts)."
CreatePage: "Create a new page in the World KB for world observations or discoveries."
GetPage: "Read a page from the Agent KB (codebase knowledge, guides, architecture)."
```

---

## Startup Behavior

If `WorldKbUrl` is null or empty, the agent starts normally but logs:

```
[Warning] WorldKbUrl is not configured — world knowledge base is disabled.
SearchMemory and CreatePage tools will return empty results.
```

Tools that route to the World KB (`SearchMemory`, `CreatePage`) return empty results gracefully. No exception is thrown. This allows the agent to operate in "Agent KB only" mode.

---

## What to Store in Each KB

| Agent KB | World KB |
|----------|----------|
| Architecture docs | Block locations found |
| API reference | Resource deposits |
| Blueprints | Exploration log entries |
| Guide pages | Crafting outcomes (what worked) |
| This wiki | World events timeline |
| Agent profile | Village/structure coordinates |

---

## Migration from Single-KB (pre Sprint 22)

Before Sprint 22, all tools routed to a single `IMemoryGateway`. If you have an existing deployment:

1. Your existing MemorySmith instance becomes the **Agent KB** — no change to its `BaseUrl`.
2. Optionally spin up a new MemorySmith instance as the **World KB** and set `WorldKbUrl`.
3. If you leave `WorldKbUrl` as null, `SearchMemory` and `CreatePage` will log a warning and return empty — existing `GetPage` calls still work.

The `WorldKbUrl` default was changed from `"http://127.0.0.1:6869"` to `null` in Sprint 23 to make the disabled-by-default behavior explicit.

---

## Troubleshooting

| Problem | Fix |
|---------|-----|
| `SearchMemory` returns empty results | Check startup log for `WorldKbUrl not configured` warning |
| `Connection refused on :6869` | Start the World KB MemorySmith instance |
| World KB search returns stale data | World KB has its own index — verify pages were created by `CreatePage` |
| Both KBs return 401 | Set `WorldApiKey` / `ApiKey` to match each instance's `ApiKey` setting |
