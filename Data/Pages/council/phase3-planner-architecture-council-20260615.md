# Council Review: Phase 3 HTN/GOAP Planner Architecture

Date: 2026-06-15

## Decision

Accept Phase 3 as architecturally sound. The HtnPlanner + GoalFactory + AgentBackgroundService integration is correct and testable. Four items must be addressed before Phase 4 (Vision & GOAP); one is a pre-commit blocker.

## Evidence Reviewed

- `Agent.Planning/HtnPlanner.cs` — task library decomposition + phase-by-phase fallback
- `Agent.Planning/HtnTaskLibrary.cs` — GatherWood, SurviveNight, FindTree, MineWood, Collect decomposers
- `Agent.Planning/Goals/GatherWoodGoal.cs`, `SurviveNightGoal.cs`
- `Agent.Planning/GoalFactory.cs` + `Interfaces/IGoalFactory.cs`
- `WebUI.Blazor/AgentBackgroundService.cs` — IPlanner-wired dispatch with SetGoal + 3-strike guard
- `WebUI.Blazor/Program.cs` — DI, POST /api/agent/plan, GET /api/goals
- `MemorySmith.Agent.Tests/HtnPlannerTests.cs` — 11 tests
- `MemorySmith.Agent.Tests/GatherWoodGoalTests.cs` — 10 tests
- `MemorySmith.Agent.Tests/SimpleGoalTests.cs`, `ActionPlanTests.cs`, `MockPlanner.cs`
- Previous council: phase2-memory-integration-council-20260615.md

## Findings

| Seat | Recommendation | Confidence | Blocking Concern |
|---|---|---:|---|
| Source-Grounded Archivist | HtnPlanner correctly decomposes GatherWoodGoal (goal-level → GatherWoodDecompose) and SimpleGoal with known phases (phase-by-phase). `ReplanAsync` wraps `PlanAsync` with a try/catch returning null on failure. The task library has 8 named methods. Matches the architecture documented in `Data/Pages/planner.md`. | 93% | `ReplanAsync` creates a new `SimpleGoal` from the current plan's phases but discards the `TargetCount` parameter of `GatherWoodGoal`. Replanning GatherWoodGoal after partial collection will restart with the full target instead of remaining count. |
| Data Model Architect | `HtnTaskLibrary.MakeAction` mutates `ActionData.Arguments` after init-only construction. This works in C# (the dictionary is mutable; only the reference is init) but is a subtle gotcha. The `GatherWoodGoal.IsComplete` sums ALL `*_log` inventory keys, which is clean. `WorldState.Inventory` is a `Dictionary<string,int>` — the test correctly initializes it with `{ ["oak_log"] = 5 }`. | 90% | None blocking. Recommend a follow-up to add a typed `InventoryEntry` record to disambiguate item counts vs stack sizes. |
| Retrieval Specialist | `SearchMemory` is the FIRST action in `GatherWoodDecompose` — the plan asks memory for wood location before moving. This is the right integration point. However, the plan doesn't USE the SearchMemory result to inform the MoveTo coordinates — there's no adaptive execution layer yet. The MineBlock call uses hardcoded block names (`minecraft:oak_log`), but the actual block ID depends on the Minecraft version. | 88% | SearchMemory result is discarded at execution time (the tool engine just fires it and moves on). Phase 4 GOAP needs an "inject search result into next action" mechanism. This is a Phase 4 pre-requisite, not a Phase 3 blocker. |
| Human Learning Advocate | `POST /api/agent/plan` returns `ActionCount` and `Phases` which is good for observability. `GET /api/goals` lists available goals. However, the response doesn't include the goal description, and the `Goal` field in the status endpoint returns `null` for inactive agents (not the last-completed goal). | 85% | Non-blocking: add goal description to /api/agent/plan response for better DX. |
| Skeptical Reviewer | `AgentBackgroundService` now holds `IPlanner planner` as a REQUIRED constructor parameter. But `Program.cs` registers `AgentBackgroundService` as a singleton AND calls `builder.Services.AddHostedService(sp => sp.GetRequiredService<AgentBackgroundService>())`. This means if the planner DI registration fails, the hosted service silently won't start. Also: `ReplanAsync` uses `Task.ContinueWith` with `OnlyOnRanToCompletion` — if `PlanAsync` throws, `ContinueWith` will return a cancelled task, not `null`. The caller won't get `null` — it'll get an exception. | 87% | **Blocker**: `ReplanAsync` silently swallows the outer `try/catch` but the `ContinueWith` with `TaskContinuationOptions.OnlyOnRanToCompletion` throws `TaskCanceledException` when the antecedent faults. The `catch` block catches this and returns `null` — but only intermittently depending on scheduling. Rewrite `ReplanAsync` to use `await` + `try/catch` rather than `ContinueWith`. |
| Synthesizer | Phase 3 moves the project from "stubs" to "working planner". The architecture is correct: goals → planner → task library → action sequence → dispatch loop. Four findings need action, one immediately. The `ReplanAsync` ContinueWith issue is the pre-commit blocker (intermittent null/exception behavior). The GatherWood replan parameter loss is a Phase 4 concern. | 91% | Fix ReplanAsync before CI can be marked fully trusted on replanning paths. |

## Synthesis

**Phase 3 accepted** with one pre-commit fix and three deferred items.

**Fix immediately (pre-Phase 4 gate):**
1. **`ReplanAsync` ContinueWith** — Replace the `ContinueWith(OnlyOnRanToCompletion)` pattern with a proper `await + try/catch` that cleanly returns `null` on any failure, including planner exceptions.

**Defer to Phase 4:**
2. **Adaptive execution**: SearchMemory results must feed into subsequent tool arguments. Needs an "action context" mechanism where one tool result can modify the next action's arguments before dispatch.
3. **GatherWood replan parameter preservation**: When replanning, pass the remaining count (target - current inventory) rather than the full target. Requires `GatherWoodGoal` to accept a remaining-count constructor or expose a `GetRemainingCount(WorldState)` method.
4. **Block ID version sensitivity**: `minecraft:oak_log` is a 1.13+ ID. For older servers, it's `log`. Add a Minecraft version field to `MinecraftAdapterConfig` and parameterize block IDs in the task library.

## Dissent

- Retrieval Specialist is more concerned about the SearchMemory-result-discard than the Synthesizer ranks it. Recommends making it a Phase 3 fix (simple: add a "context carry" dict to `ActionData`) before shipping the planner to any real usage.
- Skeptical Reviewer also notes that the 3-strike failure guard in `AgentBackgroundService` is good, but the consecutive failure counter resets only on success — a partial success (5/10 logs gathered) won't reset it. Phase 4 should tie resets to goal progress, not just tool-call success.

## Acceptance Criteria for Phase 4 Entry

- [ ] `ReplanAsync` uses `await/try-catch` (not `ContinueWith`) — returns `null` on any failure
- [ ] CI remains green after the fix (24+ tests passing)
- [ ] At least 1 integration test verifying `AgentBackgroundService` calls `PlanAsync` when queue is empty with a goal set

## Open Questions

- Should `ActionData` carry a `Context` dictionary (mutable bag) that tools can write to and subsequent actions can read from? This enables SearchMemory → MoveTo coordinate injection.
- Should `HtnTaskLibrary` be open for extension (plugin-style task registration from outside) or kept as a closed class for Phase 4?
- When the LLM is added (Phase 4), should it produce `ActionData` sequences directly, or should it produce an HTN task name that the library then decomposes?
