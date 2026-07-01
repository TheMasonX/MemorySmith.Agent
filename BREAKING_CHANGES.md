# Breaking Changes & Migration Guide

MemorySmith.Agent follows **Semantic Versioning** (MAJOR.MINOR.PATCH) for its public API surface.

## Version Policy

| Version | Meaning |
|:--------|:--------|
| **MAJOR** (X.0.0) | Breaking API changes — tool signatures, event contracts, REST API shape, protocol wire format |
| **MINOR** (0.X.0) | New features, new tools, new event types, non-breaking API additions |
| **PATCH** (0.0.X) | Bug fixes, logging changes, documentation updates, no API changes |

The public API surface includes:
- Tool names, input schemas, and output data dictionaries
- REST endpoints under `/api/agent/*`
- World event types and their JSON shapes
- Action/Protocol wire format between C# and Node.js adapter
- `appsettings.json` configuration schema
- `IMemoryGateway`, `ITool`, `IWorldAdapter` interfaces

Internal implementation changes (refactoring, extraction) are NOT breaking unless they change the above.

## Deprecation Policy

1. **Breaking changes are announced 1 sprint before implementation.** The announcement includes the motivation, migration path, and target sprint.
2. **Breaking changes are recorded in this file** with before/after examples and migration guidance.
3. **Deprecated APIs are marked `[Obsolete("message")]`** pointing to the replacement. They remain functional for 1 sprint after deprecation before removal.
4. **Bridge removal** (see `Data/Pages/architecture.md` Compatibility Bridge Registry) follows the same 1-sprint deprecation cycle.
5. **Exception:** Security fixes and critical bug fixes may break immediately with a PATCH bump if the risk of delayed migration outweighs the disruption.

## Breaking Change Log

### v0.56.0 (Sprint 58)

| Change | Type | Migration | Sprint |
|:-------|:-----|:----------|:-------|
| **`CommandExecutionEnabled` defaults to `false`** | MINOR | Previously defaulted to `true` (doc said `false`). Set `"CommandExecutionEnabled": true` in `Agent:Chat` config to restore command execution. The LLM prompt already correctly gates command intents; this change aligns the default with the documented safe behavior. | S58 |

### v0.51.0 (Sprint 51)

| Change | Type | Migration | Sprint |
|:-------|:-----|:----------|:-------|
| **`ChatInterpretation` record removed** | MAJOR | Use `IntentDraft` instead. `IChatInterpreter.InterpretAsync` returns `IntentDraft?`. Goal mapping is done by `IntentManager.BuildGoalRequest`. | S44 (completed) |
| **`_agentRuntime` field removed** from `AgentBackgroundService` | PATCH | No consumer impact — field was unused. DI registration of `AgentRuntime` still exists for Sprint 52 extraction. | S51 |
| **`SearchMemoryTool` scans all results** for coordinates (not just top hit) | PATCH | Behavior change: coordinate-carry now considers all search results. Previously only the #1 result was checked. `bestPageId` still returns the top-ranked result's PageId. | S51 |
| **`CoordLabelsPattern` regex updated** — group names changed from single `"x"` to distinct `"axis"`/`"val"` | PATCH | Internal regex change; no consumer impact unless matching against regex group names directly. | S51 |
| **`Serilog.Sinks.SQLite` / `SQLitePCLRaw.lib.e_sqlite3` removed** | PATCH | Removed due to unpatched CVE-2025-6965 (CVSS 7.2 High) in deprecated transitive dependency. The `Agent:Logging:Sqlite` config section is now ignored. Use `Serilog.Sinks.File` (already configured) for persistent log storage. Logs previously written to `logs/agent-telemetry.db` are no longer produced. | S51 |

---

## Migration Templates

### Tool Schema Change

```diff
- InputSchema: { "properties": { "oldField": { "type": "string" } } }
+ InputSchema: { "properties": { "newField": { "type": "string" } } }
```

**Migration:** Update tool callers to use `newField` instead of `oldField`. `oldField` is accepted as a deprecated alias for 1 sprint.

### Event Contract Change

```diff
- { "event": "oldEventName", "data": { ... } }
+ { "event": "newEventName", "data": { ... } }
```

**Migration:** Both event names are emitted for 1 sprint. Update `ProcessEventsAsync` switch to handle both, then remove the old case.

### REST Endpoint Change

```diff
- GET /api/agent/old-endpoint
+ GET /api/agent/v2/new-endpoint
```

**Migration:** Old endpoint returns `301 Moved Permanently` to the new endpoint for 1 sprint before removal.

---

*This file is maintained by the MemorySmith.Agent team. Add entries for every breaking change as part of the implementing sprint.*
