# Guide: Adding a New Goal

Goals are high-level objectives the agent pursues. Each goal defines what success looks like (`IsComplete`), when to give up (`HasFailed`), and which HTN phases to execute.

## Step 1 — Create the goal class

Add a file to `Agent.Planning/Goals/`:

```csharp
// Agent.Planning/Goals/BuildHouseGoal.cs
namespace Agent.Planning.Goals;

using Agent.Core;

public sealed class BuildHouseGoal(string material = "oak_planks") : IGoal
{
    public string Name => "BuildHouse";
    public string Description => $"Build a basic house using {material}.";
    public string[] Phases => ["GatherMaterial", "LayFoundation", "BuildWalls", "AddRoof"];

    public bool IsComplete(WorldState state) =>
        state.Facts.TryGetValue("houseBuilt", out var v) && v is true;

    public bool HasFailed(WorldState state) =>
        state.Facts.TryGetValue("goal:BuildHouse:failed", out var v) && v is true;
}
```

**IsComplete** must be a pure state predicate — check inventory, facts, or world state.
**HasFailed** is optional (defaults to false if not overridden).

## Step 2 — Register in GoalFactory

Open `Agent.Planning/GoalFactory.cs` and add to the `Creators` dictionary:

```csharp
["BuildHouse"] = p => new BuildHouseGoal(
    GetString(p, "material", "oak_planks")),
```

Add a `GetString` helper if needed:
```csharp
private static string GetString(IReadOnlyDictionary<string, object?>? p, string key, string def) =>
    p?.TryGetValue(key, out var v) == true ? v?.ToString() ?? def : def;
```

## Step 3 — Add task decompositions

Open `Agent.Planning/HtnTaskLibrary.cs`. Add entries to `_methods` in the constructor:

```csharp
["BuildHouse"]       = BuildHouseDecompose,
["GatherMaterial"]   = GatherMaterialDecompose,
["LayFoundation"]    = LayFoundationDecompose,
```

Then implement the decomposers:

```csharp
private static IReadOnlyList<ActionData> BuildHouseDecompose(
    string[] params, WorldState state) =>
[
    MakeAction("SearchMemory", ("query", "house blueprint foundation walls roof")),
    MakeAction("MineBlock", ("block", "minecraft:oak_log"), ("count", (object?)20)),
    MakeAction("GetStatus"),
];
```

## Step 4 — Write tests

Add `BuildHouseGoalTests.cs` to `MemorySmith.Agent.Tests/`:

```csharp
[TestFixture]
public class BuildHouseGoalTests
{
    [Test]
    public void Name_Is_BuildHouse()
        => Assert.That(new BuildHouseGoal().Name, Is.EqualTo("BuildHouse"));

    [Test]
    public void IsComplete_False_Initially()
        => Assert.That(new BuildHouseGoal().IsComplete(new WorldState()), Is.False);

    [Test]
    public void IsComplete_True_WhenFactSet()
    {
        var state = new WorldState { Facts = new() { ["houseBuilt"] = (object?)true } };
        Assert.That(new BuildHouseGoal().IsComplete(state), Is.True);
    }
}
```

## Step 5 — Try it

```bash
curl -X POST http://localhost:5000/api/agent/plan \
  -H "Content-Type: application/json" \
  -d '{"goalName":"BuildHouse","parameters":{"material":"spruce_planks"}}'
```

## Design principles

- **One goal class per logical objective** — don't combine "gather" and "build" in one class.
- **IsComplete must be deterministic** — it's called every dispatch cycle.
- **Use world state facts as signals** — tools write `goal:{Name}:failed = true` to signal failure.
- **Keep phases at a high level** — HtnTaskLibrary handles the decomposition details.
