---
name: mcp-tools
description: "General MCP server interaction for MemorySmith.Agent — health probes, tool discovery, memory/page reads. For task mutations use the task-tracker skill."
argument-hint: "MCP operation (health/tools-list/search/get/page) and target scope"
user-invocable: true
disable-model-invocation: false
---

# MCP Tools (MemorySmith.Agent)

General-purpose guidance for safely using the local MCP HTTP API from `MemorySmith.Agent` at `http://localhost:6868/mcp`. Use for server health checks, tool discovery, and memory/page read operations.

For **task mutations** (create, update, transition status), use the `task-tracker` skill instead.

## Tool Activation (CRITICAL)

The MemorySmith MCP tool groups are **dormant until activated**. Before using any MCP tool, call the matching activation function:

| If you need... | Call this activation tool |
|----------------|--------------------------|
| Task read/write tools (`memorysmith_task_*`) | `activate_memorysmith_task_management` |
| MemorySmith search tools (search, hybrid_search, context_pack, get, code_search) | `activate_memorysmith_search_tools` |
| Source bundle / back-map tools | `activate_memorysmith_source_management` |
| Wiki page create/update/delete tools | `activate_memorysmith_wiki_management` |
| Browser interaction tools | `activate_browser_interaction_tools` |

**Rule:** Before concluding any MCP tool is unavailable, scan your available `activate_*` tools. Call the matching one — the tools will appear in your next turn. Do NOT assume they are missing.

## Server Details

- **Endpoint:** `http://localhost:6868/mcp` (configured in `.vscode/mcp.json`)
- **Auth header:** `X-Api-Key` — use the key from `.vscode/mcp.json`. Never print or exfiltrate it in outputs or logs.
- **Scope:** Only MCP operations under `MemorySmith.Agent/Data/Memories` and related repo-scoped records. Do NOT modify files outside this repo.
- **Health probes:** `GET /health`, `GET /api/health/live`, `GET /api/health/ready` — non-destructive liveness checks.

## Verified Read/Search Tools

These tools are confirmed available on the live server:

| Tool | Purpose |
|------|---------|
| `memorysmith_search` | Search memories |
| `memorysmith_hybrid_search` | Hybrid (vector + keyword) search |
| `memorysmith_context_pack` | Get context pack |
| `memorysmith_get` | Get memory by id |
| `memorysmith_code_search` | Search code index |
| `memorysmith_code_search_status` | Code search index status |
| `memorysmith_page_search` | Search wiki pages |
| `memorysmith_page_get` | Get wiki page by path |
| `memorysmith_task_list` | List tasks (read-only) |
| `memorysmith_task_get` | Get task by id/key |

> **Task write tools** (`memorysmith_task_create`, `memorysmith_task_set_status`, etc.) are documented in the `task-tracker` skill. Use that skill for all task mutations.

## Security & Safety

- Keep the `X-Api-Key` secret; never include it in published PRs or logs.
- If automation needs to run in CI, use a CI-secret bound to the MCP service with minimal required permissions.
- `/api/diagnostics` returns `401 Unauthorized` without valid auth context — do not treat it as a routine public endpoint.

## When to Call This Skill

- You need to probe the MCP server health or discover available tools.
- You need to read/search memories or pages in the repo-scoped KB.
- You need to list or get task records (read-only).
- You are unsure which skill covers your MCP operation — start here for orientation, then delegate to `task-tracker` for writes.

## References

- MCP config: `.vscode/mcp.json`
- Task mutations: `.github/skills/task-tracker/SKILL.md`
- Task tracker activation: `activate_memorysmith_task_management`
- Agent that references MCP tools: `.github/agents/stevebot.agent.md`
