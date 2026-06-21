Skill: MCP Tools (MemorySmith.Agent)

Purpose
-------
Provide concise, repo-scoped guidance for safely using the local MCP (Model
Context Protocol) HTTP API from `MemorySmith.Agent`. This skill documents the
server configuration, allowed operations, security practices, and common
workflows for reading and writing the repository-scoped knowledge base.

Location
--------
Keep this skill at `.github/skills/mcp-tools/SKILL.md` in the
`MemorySmith.Agent` repository so agents can load it when they need MCP
interaction guidance.

Key points
----------
- Server: the verified local MCP endpoint is `http://localhost:6868/mcp`.
- Credentials: use the repo-local `.vscode/mcp.json` (or environment) for
  `X-Api-Key`. Never print or exfiltrate the key in outputs or logs.
- Scope: Only use MCP to read/write memories and tasks under
  `MemorySmith.Agent/Data/Memories` and related repo-scoped records.
- Forbidden: Do NOT use MCP to modify files outside `MemorySmith.Agent` (for
  example: the base `MemorySmith` repo). For cross-repo change requests, create
  a proposal document instead (see Request Template in the agent skill).
- Verified live tool names: `memorysmith_search`, `memorysmith_hybrid_search`,
  `memorysmith_context_pack`, `memorysmith_get`, `memorysmith_code_search`,
  `memorysmith_code_search_status`, `memorysmith_page_search`,
  `memorysmith_page_get`, `memorysmith_task_list`, `memorysmith_task_get`.

Allowed MCP operations
----------------------
- Probe the live HTTP/MCP endpoint with `GET /health`, `GET /api/health/live`,
  and `GET /api/health/ready` for non-destructive liveness checks.
- Call `initialize`, `ping`, and `tools/list` over JSON-RPC at `POST /mcp`.
- Use the verified MCP tool catalog listed above for search, page, code, and
  task operations scoped to this repository.
- Read and update repository-scoped memories/tasks; do not treat
  `/api/diagnostics` as a routine public endpoint because it currently returns
  `401 Unauthorized` without a valid auth context.

Recommended patterns
--------------------
- Snapshot-first: fetch the memory or page snapshot before making changes.
- Evidence-backed edits: when editing a memory, include code/file references
  and a short changelog message.
- Small diffs: prefer granular edits and create accompanying tests when code
  behavior is changed.

Security and safety
-------------------
- Keep the `X-Api-Key` secret; never include it in published PRs or logs.
- If automation needs to run in CI, use a CI-secret bound to the MCP service
  with the minimal required permissions.

Examples
--------
- Verify the MCP endpoint: GET /mcp
- List tools over JSON-RPC: POST /mcp with `{"jsonrpc":"2.0","id":3,"method":"tools/list"}`
- Check health probes: GET /health, GET /api/health/live, GET /api/health/ready
- Note: `/mcp/status` is not a valid route; use `/mcp` for the live tool catalog.

When to call this skill
-----------------------
- Use when a task requires reading or mutating the repo-scoped knowledge
  (Data/Memories) or creating implementation tasks tied to this repo.
- Prefer using this skill over free-form MCP calls; it centralizes safe
  practices and patterns for MemorySmith.Agent.

References
----------
- Repository MCP config: `.vscode/mcp.json`
- Example agent skill that references MCP: `.github/agents/stevebot.agent.md`
