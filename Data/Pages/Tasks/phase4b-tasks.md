# Phase 4b Tasks — Blueprint Construction System

**Status:** COMPLETE  
**Started:** 2026-06-16  
**Closed:** 2026-06-16

---

## Completed This Phase

### D1 — GoalFactory.RegisteredGoals exposes dynamic prefixes
- `RegisteredGoals` now returns `["GatherWood", "SurviveNight", "GatherItem:{itemId}", "Build:{blueprintId}"]`
- REST callers can discover both static and dynamic goal types

### D2 — IItemSpecGoal marker interface
- `Agent.Core/Interfaces/IItemSpecGoal.cs` — `IItemSpecGoal : IGoal { ItemSpec Spec { get; } }`
- `GenericGatherGoal` now implements `IItemSpecGoal`
- `HtnPlanner` dispatches on `goal is IItemSpecGoal` instead of `goal is GenericGatherGoal`
- Prevents `goal is ConcreteType` pattern from spreading to future item goals

### D3 — GoalFactory.CreateAsync null-registry warnings
- `CreateAsync("GatherItem:*")` with null `IItemRegistry` → `Debug.WriteLine` warning + return null
- `CreateAsync("Build:*")` with null `IBlueprintRepository` → `Debug.WriteLine` warning + return null
- No exceptions thrown; callers can check for null return gracefully

### Blueprint Schema Enhancement
- `Agent.Construction/BlueprintSchema.cs` — added `PlacementBlock(int X, int Y, int Z, string BlockId)` record
- Enables programmatic block placement from parsed blueprint data

### BlueprintParser (new)
- `Agent.Construction/BlueprintParser.cs` — static parser: markdown → (Blueprint, IReadOnlyList<PlacementBlock>)
- Parses frontmatter: id, name, tags, dimensions, materials, description
- Parses `### Y=N` layer grids, maps symbols to block IDs via built-in or page-defined legend
- Rejects prose lines via `IsValidGridRow` (all chars must be in legend)
- Handles custom legend overrides; supports `null`/`skip`/`air` for skipped cells

### BlueprintExecutor + IBlueprintExecutor (new)
- `Agent.Construction/Interfaces/IBlueprintExecutor.cs` — interface
- `Agent.Construction/BlueprintExecutor.cs` — emits PlaceBlock ActionData, Y-ascending order
- Arguments: `material`, `x`, `y`, `z` matching PlaceBlockTool signature

### BuildGoal (new)
- `Agent.Planning/Goals/BuildGoal.cs` — phases: GatherMaterials, Build, Verify
- `Name = "Build:{blueprintId}"`, IsComplete/HasFailed via world-state facts
- Holds `Blueprint` metadata + `IReadOnlyList<PlacementBlock>` blocks

### GoalFactory — Build support (updated)
- Optional `IBlueprintRepository?` constructor parameter
- `CreateAsync("Build:{blueprintId}")` → repository lookup → `BuildGoal(blueprint, blocks)`
- See D1 and D3 above

### HtnPlanner — BuildGoal branch (updated)
- New branch: `else if (goal is BuildGoal bg)` → `library.DecomposeBuild(...)`
- Reads build origin from world-state facts: `build:{id}:origin:x/y/z` (defaults to 0)
- Uses IItemSpecGoal interface check for gather goals (D2)

### HtnTaskLibrary — DecomposeBuild (updated)
- `DecomposeBuild(Blueprint, IReadOnlyList<PlacementBlock>, int ox, int oy, int oz, WorldState)`
- GatherMaterials phase: emits SearchMemory + Wander + MineBlock for each directly-mineable material not yet in inventory
- Navigation: SearchMemory + MoveTo origin
- Build phase: delegates to BlueprintExecutor for all PlaceBlock actions
- Verify: GetStatus

### MemorySmithBlueprintRepository (new)
- `Agent.Memory/MemorySmithBlueprintRepository.cs` — implements `IBlueprintRepository`
- Page lookup: `blueprints/{id-slug}`, search fallback mirrors MemorySmithItemRegistry
- `SaveAsync` throws `NotImplementedException` (Phase 5)

### Project Reference Updates
- `Agent.Planning/Agent.Planning.csproj` — added `Agent.Construction` reference
- `Agent.Memory/Agent.Memory.csproj` — added `Agent.Construction` reference
- `MemorySmith.Agent.Tests/MemorySmith.Agent.Tests.csproj` — added `Agent.Construction` reference

### Small House Blueprint
- `Data/Pages/blueprints/small-house.md` — 9W×5H×7D house
- Y=0: cobblestone floor
- Y=1: oak plank walls with oak log corners, door (south-center), crafting table, double chest
- Y=2: windows (glass pane north + sides), torches, bed
- Y=3: upper walls
- Y=4: oak slab roof

### Item Registry Pages (9 new)
- `Data/Pages/item-registry/cobblestone.md`
- `Data/Pages/item-registry/oak-planks.md`
- `Data/Pages/item-registry/glass-pane.md`
- `Data/Pages/item-registry/torch.md`
- `Data/Pages/item-registry/crafting-table.md`
- `Data/Pages/item-registry/chest.md`
- `Data/Pages/item-registry/oak-slab.md`
- `Data/Pages/item-registry/oak-door.md`
- `Data/Pages/item-registry/red-bed.md`

### Tests (42 new — total now ~132)
- `BuildGoalTests.cs` — 14 tests: name, description, phases, IsComplete, HasFailed, retention
- `BlueprintParserTests.cs` — 18 tests: frontmatter, dimensions, materials, grid layers, legend, edge cases
- `GoalFactoryBuildTests.cs` — 14 tests: D1 registered goals, D3 null-repo, Build prefix, case insensitivity
- `HtnPlannerBuildTests.cs` — 14 tests: PlaceBlock count/args, origin offset, mining, non-mineable skip

---

## Deferred to Phase 5

| ID | Description | Effort |
|----|-------------|--------|
| P5-01 | FurnaceTool + smelting chain | Large |
| P5-02 | CraftItemTool (planks, slabs, doors, torches, chests, beds) | Large |
| P5-03 | LLM-driven CreatePage for unknown item IDs in GoalFactory | Medium |
| P5-04 | Node.js mine loop reading block variants from ActionData arguments | Medium |
| P5-05 | AgentBackgroundServiceTests error-channel test (blockNotFound path) | Small |
| P5-06 | MemorySmithBlueprintRepository.SaveAsync implementation | Medium |
| P5-07 | BlueprintChunker: split large blueprints into multi-plan segments | Medium |
| P5-08 | Door/bed facing direction arguments in PlaceBlock actions | Small |
| P5-09 | Multi-level blueprint (floors stacked with stairs/ladder access) | Large |
| P5-10 | Terrain clearing before building (FlattenAreaTool) | Medium |

---

## Known Gaps / Bugs Noted

- **Build origin default (0,0,0):** If no origin facts are set, the house is built at world origin. A `FindBuildSiteTool` or a REST endpoint to set origin facts is needed before live use.
- **Large plan size:** A 9×5×7 house produces ~330 PlaceBlock actions in a single plan. The ActionQueue handles them serially but a timeout or chunk size may be needed.
- **Door facing:** The PlaceBlockTool doesn't pass a facing direction to Mineflayer for doors. Bot must be facing the correct direction at placement time.
- **Bed facing:** Same issue — bed facing is determined by bot yaw at placement time, not by arguments.
- **Crafted-item gathering:** DecomposeBuild emits MineBlock only for `DirectMineBlocks` set. Crafted items (planks, slabs) require manual prep or CraftItemTool (Phase 5).
- **Glass requires smelting:** glass_pane has `requires_smelting: true` but FurnaceTool is not yet implemented.