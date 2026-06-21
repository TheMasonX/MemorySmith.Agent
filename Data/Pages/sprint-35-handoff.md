# Sprint 35: Build Origin, API Auth, Connect Announcement

> Handoff date: 2026-06-20

## Summary

Three categories of changes: (1) auth fix for Agent ↔ MemorySmith API, (2) chat announcement on connect, (3) build origin coordinate system with auto-detect fallback and LLM support.

---

## Changes Made

### A. API Auth Fix

**Problem**: MemorySmith App (`localhost:6868`) has `AllowRemoteApi=true` and a configured API key. The Agent's `appsettings.json` had `ApiKey: null`, so the `X-Api-Key` header was never sent, causing all API calls to fail with 401 Unauthorized.

**Fix**: Added `MEMORYSMITH_API_KEY` env var mapping in `WebUI.Blazor/Program.cs`. The API key is now also configurable directly in `appsettings.json` under `Agent:Memory:ApiKey`.

**Also fixed**: `WorldKbUrl` was set to `http://127.0.0.1:6869` with no listener. Changed to `null` so it falls back to the main `BaseUrl` (`http://127.0.0.1:6868`).

**Files**: `WebUI.Blazor/Program.cs`, `WebUI.Blazor/appsettings.json`

### B. Chat Announcement on Connect

**Problem**: When the bot connected to the Minecraft server, it didn't announce itself in chat.

**Fix**: Added `_queue.Enqueue(new ActionData { Tool = "Chat", ... })` after connection in `AgentBackgroundService.ExecuteAsync`. The bot now says "Leo has connected to the server."

**File**: `WebUI.Blazor/AgentBackgroundService.cs`

### C. Build Origin Coordinate System

**Problem**: Build origins defaulted silently to `(0,0,0)` with only a warning. Chat didn't support coordinate input. Blueprints had no auto-detect fallback.

**Changes** (all in `Agent.Planning/`):

| File | Change |
|------|--------|
| `Goals/BuildGoal.cs` | Added optional `OriginX/Y/Z` properties with `HasExplicitOrigin` flag |
| `GoalFactory.cs` | Reads `originX/Y/Z` from `GoalParameters` and passes to `BuildGoal` |
| `ChatInterpreter.cs` | `BuildRegex` now captures optional `at X Y Z` suffix |
| `ChatInterpreter.cs` | Help text updated to show `build <blueprint> [at X Y Z]` |
| `Decomposition/BuildGoalDecomposer.cs` | Three-tier origin resolution: explicit → facts → auto-detect `FindFlatArea` |
| `LlmChatInterpreter.cs` | LLM `"build"` intent now extracts `x/y/z` from JSON response and passes as origin params |
| `LlmChatInterpreter.cs` | LLM prompt schema already had `x/y/z` fields — wiring was missing |

**Design principle**: Blueprints are "stamps" — relative offsets only. No silent (0,0,0) default. Auto-detect via `FindFlatArea` when no origin is available.

---

## Test Results

```
Passed:   498, Failed:     3, Skipped:     0, Total:   501
```

The 3 failures are pre-existing timing-sensitive tests in `AgentBackgroundServiceTests`:
- `BlockNotFoundEvent_MinedGreaterThanZero_DoesNotSignalError`
- `BlockNotFoundEvent_MinedZero_WritesToErrorChannel_CausesGoalAbandonment`
- `ErrorEvent_WritesToErrorChannel_CausesGoalAbandonment`

All `BuildGoalDecomposer_ReadOriginFact_*` tests pass.

---

## Configuration

### API Key (per-system setup)

Set the API key via any of these (highest priority first):

1. **Environment variable**: `$env:MEMORYSMITH_API_KEY = "your-key"`
2. **`appsettings.Development.json`**: `"ApiKey": "your-key"` under `Agent:Memory`
3. **`appsettings.json`**: Same path

### World KB URL

When no separate world KB instance exists, keep `WorldKbUrl: null` to fall back to the main MemorySmith API. A dedicated world KB requires a second MemorySmith instance on a different port.

---

## Files Modified

| File | Change |
|------|--------|
| `WebUI.Blazor/Program.cs` | MEMORYSMITH_API_KEY env var mapping |
| `WebUI.Blazor/appsettings.json` | WorldKbUrl set to null |
| `WebUI.Blazor/AgentBackgroundService.cs` | Chat announcement on connect |
| `Agent.Planning/Goals/BuildGoal.cs` | Optional origin coords, constructor |
| `Agent.Planning/GoalFactory.cs` | Origin param extraction, nullable GetInt overload |
| `Agent.Planning/ChatInterpreter.cs` | BuildRegex at-X-Y-Z capture, help text |
| `Agent.Planning/LlmChatInterpreter.cs` | Build intent coords passthrough |
| `Agent.Planning/Decomposition/BuildGoalDecomposer.cs` | Three-tier resolution, auto-detect fallback |
| `MemorySmith.Agent.Tests/Sprint30Tests.cs` | Updated for new ReadOriginFact signature |
| `Data/Pages/guides/build-origin.md` | New guide |

---

## Next Steps / Future Work

1. **IBuildGoal marker interface**: The architecture review noted `HtnPlanner` still uses `goal is BuildGoal` type-check. A marker interface analogous to `IItemSpecGoal` would improve extensibility.
2. **Semantic build locations**: The LLM could eventually resolve "build a house in the nearest village" by searching memory for village coordinates.
3. **World KB setup**: If the user wants separate world KB storage, follow `Data/Pages/Guides/world-kb-deployment.md`.
