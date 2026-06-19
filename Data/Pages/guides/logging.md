# Logging Guide

MemorySmith.Agent uses **Serilog** for structured logging with millisecond precision. Both a human-readable text log and a machine-readable JSON log are written to disk.

**Introduced:** Sprint 19

---

## Log Files

| File | Format | Level | Notes |
|---|---|---|---|
| `logs/agent-<date>.log` | Human-readable text | Debug | Rolls daily |
| `logs/agent-structured-<date>.json` | JSON (one object per line) | Debug | For log analysis tools |
| `MineflayerAdapter/logs/adapter-<date>.log` | JSON (logStructured) | Info | Node.js adapter events |

Log files are relative to the working directory (typically `WebUI.Blazor/`).

---

## Log Levels

| Level | Usage |
|---|---|
| `Verbose` | Rarely used; raw event streams |
| `Debug` | All tool calls, plan steps, inventory checks |
| `Information` | Goal set/complete/fail, stall recovery, intent resolved, thinking indicator |
| `Warning` | Damage interrupt triggered, World KB not configured, LLM timeout |
| `Error` | Unhandled exceptions, tool execution failures |
| `Fatal` | Agent crash |

Console output is at `Information` level. File output is at `Debug` level (full detail).

---

## Key Log Messages

### Goal Lifecycle

```
[Info]  Goal set: GatherItem:oak_log (count=32)
[Info]  Goal complete: GatherItem:oak_log — inventory: {oak_log: 32, cobblestone: 4}
[Info]  Goal failed: GatherItem:oak_log — FailureReason: TargetUnreachable
[Info]  Goal cancelled: GatherItem:oak_log
```

### Planning

```
[Debug] Plan created for GatherItem:oak_log: [SearchMemory, MineBlock, GetStatus]
[Debug] Action dispatched: MineBlock({block=oak_log, count=32}) elapsed=0ms
[Debug] Action completed: MineBlock — elapsed=4200ms
[Debug] Action failed: MineBlock — FailureReason: TargetUnreachable
[Info]  Replan triggered for GatherItem:oak_log (attempt 2)
```

### Replan Governor

```
[Info]  Governor: STALLED — 3 identical fingerprints, no inventory change
[Info]  Governor: recovered to ACTIVE after 60s timeout
[Debug] Governor: stall suppressed — IsStalled check before plan
```

### Chat & LLM

```
[Info]  [chat] <TheMasonX23> -> CreateGoal (CraftItem:iron_pickaxe)
[Info]  [chat] <TheMasonX23> -> CancelGoal
[Info]  [thinking] LLM slow — EnqueueThinkingIfSlowAsync fired for TheMasonX23
[Info]  Rate limited TheMasonX23: 3s cooldown (max 1/min global)
[Warn]  LLM call timed out after 10s for TheMasonX23 — falling back to pattern match
```

### Damage Interrupt

```
[Warn]  Damage interrupt: health 20→8 (delta=-12) — clearing queue, enqueue GetStatus
[Debug] Damage interrupt suppressed: 3s cooldown (last interrupt 1.2s ago)
[Debug] Damage interrupt suppressed: health 8 >= threshold 6 for GatherItem:oak_log
```

### Inventory Freshness

```
[Debug] IsComplete: false — IsInventoryStale=true for GatherItem:oak_log (waiting for GetStatus)
```

### Health Monitoring

```
[Info]  Health critical: 4/20 — enqueuing GetStatus
```

### World KB

```
[Warn]  WorldKbUrl is not configured — world knowledge base is disabled
[Info]  SearchMemory → World KB: query="diamond_ore", 3 results
[Info]  CreatePage → World KB: "Found diamond_ore at (10, 12, -5)"
```

---

## Serilog Configuration

Serilog is configured in `Program.cs`:

```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Information)
    .WriteTo.File(
        path: "logs/agent-.log",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        new JsonFormatter(),
        path: "logs/agent-structured-.json",
        rollingInterval: RollingInterval.Day)
    .CreateLogger();
```

### Structured Properties

The JSON log includes contextual properties:

```json
{
  "Timestamp": "2026-06-19T14:00:01.234Z",
  "Level": "Information",
  "MessageTemplate": "Goal set: {GoalName} (count={Count})",
  "Properties": {
    "GoalName": "GatherItem:oak_log",
    "Count": 32,
    "Elapsed": 0
  }
}
```

---

## JavaScript Adapter Logging

`MineflayerAdapter/index.js` uses `logStructured` for timing:

```js
logStructured('MineBlock', { block: 'oak_log', count: 8, elapsed: 4200, success: true });
```

Output goes to `MineflayerAdapter/logs/adapter-<date>.log` as JSON:

```json
{"event":"MineBlock","block":"oak_log","count":8,"elapsed":4200,"success":true,"timestamp":"2026-06-19T14:00:05.123Z"}
```

---

## Searching Logs

```bash
# All warnings and errors
grep -E "^\[Warn\]|\[Err\]" logs/agent-2026-06-19.log

# All goal lifecycle events
grep "Goal" logs/agent-2026-06-19.log

# All damage interrupt events
grep "Damage interrupt" logs/agent-2026-06-19.log

# Action timings (jq required)
cat logs/agent-structured-2026-06-19.json | jq 'select(.MessageTemplate | contains("elapsed"))'
```

---

## Log Rotation

Logs roll daily. Old log files are kept indefinitely (no auto-purge). To limit disk usage, add `retainedFileCountLimit` to the Serilog configuration:

```csharp
.WriteTo.File(path: "logs/agent-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
```
