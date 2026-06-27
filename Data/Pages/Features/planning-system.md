# Planning System

**Feature ID:** F-PLANNING  
**Status:** Core (Stable)  
**Location:** `Agent.Planning/`

The planning system converts goals into executable action sequences using a **two-layer architecture**: specialized decomposers for known goal types and a general HTN (Hierarchical Task Network) planner as fallback.

## Architecture

```
PlannerRouter
    ├── DecomposerRegistry.Find(goal)
    │     ├── BuildGoalDecomposer → HtnTaskLibrary.DecomposeBuild
    │     ├── GatherGoalDecomposer → MineBlock actions
    │     └── CraftItemGoalDecomposer → Gather + Craft actions
    └── HtnPlanner (fallback)
          └── HtnTaskLibrary (8 task methods)
```

## Decomposers

| Decomposer | Handles | Produces |
|-----------|---------|----------|
| `BuildGoalDecomposer` | BuildGoal | PlaceBlock, CraftItem, Gather actions (origin: explicit → facts → FindFlatArea) |
| `GatherGoalDecomposer` | GatherWoodGoal, IItemSpecGoal | MineBlock actions for target item |
| `CraftItemGoalDecomposer` | CraftItemGoal | Prerequisite gathering, crafting table placement, craft action |

## HTN Task Library (8 methods)

- `GatherWood` — SearchMemory → GetStatus → MineBlock → GetStatus
- `FindTree` — SearchMemory → GetStatus
- `MineWood` — MineBlock(oak_log) + MineBlock(birch_log)
- `Collect` — GetStatus
- `SurviveNight` — SearchMemory → GetStatus
- `FindShelter` — SearchMemory → GetStatus
- `LightArea` — GetStatus
- `WaitForSunrise` — GetStatus

## Creative vs Survival

- **Creative mode**: Uses `BlueprintExecutor` to emit PlaceBlock actions sorted by Y→Z→X
- **Survival mode**: Full HTN decomposition with gather/craft phases for each block

## Replanning

`ReplanAsync` preserves context keys (SearchMemory:, CraftItem:, FindFlatArea:, Build:, MoveTo:) across replans, allowing the bot to pick up where it left off.

## Related

- [Goal Types Catalog](../memories/Core/agent-goal-types-catalog.json)
- [Planner Architecture Memory](../memories/Core/agent-planner-architecture.json)
- [Blueprint System](blueprint-system.md)
- [Planner Wiki Page](../planner.md)
- [Adding a Goal Guide](../guides/adding-a-goal.md)
