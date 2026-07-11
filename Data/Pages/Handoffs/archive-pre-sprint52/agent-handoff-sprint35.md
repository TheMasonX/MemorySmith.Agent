# Agent Handoff — Creative-Mode Planner Fix (2026-06-21)

**For:** next agent session  
**Status:** partial implementation; two regressions remain  
**Goal:** finish the creative-mode build-planner fix so creative-mode builds do not enter gather/mining/crafting pre-steps, and preserve the build-goal inventory-summary logging work.

---

## 1. What has already been changed

The current branch already contains partial work in these areas:

- [../../../Agent.Planning/HtnTaskLibrary.cs](../../../Agent.Planning/HtnTaskLibrary.cs) — build decomposition now has a creative-mode branch and an attempted crafted-material guard.
- [../../../WebUI.Blazor/AgentBackgroundService.cs](../../../WebUI.Blazor/AgentBackgroundService.cs) — build-goal inventory summaries were adjusted to be more task-relevant for plan logging.
- [../../../MemorySmith.Agent.Tests/HtnPlannerBuildTests.cs](../../../MemorySmith.Agent.Tests/HtnPlannerBuildTests.cs) and [../../../MemorySmith.Agent.Tests/AgentBackgroundServiceTests.cs](../../../MemorySmith.Agent.Tests/AgentBackgroundServiceTests.cs) — regression tests exist for the remaining behaviors.

---

## 2. Verified blocker

I verified the current state by running:

```powershell
dotnet test MemorySmith.Agent.Tests/MemorySmith.Agent.Tests.csproj
```

Result:
- 24 tests discovered
- 22 passed
- 2 failed

### Failing tests

1. `PlanAsync_BuildGoal_DoesNotMine_NonMineableBlocks`
   - Expected: no `MineBlock` action for a crafted prerequisite such as `oak_planks`
   - Actual: a `MineBlock` action is still emitted

2. `PlanCreation_LogsTaskRelevantInventoryForBuildGoal`
   - Expected: the plan log contains `cobblestone: 0/2` and `oak_planks: 0/1`
   - Actual: the `[plan]` log entry does not include that inventory summary

---

## 3. Files to inspect first

### Primary suspects
- [../../../Agent.Planning/HtnTaskLibrary.cs](../../../Agent.Planning/HtnTaskLibrary.cs)
  - Review the build decomposition path in `DecomposeBuild` and the crafting-chain helpers.
  - The remaining planner bug appears to be in the path that still emits mining actions for crafted prerequisites.

- [../../../WebUI.Blazor/AgentBackgroundService.cs](../../../WebUI.Blazor/AgentBackgroundService.cs)
  - Review `SummarizeTaskRelevantInventory` and the `[plan]` log emission path.
  - The logging failure suggests the BuildGoal summary is still not formatted the way the test expects.

### Regression tests
- [../../../MemorySmith.Agent.Tests/HtnPlannerBuildTests.cs](../../../MemorySmith.Agent.Tests/HtnPlannerBuildTests.cs)
- [../../../MemorySmith.Agent.Tests/AgentBackgroundServiceTests.cs](../../../MemorySmith.Agent.Tests/AgentBackgroundServiceTests.cs)

---

## 4. What the next agent should do

### Planner fix
- Preserve the creative-mode behavior that skips mining/smelting/crafting pre-steps for build plans.
- Make the crafted-material guard explicit enough that build decomposition cannot emit a `MineBlock` action for crafted prerequisites such as `oak_planks`.
- If the fix is implemented in one branch of the planner, verify that the same rule is respected in the other build-planning path as well.

### Logging fix
- Ensure the BuildGoal plan log includes a task-relevant inventory summary in the format expected by the test:
  - `cobblestone: 0/2`
  - `oak_planks: 0/1`
- Keep the summary focused on the current goal’s materials rather than falling back to a generic inventory dump.

---

## 5. Suggested verification loop

1. Re-run the targeted tests:
   ```powershell
   dotnet test MemorySmith.Agent.Tests/MemorySmith.Agent.Tests.csproj
   ```
2. If needed, inspect the actual emitted action list and log message with a temporary harness or targeted debugging.
3. Re-run the same command until both regressions are green.

---

## 6. Definition of done

The handoff is complete when:
- the two failing NUnit tests pass;
- creative-mode build plans still skip pre-gather actions; and
- BuildGoal plan logging contains the expected task-relevant inventory summary.
