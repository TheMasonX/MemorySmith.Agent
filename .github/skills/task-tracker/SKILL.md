---
name: task-tracker
description: "Task tracking via MCP tools — create, update, query, and transition tasks using memorysmith_task_* MCP tools. Never manually edit task JSON files."
argument-hint: "Task goal, status transition, or query scope"
user-invocable: true
disable-model-invocation: false
---

# Task Tracker

Use this skill for **ALL** task tracking operations. The MCP task system via `http://localhost:6868/mcp` is the single source of truth. Do **NOT** edit `Data/Tasks/*.json` files directly — use the `memorysmith_task_*` MCP tools listed below.

## Tool Activation (CRITICAL — Read Before Using Task Tools)

The `memorysmith_task_*` MCP tools are **dormant until activated**. Before your first task operation, call:

```
activate_memorysmith_task_management
```

This unlocks:
- `memorysmith_task_create`
- `memorysmith_task_get`
- `memorysmith_task_list`
- `memorysmith_task_set_status`
- `memorysmith_task_update`
- `memorysmith_task_add_comment`
- `memorysmith_task_add_attachment`

**Rule:** If you don't see `memorysmith_task_*` tools available, call `activate_memorysmith_task_management` — the tools appear in your next turn. Do NOT assume they are unavailable.

## Task Lifecycle

### 1. Create a Task

```yaml
memorysmith_task_create
  title: "Descriptive task title"
  description: "Detailed description of the work, scope boundaries, and acceptance criteria"
  priority: "Medium"        # Critical, High, Medium, Low
  status: "Backlog"         # defaults to Backlog
  labels: ["sprint-XX", "domain:xxx", "type:refactor"]
```

Required: `title`. Strongly recommended: `description`, `priority`, `labels`.

### 2. Start Working

Transition to `InProgress` with a note explaining what you're doing:

```yaml
memorysmith_task_set_status
  idOrKey: "tsk-XXXX-descriptive-slug"
  status: "InProgress"
  note: "Started work — scoping and gathering evidence. Reading src/TargetService.cs and related tests."
```

Add progress notes along the way:

```yaml
memorysmith_task_add_comment
  idOrKey: "tsk-XXXX-descriptive-slug"
  body: "Found that X depends on Y. Key file: src/SomeService.cs. Proceeding with approach Z."
```

### 3. Blocked

If stuck, transition to `Blocked` with the blocker details:

```yaml
memorysmith_task_set_status
  idOrKey: "tsk-XXXX-descriptive-slug"
  status: "Blocked"
  note: "Blocked on external dependency — waiting for PR #123 to merge. Test Test_Foo fails with NullReferenceException in BarService.cs:42."
```

### 4. Complete

Transition to `Done` **only after** the change is applied and validation passes. Always include evidence:

```yaml
memorysmith_task_set_status
  idOrKey: "tsk-XXXX-descriptive-slug"
  status: "Done"
  note: |-
    Implemented feature in:
    - src/Feature.cs — added new method
    - tests/FeatureTests.cs — 15 new tests

    Validation:
    - dotnet build: 0 errors, 0 warnings
    - dotnet test: 15/15 passing
    - Test-TaskRecords.ps1: passed
    - CI: green (commit abc1234)
```

### 5. Archive or Reject

```yaml
memorysmith_task_set_status
  idOrKey: "tsk-XXXX-descriptive-slug"
  status: "Archived"
  note: "Superseded by TSK-0124. This approach was abandoned because the dependency was removed in .NET 10."
```

## Critical Rules

1. **NEVER edit `Data/Tasks/*.json` files directly.** The MCP task system is the write path. Manual edits bypass schema validation, risk malformed records, and break CI (`Test-TaskRecords.ps1` will fail).
2. **ALWAYS call `activate_memorysmith_task_management`** before your first task operation — the tools are dormant until activated.
3. **ALWAYS add evidence comments** when transitioning to `Done` — include file paths, test results, or validation output.
4. **Prefer task creation over ad-hoc notes** — every meaningful work item deserves a task record with priority and labels.
5. **Use labels** for sprint association (`sprint-XX`), domain (`domain:xxx`), and type (`type:bug`, `type:refactor`, `type:testing`).
6. **Fallback:** If the MCP task system is unreachable, document the work item in `Data/Pages/pending-tasks.md` as a temporary record and create the MCP task as soon as the system is restored.

## Evidence Standard

When completing a task, the `memorysmith_task_set_status` note should include:
- File paths changed (relative to repo root)
- Test results (`N/N passing`)
- Validation output (`Test-TaskRecords.ps1`, `dotnet build`, etc.)
- CI status if applicable
- Any surprises or changed assumptions

## Quick Reference

| Tool | Purpose | Key Args |
|------|---------|----------|
| `memorysmith_task_create` | Create new task | `title`, `description`, `priority`, `status`, `labels` |
| `memorysmith_task_get` | Fetch task by id/key | `idOrKey` |
| `memorysmith_task_list` | Query tasks | `query`, `status`, `assignee`, `limit` |
| `memorysmith_task_update` | Update task fields | `idOrKey`, `title`, `description`, `priority`, `labels` |
| `memorysmith_task_set_status` | Transition status | `idOrKey`, `status`, `note` |
| `memorysmith_task_add_comment` | Add comment | `idOrKey`, `body` |
| `memorysmith_task_add_attachment` | Attach URI | `idOrKey`, `name`, `kind`, `uri` |

## References

- MCP server config: `.vscode/mcp.json` → `http://localhost:6868/mcp`
- Task records (read-only reference — do not edit): `Data/Tasks/*.json`
- Validation: `pwsh ./Scripts/Test-TaskRecords.ps1`
- Sprint planning: `Data/Pages/roadmap.md`
- Task schema contract: `AGENTS.md` (#task-governance section)
- MCP server health: `GET /health`, `GET /api/health/live`, `GET /api/health/ready`
