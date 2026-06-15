# Phase 3 Tasks — HTN/GOAP Planner

Status: **COMPLETE** (2026-06-15)

## Completed

- [x] `Agent.Core/Models/SimpleGoal.cs` — concrete IGoal with lambda predicates
- [x] `Agent.Core/Models/ActionPlan.cs` — concrete IPlan with immutable action list
- [x] `Agent.Planning/HtnTask.cs` — task record types and TaskDecomposer delegate
- [x] `Agent.Planning/HtnTaskLibrary.cs` — 8 named task decompositions (GatherWood, FindTree, MineWood, Collect, SurviveNight, FindShelter, LightArea, WaitForSunrise)
- [x] `Agent.Planning/HtnPlanner.cs` — real implementation with phase-by-phase fallback and await/try-catch ReplanAsync
- [x] `Agent.Planning/Goals/GatherWoodGoal.cs` — collects any *_log blocks
- [x] `Agent.Planning/Goals/SurviveNightGoal.cs` — tracks timeOfDay and inShelter facts
- [x] `Agent.Planning/Interfaces/IGoalFactory.cs` — factory interface
- [x] `Agent.Planning/GoalFactory.cs` — GatherWood + SurviveNight registered
- [x] `WebUI.Blazor/AgentBackgroundService.cs` — IPlanner integrated, SetGoal(), 3-strike failure guard
- [x] `WebUI.Blazor/Program.cs` — HtnTaskLibrary + HtnPlanner + IGoalFactory DI; POST /api/agent/plan; GET /api/goals
- [x] Tests: SimpleGoalTests (5), ActionPlanTests (4), GatherWoodGoalTests (10), HtnPlannerTests (11), MockPlanner
- [x] CI green: commit `ca17f75b4c` — 42+ tests passing
- [x] Council review: `Data/Pages/council/phase3-planner-architecture-council-20260615.md`
- [x] Council fix: `ReplanAsync` uses `await/try-catch` (not ContinueWith)

## Pre-Phase-4 acceptance criteria (from council)

- [ ] `AgentBackgroundServiceTests` — integration test verifying PlanAsync called when queue empty + goal set
- [ ] SearchMemory result feed into subsequent tool args (action context carry)
- [ ] Block ID version awareness in HtnTaskLibrary (configurable per MC version)

## Notes

The GatherWood decomposer includes `SearchMemory` as the first action, but the result is currently discarded — the MoveTo coordinates are not derived from the search result. This is the primary Phase 4 improvement.
