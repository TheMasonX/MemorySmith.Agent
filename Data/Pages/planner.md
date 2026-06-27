# HTN Planner & Agent Loop

The agent uses a **hierarchical task network (HTN)** planner with a **decomposer registry** that makes goal decomposition pluggable. Deterministic decomposers handle known goals; the planner falls back to HTN methods for unrecognized goals.

## Agent Loop

`AgentBackgroundService` processes events and drives the agent loop:

1. **Process events** — `ProcessEventsAsync` drains the event queue:
   - `HealthEvent` → update `WorldState`, synthesize `DamageTakenEvent` if delta is negative
   - `DamageTakenEvent` → `TryInterruptOnDamage` (see [Damage Interrupt](guides/damage-interrupt.md))
   - `ChatEvent` → `HandleChatEventAsync` → `IChatInterpreter`
   - `PositionEvent`, `InventoryEvent` → update `WorldState`
   - `BlockNotFoundEvent` → `WorldStateProjector.Apply` → adds `BlockNotFound` fact

2. **Goal evaluation** — if current goal `IsComplete` or `HasFailed`, clear goal and idle.

3. **Governor check** — `IReplanGovernor.IsStalled` checked before planning. If stalled: 10s delay, skip `PlanAsync`.

4. **Planner call** — `PlannerRouter.PlanAsync(goal, state)`:
   - Tries each `IGoalDecomposer` in `DecomposerRegistry` via `CanHandle`
   - Falls back to `HtnPlanner` for unmatched goals

5. **Action dispatch** — `DispatchActionsAsync` iterates the plan:
   - Calls `ToolDispatcher.CallAsync` per action with 30s timeout
   - Records timing via `Stopwatch`
   - Checks `IsStalled` before each replan
   - Logs staleness when `IsComplete` returns false due to stale inventory

6. **Progress recording** — `ReplanGovernor.RecordProgress()` called when `sum(Inventory)` changes after 300ms settle.

7. **Repeat** — loops until goal complete or user interrupts.

## Decomposer Registry (Sprint 6+)

`DecomposerRegistry` holds pluggable `IGoalDecomposer` implementations:

```csharp
public interface IGoalDecomposer
{
    bool CanHandle(IGoal goal);
    ActionPlan Decompose(IGoal goal, WorldState state);
}
```

Registered decomposers (all in DI, registered via `DecomposerRegistry.Register`):

| Decomposer | Handles | Default Plan |
|---|---|---|
| `BuildGoalDecomposer` | `BuildGoal` | Origin resolution (explicit→facts→FindFlatArea) → gather materials → place blocks |
| `GatherGoalDecomposer` | `GatherWoodGoal`, `IItemSpecGoal` | SearchMemory → MineBlock → GetStatus (passes TargetCount, not default 10 — Sprint 26) |
| `CraftItemGoalDecomposer` | `CraftItemGoal` | Gather prerequisites → place crafting table → craft item (handles 2x2+ grid recipes) |
| `SurviveNightGoalDecomposer` | `SurviveNight` goals | FindFlatArea → build shelter → Chat |

`PlannerRouter` tries decomposers first via `registry.Find(goal)`, then falls back to `HtnPlanner`.

### CraftItemGoalDecomposer (Sprint 22+)

Handles the full craft pipeline: checks if the recipe requires a crafting table (2x2+ grid), searches for or places one, gathers prerequisite materials, crafts N items, and cleans up. Registered in `DecomposerRegistry` and routed by `PlannerRouter` when `goal is CraftItemGoal`.

## Goal Decomposition Details

### Gather Goal

Default gather plan (Sprint 19):
1. `SearchMemory` — query World KB for known block locations
2. `MineBlock` — mine target block
3. `GetStatus` — refresh inventory state

`Wander` is **conditional** — only added when `WorldState` has a `BlockNotFound` fact matching the source block name. This prevents aimless wandering when the bot simply hasn't looked yet.

### Build Goal

`BuildGoalDecomposer` with `requireOrigin` flag:
- `requireOrigin: false` (default) — can plan without a known origin
- `requireOrigin: true` — returns `FindFlatArea`-only plan if no origin resolvable

Radius retry: first pass uses radius 32; if `FindFlatArea` returns zero area, retry with radius 48.

### HtnPlanner

For goals not matched by a decomposer, `HtnPlanner` uses a method library:

- `GatherWoodGoal` → `[FindTree, MoveTo(tree), MineBlock(log) × N, GetStatus]`
- `BuildHouseGoal` → `[GatherWood, GatherStone, LayFoundation, BuildWalls, AddRoof]`
- `SurviveNightGoal` → `[FindShelter | BuildShelter, LightArea, Wait(sunrise)]`

`HtnPlanner.IItemSpecGoal` branch passes `GenericGatherGoal.TargetCount` as `parameters[0]`, ensuring "get 100 sand" produces a count=100 plan not count=10.

## Replanning

`ReplanAsync` preserves context across replans — entries with these prefixes survive context wipe:
- `SearchMemory:`, `CraftItem:`, `FindFlatArea:`, `Build:`, `MoveTo:`

This ensures the agent doesn't lose block-location knowledge when replanning due to a single action failure.

## Replan Governor

See [Replan Governor Guide](guides/replan-governor.md) for full details.

**Quick summary:** `ReplanGovernor` tracks:
1. Plan fingerprints (hash of action sequence)
2. Inventory delta (sum of all inventory item counts)

If 3 consecutive identical plan fingerprints occur **without** any inventory change → STALLED.  
Recovery: 60s auto-recovery to ACTIVE state.

## GoalFactory & Item Classification

`GoalFactory` creates `IGoal` instances from goal names and parameters via a `Dictionary<string, Func<...>>` registry. Returns `null` if goalName is not registered (logs warning with available names).

Item classification via `LocalKnowledgeResolver.ResolveAsync` (Phase 7-B pipeline):

1. **Normalize query** — lowercase, trim, strip pluralization
2. **IItemRegistry.GetAsync** — exact match against item registry pages (confidence 0.95)
3. **IMemoryGateway.SearchAsync** — wiki search results (confidence 0.60 × search score)
4. **WorldState.StructuredFacts scan** — recent world facts (0.70 if < 60s, 0.50 if older)
5. **Type filter + confidence cap + TopN sort** — filter by `CandidateType` match
6. **Ambiguity detection** — if top-2 candidates within 0.05 confidence → mark as ambiguous

See [memory.md](memory.md) for the full resolver pipeline details.

**Stone aliases:** `stone` resolves through `SourceBlocks` to include `cobblestone` for correct `IsComplete` behavior.

## Implementation Status

| Component | Status |
|---|---|
| `IPlanner` interface | ✅ Defined |
| `HtnPlanner` | ✅ Sprint 3 |
| `DecomposerRegistry` | ✅ Sprint 6 |
| `BuildGoalDecomposer` | ✅ Sprint 6 |
| `GatherGoalDecomposer` | ✅ Sprint 6 |
| `SurviveNightGoalDecomposer` | ✅ Sprint 6 |
| `PlannerRouter` | ✅ Sprint 6 |
| `ReplanGovernor` (ACTIVE/STALLED) | ✅ Sprint 19 |
| Progress-hash governor | ✅ Sprint 20 |
| Governor pre-plan check | ✅ Sprint 21 |
| `IItemSpecGoal` count fix | ✅ Sprint 22 |
| LLM integration (`IChatClient`/`ILlmProvider`) | ✅ Sprint 11 |
