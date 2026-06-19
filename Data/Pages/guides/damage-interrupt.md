# Damage Interrupt Guide

The damage interrupt system lets the agent react immediately when it takes significant damage — pausing the current goal and checking its health before deciding what to do next.

**Introduced:** Sprint 23

---

## Overview

```
Mineflayer health events → HealthEvent (C#-side)
        ↓
AgentBackgroundService._previousHealth comparison
        ↓
  delta < 0 ?  → synthesize DamageTakenEvent
        ↓
TryInterruptOnDamage
  ├─ Is delta within DamageInterruptThresholdHp?  → interrupt
  ├─ Is cooldown elapsed (3s)?                    → proceed
  └─ Log decision (Warning on interrupt, Debug on suppress)
        ↓
SendEmergencyStop + ActionQueue.ClearAndEnqueue(GetStatus)
```

---

## DamageTakenEvent

`DamageTakenEvent` is synthesized C#-side from consecutive `HealthEvent` deltas. It is **not** sent directly from Mineflayer.

```csharp
public record DamageTakenEvent(
    float PreviousHealth,
    float Health,
    float Delta,       // always negative
    float Food,
    DateTimeOffset Timestamp
);
```

---

## Interrupt Threshold

Each `IGoal` can declare its damage interrupt threshold:

```csharp
public interface IGoal
{
    // null = use system default (6 HP)
    // 0    = never interrupt (reserved for combat goals)
    float? DamageInterruptThresholdHp { get; }
}
```

The default implementation returns `null`, which uses the system default of **6 HP (3 hearts)**.

**System default:** Interrupt fires when `delta <= -DamageInterruptThresholdHp`.

| ThresholdHp | Meaning |
|-------------|---------|
| `null` | Use system default (6 HP) |
| `6.0f` | Interrupt if > 3 hearts of damage in one tick |
| `2.0f` | Interrupt on any non-trivial hit |
| `0` | Never interrupt (reserved for future combat goals) |

---

## Cooldown Settings

| Setting | Default | Description |
|---------|---------|-------------|
| `DamageInterruptCooldownSeconds` | `3` | Minimum seconds between interrupt triggers |
| `HealthCheckCooldownSeconds` | `2` | Minimum seconds between passive health `GetStatus` enqueues |

Both settings are in `appsettings.json` under `Agent:`.

---

## ActionQueue.ClearAndEnqueue (Sprint 23)

Before Sprint 23, clearing the queue and enqueuing `GetStatus` was two separate operations — not atomic. `ClearAndEnqueue` is a lock-protected atomic operation:

```csharp
// Atomically clear the queue and set a single action
actionQueue.ClearAndEnqueue(new ActionData { ToolName = "GetStatus" });
```

This prevents a race condition where an in-progress action could be dispatched between the clear and the enqueue.

---

## Rate Limiting

The interrupt uses two fields to prevent thrash:

| Field | Purpose |
|-------|---------|
| `_lastDamageInterruptAt` | Prevents re-interrupting within `DamageInterruptCooldownSeconds` |
| `_lastHealthStatusEnqueuedAt` | Prevents passive `GetStatus` spam within `HealthCheckCooldownSeconds` |

On `SetGoal`, both fields are reset to `DateTimeOffset.MinValue` so the new goal starts with a clean interrupt state.

---

## Logging

| Scenario | Log level | Message |
|----------|-----------|---------|
| Interrupt fires | Warning | `Damage interrupt: health 20→8 (delta=-12) — clearing queue, enqueue GetStatus` |
| Suppressed by cooldown | Debug | `Damage interrupt suppressed: 3s cooldown (last 1.2s ago)` |
| Suppressed by threshold | Debug | `Damage interrupt suppressed: delta -2 < threshold 6 for GatherItem:oak_log` |

---

## Per-Goal Customization

To customize the threshold for a specific goal:

```csharp
public class MyCombatGoal : IGoal
{
    // Never interrupt — this goal handles health itself
    public float? DamageInterruptThresholdHp => 0;

    public bool IsComplete(WorldState state) => /* ... */;
    public bool HasFailed(WorldState state) => /* ... */;
}
```

```csharp
public class MyFragileGoal : IGoal
{
    // Interrupt on any hit > 1 heart
    public float? DamageInterruptThresholdHp => 2.0f;
    // ...
}
```

---

## Health-Critical vs Damage Interrupt

These are two separate systems:

| System | Trigger | Action | Sprint |
|--------|---------|--------|--------|
| **Health-critical** | Health < `HealthCriticalThreshold` (6 HP) after any event | Enqueue `GetStatus` passively | Sprint 22 |
| **Damage interrupt** | Health delta ≥ `DamageInterruptThresholdHp` in one tick | Atomic clear + `GetStatus` | Sprint 23 |

The health-critical check is passive (doesn't clear the queue). The damage interrupt is aggressive (clears the queue immediately).

---

## Testing

```csharp
// Simulate damage interrupt in tests
var event = new DamageTakenEvent(
    PreviousHealth: 20f,
    Health: 6f,
    Delta: -14f,
    Food: 20f,
    Timestamp: DateTimeOffset.UtcNow);

// Inject into service — queue should be cleared
```

Integration tests for `TryInterruptOnDamage` are planned for Sprint 24 (D-8 deferred from Sprint 23).
