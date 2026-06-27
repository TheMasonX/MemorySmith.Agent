# Sprint 53 Handoff — SteveBot Wave

**Date:** 2026-06-27  
**Agent:** SteveBot (GitHub Copilot / DeepSeek V4 Pro)  
**Branch:** [current branch]

## Completed Tasks

### ✅ TSK-0197 — Normalize ActionData.Tool (High)
- `ActionData.Tool` now consistently uses wire protocol name `"place"` instead of C# class name `"PlaceBlock"`
- **BlueprintExecutor.cs**: const changed from `"PlaceBlock"` → `"place"`, doc comment updated
- **AgentBackgroundService.cs**: 11 `"PlaceBlock"` string literals → `"place"` (timeout map, checkpoint advance, correlation, max concurrent, fire-and-forget, progress signal, stale context cleanup, and the dual-check at line 1547 removed)
- **MapNodeActionToToolName** preserved (`"place" => "PlaceBlock"` is correct — maps wire name to ITool.Name)
- **Tests**: HtnTaskLibraryExtraTests (6 occurrences) and HtnPlannerBuildTests (3 occurrences) updated
- **All 742 tests pass**

### ✅ TSK-0194 — HTTP Retry/Resilience (High)
- Added `Microsoft.Extensions.Http.Resilience` v9.1.0 to `WebUI.Blazor.csproj`
- `.AddStandardResilienceHandler()` called on both `"memorysmith"` and `"memorysmith-world"` HttpClient registrations in `Program.cs`
- Standard retry with exponential backoff + circuit breaker now active

### ✅ TSK-0193 — Search Error Handling (High)
- **RestMemoryGateway.SearchAsync**: try/catch for `HttpRequestException`, `TaskCanceledException` (timeout), `JsonException` — returns empty with logged warning
- `OperationCanceledException` with `cancellationToken.IsCancellationRequested` is propagated
- **SearchMemoryTool.ExecuteAsync**: wrapped in try/catch → returns `ToolResult(false, "Search failed: {message}")`

### ✅ TSK-0176 — IBuildGoal Marker Interface (Medium)
- Created `Agent.Planning/Goals/IBuildGoal.cs` — marker interface extending IGoal
- `BuildGoal` now implements `IBuildGoal`
- Updated all 4 `is BuildGoal` type checks → `is IBuildGoal`:
  - `BuildGoalDecomposer.CanHandle` + cast
  - `HtnPlanner.PlanAsync` fallback path
  - `AgentBackgroundService.ProvisionGoalIfCreativeAsync`
  - `AgentBackgroundService.SummarizeTaskRelevantInventory`

### 🚧 TSK-0175 — IBuildMaterialProvider (Medium, BLOCKED)
- Interface `IBuildMaterialProvider`, implementations (`CreativeMaterialProvider`, `SurvivalMaterialProvider`), and composite (`BuildMaterialProvider`) designed
- **Blocked by**: VS Code buffer → disk persistence issue for `HtnTaskLibrary.cs`
- Provider files deleted to keep build clean; interface design documented in task record
- **Next agent**: Wire up by adding constructor injection of `IBuildMaterialProvider` into `HtnTaskLibrary`, replace `if (!isCreative)` check in `DecomposeBuild` with `_materialProvider.EmitProvisioningActions(...)`, make `EmitSurvivalMaterialProvisioning` public, register `BuildMaterialProvider` in DI

## Files Changed

| File | Change |
|------|--------|
| `Agent.Construction/BlueprintExecutor.cs` | `"PlaceBlock"` → `"place"` const |
| `Agent.Memory/RestMemoryGateway.cs` | SearchAsync error handling |
| `Agent.Planning/Goals/IBuildGoal.cs` | **NEW** — marker interface |
| `Agent.Planning/Goals/BuildGoal.cs` | `: IGoal` → `: IBuildGoal` |
| `Agent.Planning/Decomposition/BuildGoalDecomposer.cs` | `is BuildGoal` → `is IBuildGoal` |
| `Agent.Planning/HtnPlanner.cs` | `is BuildGoal` → `is IBuildGoal` |
| `Agent.Tools/Tools/SearchMemoryTool.cs` | ExecuteAsync try/catch wrapper |
| `MemorySmith.Agent.Tests/HtnPlannerBuildTests.cs` | `"PlaceBlock"` → `"place"` |
| `MemorySmith.Agent.Tests/HtnTaskLibraryExtraTests.cs` | `"PlaceBlock"` → `"place"` (6x) |
| `WebUI.Blazor/AgentBackgroundService.cs` | 11 `"PlaceBlock"` → `"place"` + 2 `is IBuildGoal` |
| `WebUI.Blazor/Program.cs` | `.AddStandardResilienceHandler()` on both HTTP clients |
| `WebUI.Blazor/WebUI.Blazor.csproj` | `Microsoft.Extensions.Http.Resilience` v9.1.0 |

## Build & Test Status

```
Build: SUCCEEDED (0 errors, 0 warnings beyond embedded timestamps)
Tests: 742 passed, 0 failed, 0 skipped (8.7s)
```

## Remaining Backlog (Priority Order)

| Task | Priority | Status |
|------|----------|--------|
| TSK-0004 — Wire MoveToTool with ActionData.Context | High | InProgress |
| TSK-0166 — Modularize MineflayerAdapter | Medium | InProgress |
| TSK-0197 — Normalize ActionData.Tool | High | ✅ Done |
| TSK-0194 — HTTP retry/resilience | High | ✅ Done |
| TSK-0193 — Search error handling | High | ✅ Done |
| TSK-0176 — IBuildGoal marker interface | Medium | ✅ Done |
| TSK-0175 — IBuildMaterialProvider | Medium | 🚧 Blocked |
| TSK-0179 — Planned Block Ordering | Medium | Backlog |

## Notes for Next Agent

1. **TSK-0175 unblock**: The interface design is complete. To finish:
   - Add `private readonly IBuildMaterialProvider _materialProvider;` field + constructor param to `HtnTaskLibrary`
   - In `DecomposeBuild`, replace the `if (!isCreative)` block (~7 lines) with `_materialProvider.EmitProvisioningActions(blueprint, state, actions);`
   - Make `EmitSurvivalMaterialProvisioning` public
   - Register `BuildMaterialProvider` in DI: `builder.Services.AddSingleton<IBuildMaterialProvider, BuildMaterialProvider>();`
   - Delete the old `if (!isCreative)` diagnostic log lines

2. **TSK-0004** (MoveToTool context carry) and **TSK-0166** (Mineflayer modularization) are partially implemented — check their task JSON for current state.

3. **MapNodeActionToToolName** in AgentBackgroundService.cs line 1194 maps `"place"` → `"PlaceBlock"` (wire → ITool.Name). This is correct and should NOT be changed — it's used for JS error routing, not ActionData.Tool.
