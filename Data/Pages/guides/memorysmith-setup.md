# Guide: Configuring MemorySmith Integration

MemorySmith.Agent uses MemorySmith as its long-term memory (wiki pages, blueprints, plans). This guide shows how to connect them.

## Prerequisites

1. A running MemorySmith instance. Clone and run:

```bash
git clone https://github.com/TheMasonX/MemorySmith
cd MemorySmith
dotnet run --project MemorySmith.App
```

Default local MemorySmith app: `http://localhost:5000`.
For the repo-scoped MCP probe in this repo, use `http://localhost:6868/mcp` with the local `X-Api-Key` from `.vscode/mcp.json`.

## Configure the agent

In `WebUI.Blazor/appsettings.json`:

```json
{
  "Agent": {
    "Enabled": true,
    "Memory": {
      "BaseUrl": "http://localhost:5000",
      "ApiKey": "",
      "TimeoutSeconds": 30,
      "DefaultPageRole": "Anonymous"
    }
  }
}
```

| Field | Description |
|---|---|
| `BaseUrl` | URL of the MemorySmith instance |
| `ApiKey` | Optional API key (set in MemorySmith's `appsettings.json` under `ApiKey`) |
| `DefaultPageRole` | Access level for pages created by the agent: `Anonymous`, `Authenticated`, or `Admin` |

## Seeding the wiki

The repo includes 10 wiki pages in `Data/Pages/` seeded from the Executive Summary. To populate MemorySmith with these:

```bash
# Using the MemorySmith bulk-import feature (if available) or POST individually:
curl -X POST http://localhost:5000/api/pages \
  -H "Content-Type: application/json" \
  -d '{"slug":"agent/architecture","title":"Architecture","body":"..."}'
```

## Verify connectivity

```bash
curl http://localhost:5000/api/search?query=architecture
```

Should return a list of matching pages. If you get a 401, configure the `ApiKey`.

## Verified local MCP probe

Use the repo-local key in `.vscode/mcp.json` for non-destructive checks:

```bash
curl -i -H "X-Api-Key: <key>" http://localhost:6868/health
curl -i -H "X-Api-Key: <key>" http://localhost:6868/api/health/live
curl -i -H "X-Api-Key: <key>" http://localhost:6868/api/health/ready
curl -i -H "X-Api-Key: <key>" http://localhost:6868/mcp
```

`/api/diagnostics` is protected and currently returns `401 Unauthorized` without a valid auth context. `/mcp/status` is not a valid route.

## IMemoryGateway patterns

The agent uses three integration modes (see `memory.md`):

| Mode | When to use |
|---|---|
| **RestApi** (default) | MemorySmith runs separately on its own port |
| **InProcess** | Agent and MemorySmith run in the same process (faster) |
| **MockGateway** | Testing — use `MockMemoryGateway` in test projects |

## Authentication

If MemorySmith is configured with `ApiKey` auth:

```json
// MemorySmith appsettings.json
{ "ApiKey": "my-secret-key" }

// MemorySmith.Agent appsettings.json
{ "Agent": { "Memory": { "ApiKey": "my-secret-key" } } }
```

The agent adds `X-Api-Key: {key}` to every request automatically.

## Remote MemorySmith (production)

For production deployments with MemorySmith on a different host:

```json
{
  "Agent": {
    "Memory": {
      "BaseUrl": "https://memorysmith.myserver.com",
      "ApiKey": "prod-key-here"
    }
  }
}
```

Enable `AllowRemoteApi` in MemorySmith for remote access.

## Troubleshooting

| Error | Cause | Fix |
|---|---|---|
| `Connection refused` | MemorySmith not running | Start MemorySmith first |
| `401 Unauthorized` | Missing ApiKey | Set `ApiKey` in both configs |
| `404` on GetPage | Page doesn't exist | Create it first via POST /api/pages |
| `Timeout` | Slow MemorySmith | Increase `TimeoutSeconds` |
