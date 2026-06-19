# Replan Governor Guide

The `IReplanGovernor` detects when the agent is stuck in a nonproductive loop (STALLED) and prevents it from spinning in rapid-fire replanning cycles.

**Introduced:** Sprint 19 (basic governor), Sprint 20 (progress-hash), Sprint 21 (pre-plan check)

---

## Overview

The governor tracks two signals:
1. **Plan fingerprint** — a hash of the current action sequence. If the same plan is generated 3 times in a row without progress, the governor calls STALLED.
2. **Inventory delta** — the sum of all inventory item counts. Progress is only recorded when this changes (prevents tools that complete in 0ms from masking stagnation).

```
PlanAsync returns → governor.RecordPlan(fingerprint)
        ↓
After 300ms settle → check inventory delta
        ↓
Delta > 0? → governor.RecordProgress()    ← resets fingerprint counter
Delta = 0? → governor checks fingerprint count
        ↓
3 identical fingerprints + no progress → STALLED
```

---

## States

| State | Description | Behavior |
|-------|-------------|----------|
| `ACTIVE` | Normal operation | Full speed |
| `STALLED` | Stuck loop detected | 10s delay before retry, skip `PlanAsync` |

The governor transitions STALLED → ACTIVE automatically after 60 seconds (`RecoveryTimeoutSeconds`).

---

## IReplanGovernor Interface

```csharp
public interface IReplanGovernor
{
    bool IsStalled { get; }
    void RecordPlan(string fingerprint);
    void RecordProgress();
    void Reset();
}
```

---

## Pre-Plan Stall Check (Sprint 21)

`DispatchActionsAsync` checks `IsStalled` **before** calling `PlanAsync`:

```
IsStalled == true → 10s delay → skip PlanAsync → retry next cycle
IsStalled == false → call PlanAsync normally
```

This prevents wasted plan computation during stalls.

---

## Progress Hash (Sprint 20)

Progress is gated on a **settled inventory delta**:

1. After a tool completes, wait 300ms (settle time)
2. Compute `sum(Inventory)` — total item count across all slots
3. If different from the snapshot at cycle start → `RecordProgress()`
4. If unchanged → no progress recorded

The 300ms settle prevents fast tools (e.g. `Chat`, `GetStatus`) from immediately masking inventory stagnation.

`_cycleInventorySnapshot` is updated at the start of each dispatch cycle, not per-tool.

---

## Configuration

All settings are in `appsettings.json` under `Agent:Governor:`:

```json
{
  "Agent": {
    "Governor": {
      "StallThreshold": 3,
      "RecoveryTimeoutSeconds": 60,
      "ProgressSettleMs": 300
    }
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `StallThreshold` | `3` | Number of identical fingerprints before STALLED |
| `RecoveryTimeoutSeconds` | `60` | Seconds before automatic ACTIVE recovery |
| `ProgressSettleMs` | `300` | Milliseconds to wait before checking inventory delta |

All settings are also injectable via the `ReplanGovernorOptions` constructor for testing (pass zero-delay values to speed up tests).

---

## Plan Fingerprint

A plan fingerprint is the hash of the ordered action sequence:

```csharp
var fingerprint = string.Join(",", plan.Actions.Select(a => a.ToolName));
// e.g. "SearchMemory,MineBlock,GetStatus"
```

If the same fingerprint appears 3 consecutive times with no inventory change, the governor transitions to STALLED.

---

## Governor Recovery Log

On STALLED → ACTIVE transition:

```
[Info] Governor: recovered to ACTIVE after 60s timeout
```

On ACTIVE → STALLED transition:

```
[Info] Governor: STALLED — 3 identical fingerprints, no inventory change
```

On pre-plan stall suppress:

```
[Debug] Governor: stall suppressed — 10s delay, skipping PlanAsync
```

---

## Testing

`ReplanGovernorOptions` accepts optional zero-delay values for test injection:

```csharp
var governor = new ReplanGovernor(new ReplanGovernorOptions
{
    StallThreshold = 3,
    RecoveryTimeoutSeconds = 0,   // instant recovery for tests
    ProgressSettleMs = 0          // no settle delay for tests
});

// Simulate 3 identical plans without progress
governor.RecordPlan("SearchMemory,MineBlock,GetStatus");
governor.RecordPlan("SearchMemory,MineBlock,GetStatus");
governor.RecordPlan("SearchMemory,MineBlock,GetStatus");

Assert.That(governor.IsStalled, Is.True);
```

---

## Common Scenarios

### Scenario: Block Not Found

1. Agent plans `SearchMemory → MineBlock`
2. `MineBlock` fails with `TargetUnreachable` 3 times
3. Governor detects 3 identical fingerprints → STALLED
4. 60s timeout → ACTIVE
5. On recovery: `Wander` is now conditionally added to the plan (via `BlockNotFound` fact)

### Scenario: Inventory Full

1. Agent mines block repeatedly
2. Inventory is full → mining completes but inventory count doesn't change (items dropped)
3. No inventory delta recorded
4. Governor stalls after 3 cycles
5. Expected: goal should detect `InventoryFull` failure reason and cancel

### Scenario: Fast Tools Not Masking Progress

1. Agent plan includes `Chat` (instant) + `MineBlock`
2. `Chat` completes in 0ms → inventory unchanged
3. 300ms settle: `MineBlock` hasn't run yet
4. Governor doesn't record progress from `Chat` alone
5. After `MineBlock` completes: inventory changes → `RecordProgress()` called
