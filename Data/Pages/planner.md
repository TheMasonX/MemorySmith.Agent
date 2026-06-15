# HTN/GOAP Hybrid Planner

The agent uses a **hybrid planner**: LLMs generate high-level goals/plans; deterministic HTN/GOAP logic decomposes them into atomic actions. This minimizes token usage, latency, and hallucination risk.

## Agent Loop

On each tick or event, the agent runs a reasoning cycle:

1. **Receive world event** — Node.js sends JSON (e.g. `{"event":"health","hp":8}`). `WorldState` is updated.
2. **Goal evaluation** — if current goal is complete or failed, decide next action.
3. **Planner call** — if a new plan is needed, call `IPlanner.PlanAsync(goal, state)`.
   - For *known* goals: HTN method library decomposes deterministically.
   - For *novel* goals: call LLM (via `IChatClient`) to produce a JSON plan.
4. **HTN decomposition** — break plan phases into atomic actions (through GOAP precondition checks).
5. **Action queue** — primitive actions enqueued (e.g. `MoveTo`, `MineBlock`, `GatherWood`).
6. **Execution** — `ToolEngine.Execute("MoveToTool", {x, y, z})` dispatches via the adapter.
7. **Repeat** until goal is done or user interrupts.

## HTN (Hierarchical Task Network)

A library of compound tasks with predefined decomposition methods:

- `GatherWoodGoal` → `[FindTree, MoveTo(tree), MineBlock(log) × N, CollectInventory]`
- `BuildHouseGoal` → `[GatherWood, GatherStone, LayFoundation, BuildWalls, AddRoof]`
- `SurviveNightGoal` → `[FindShelter | BuildShelter, LightArea, Wait(sunrise)]`

The LLM selects which HTN task to apply; code decomposes it.

## GOAP (Goal-Oriented Action Planning)

For ad-hoc problems where no HTN method matches, GOAP plans backward from goal to actions using action preconditions/effects.

Example: "no coal" sub-task fails during `BuildHouseGoal` → GOAP re-plans a `FindCoal` route.

## LLM Plan Format

```json
{
  "goal": "BuildCathedral",
  "phases": ["GatherStone", "LayFoundation", "BuildWalls", "FinishRoof"]
}
```

The planner returns this; HTN decomposes each phase into atomic actions.

## Failure & Replanning

- Action fails (e.g. path blocked) → log context to MemorySmith, call `IPlanner.ReplanAsync`.
- Repeated failure → LLM queried for emergency plan.
- User can inject tasks or override via `ManualOverride` flag via the Blazor UI.

## Implementation Status

| Component | Status |
|---|---|
| `IPlanner` interface | ✅ Defined |
| `HtnPlanner` stub | ✅ Phase 3 |
| GOAP engine | ⬜ Phase 3 |
| LLM integration (`IChatClient`) | ⬜ Phase 2 |
| Predefined task library | ⬜ Phase 3 |
