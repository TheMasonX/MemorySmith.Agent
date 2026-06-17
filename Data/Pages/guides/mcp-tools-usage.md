# Guide: MCP Tools and SteveBot usage

This guide captures the verified, non-destructive MCP route checks for the local `MemorySmith.Agent` setup and the repo-scoped `SteveBot` workflow.

## Verified local endpoints

Use the repo-local key in `.vscode/mcp.json` for these checks.

| Route | Method | Expected result | Notes |
|---|---|---|---|
| `/health` | GET | `200 OK` with `Healthy` | Safe readiness probe |
| `/api/health/live` | GET | `200 OK` with `Healthy` | Liveness only |
| `/api/health/ready` | GET | `200 OK` with `Ready` | Readiness probe |
| `/api/diagnostics` | GET | `401 Unauthorized` unless authenticated | Requires the same `X-Api-Key` |
| `/mcp` | GET | `200 OK` with tool names | Lists MCP tools |
| `/mcp` | POST `initialize` / `ping` / `tools/list` | `200 OK` JSON-RPC replies | Non-destructive validation |

## Non-destructive probe checklist

```bash
curl -i -H "X-Api-Key: <key>" http://localhost:6868/health
curl -i -H "X-Api-Key: <key>" http://localhost:6868/api/health/live
curl -i -H "X-Api-Key: <key>" http://localhost:6868/api/health/ready
curl -i -H "X-Api-Key: <key>" http://localhost:6868/api/diagnostics
curl -i -H "X-Api-Key: <key>" http://localhost:6868/mcp

curl -i -H "X-Api-Key: <key>" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize"}' \
  http://localhost:6868/mcp
```

## SteveBot and MCP skill references

- `.github/agents/stevebot.agent.md` — repo-focused maintenance agent contract
- `.github/skills/mcp-tools/SKILL.md` — safe MCP usage patterns and scope limits
- `.vscode/mcp.json` — local `X-Api-Key` config for this repo

## Operational notes

- Keep the `X-Api-Key` secret and do not paste it into logs or PRs.
- Use only the repo-scoped MCP routes for `Data/Memories`, `Data/Tasks`, and related agent docs.
- If a semantic-search path reports the missing ONNX embedding model, treat that as a follow-up issue rather than a route failure.
