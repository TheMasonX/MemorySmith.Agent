# World Model Guide

The `IWorldModel` interface gives the agent an internal model of the world — separate from `WorldState` (ground truth events) — that tracks beliefs, predictions, and uncertainty.

**Introduced:** Sprint 6

---

## Overview

The world model sits between raw observations and the agent's planning decisions:

```
Mineflayer events → WorldState (ground truth)
                         ↓
                   IWorldModel.Observe() → ObservationState
                         ↓
                   IWorldModel.Predict() → PredictionState
                         ↓
                   IWorldModel.Reconcile() → updates uncertainty
                         ↓
                   IWorldModel.Uncertainty → 0.0–1.0 score
```

---

## Data Types

### ObservationState

Ground-truth snapshot from sensor events:

```csharp
public record ObservationState(
    Position Position,
    float Health,
    float Food,
    IReadOnlyDictionary<string, int> Inventory,
    DateTimeOffset Timestamp
);
```

### BeliefState

The agent's internal model — may differ from observations if sensors are stale:

```csharp
public record BeliefState(
    Position EstimatedPosition,
    float EstimatedHealth,
    IReadOnlyDictionary<string, int> EstimatedInventory,
    float Confidence,        // 0.0–1.0
    DateTimeOffset LastUpdated
);
```

### PredictionState

Expected outcome of a tool action:

```csharp
public record PredictionState(
    Position? PredictedPosition,
    IReadOnlyDictionary<string, int>? PredictedInventoryDelta,
    float Confidence,        // 0.0–1.0
    string Rationale
);
```

---

## IWorldModel Interface

```csharp
public interface IWorldModel
{
    void Observe(ObservationState observation);
    PredictionState Predict(string toolName, IReadOnlyDictionary<string, object> args);
    void Reconcile(ObservationState actual, PredictionState predicted);
    double Uncertainty { get; }
}
```

### Observe

Called after each world event to update the internal belief state.

### Predict

Called before dispatching a tool action. Returns expected outcome based on tool name and args.

### Reconcile

Called after `GetStatus` returns, comparing predicted vs actual outcome. Updates the running uncertainty score.

### Uncertainty

Running average of the last 20 deviation scores from `Reconcile`. Computed from position, food, and health deltas.

| Score | Meaning |
|-------|---------|
| 0.0 | World behaves exactly as predicted |
| 0.5 | Moderate uncertainty — predictions are rough |
| 1.0 | High uncertainty — world is unpredictable |

---

## Predictions by Tool

`WorldModel` implements rule-based predictions for these tools:

| Tool | Predicted outcome |
|---|---|
| `MoveTo` | Position moves toward target coordinates |
| `GetStatus` | Inventory and health refreshed; `IsInventoryStale = false` |
| `MineBlock` | Inventory gains `count` units of the block type |
| `CraftItem` | Inventory gains crafted item, loses ingredients |
| `PlaceBlock` | Inventory loses 1 unit of the material |
| `SmeltItem` | Inventory gains smelted item, loses raw material |
| `Wander` | Position changes by up to `radius` blocks |
| `Chat` | No world state change |
| `FindFlatArea` | No inventory change; provides flat origin coordinates |

---

## Thread Safety

`WorldModel` is thread-safe. `Observe`, `Predict`, and `Reconcile` use `lock(this)` internally. The uncertainty score uses a thread-safe running window of 20 samples.

---

## DI Registration

```csharp
// Program.cs
builder.Services.AddSingleton<IWorldModel, WorldModel>();
```

---

## Accessing the World Model

The world model is available via constructor injection anywhere in Agent.Core:

```csharp
public class MyService(IWorldModel worldModel)
{
    public void DoSomething()
    {
        var prediction = worldModel.Predict("MineBlock", new Dictionary<string, object>
        {
            ["block"] = "oak_log",
            ["count"] = 8
        });
        // prediction.PredictedInventoryDelta["oak_log"] == 8
        // prediction.Confidence == 0.85
    }
}
```

---

## Relationship to WorldState

`WorldState` is the **ground truth** — it's updated directly from Mineflayer events with no interpretation.

`IWorldModel` is the agent's **internal model** — it tracks beliefs, predictions, and uncertainty. It's used for:
- Pre-action planning (what do I expect to happen?)
- Post-action verification (did it happen as expected?)
- Uncertainty tracking (how reliable are my sensors?)

The two work together: `WorldState` feeds `Observe()`, and `Reconcile()` uses `WorldState` to update uncertainty.
