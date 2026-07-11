# Sprint 49 Wave C — Dashboard Wave 1 + Audit-Driven Fixes

**Date:** 2026-06-25  
**Branch:** `sprint-35-llm-first`  
**Author:** SteveBot  
**Tests:** 722 passing, 0 failing

---

## Summary

Two waves of work in this session:

### Wave 1: Dashboard Infrastructure (first commit)
Validated two audit documents against the current codebase, then implemented foundational dashboard improvements.

### Wave 2: Audit-Deferred Fixes (TSK-0113, TSK-0112)
Implemented two of the four deferred tasks from the Sprint 49 Wave B handoff.

---

## Changes Implemented

### Dashboard Wave 1 — `e01b2f2`

| File | Change |
|------|--------|
| `WebUI.Blazor/Program.cs` | Registered `LiveLogBuffer` singleton; wired `DashboardLogSink` into Serilog pipeline; added `/api/dashboard/logs` and `/api/dashboard/timeline` REST endpoints |
| `WebUI.Blazor/Managers/DashboardPublisherImpl.cs` | Replaced anonymous object with typed `AgentStatusUpdate` DTO; uses `DashboardHubEvents.SnapshotUpdated`; added `SetCurrentGoal`/`SetConsecutiveFailures` for goal metadata wiring |
| `WebUI.Blazor/wwwroot/index.html` | Added tabbed navigation (Overview, Logs, Timeline); Logs view with level/source filtering and auto-polling; Timeline view with merged journal + log feed and severity colors |

### TSK-0113: ActionQueue Lock Protection — `HEAD`

**File:** `Agent.Core/Models/ActionQueue.cs`

Replaced `ConcurrentQueue<ActionData>` with plain `Queue<ActionData>` wrapped entirely in a single lock. Previously, `Enqueue`, `Dequeue`, `Peek`, `Clear`, `Count`, and `IsEmpty` were lock-free while `EnqueueAll`, `ClearAndEnqueue`, and `ClearAndEnqueueAsync` held a lock. This meant `ClearAndEnqueue`'s documented "atomic clear+enqueue" guarantee was weaker than callers assumed — a concurrent `Enqueue` or `Clear` could observe stale state between the clear and enqueue steps.

**Fix:** All operations now use the same `_lock`, making every read and write consistently ordered.

### TSK-0112: WebSocket Clean Shutdown — `HEAD`

**File:** `Agent.World.Minecraft/WebSocketBridge.cs`

The `RunReceiveLoopWithRetryAsync` method only called `_inbound.Writer.TryComplete()` on retry exhaustion. Normal WebSocket close and cancellation paths returned without completing the inbound channel, leaving readers suspended.

**Fix:** Wrapped the entire retry loop in a `try`/`finally` block. The `finally` completes the inbound channel on ALL terminal paths (normal close, cancellation, error, retry exhaustion).

---

## Remaining Deferred Tasks

| Task | Title | Priority | Reason Deferred |
|:---|---|:---:|---|
| TSK-0107 | Build Origin Sentinel — eliminate (0,0,0) overload | High | Requires invasive refactor of `HtnTaskLibrary.DecomposeBuild` signature (tried, reverted — the method body is large and the edit tools in this environment have issues with large replacements). Best done via direct file write. |
| TSK-0110 | Structured Tool Outcomes — preserve ActionOutcome semantics | Med | Requires enriching `ToolResult` with a status enum, which cascades through the tool execution pipeline. |

---

## Build & Tests

```
Build succeeded. 0 Error(s)
Passed!  - Failed: 0, Passed: 722, Skipped: 0, Total: 722
```

---

## Notes

- TSK-0107 was attempted but reverted after the file editing tools corrupt the large `DecomposeBuild` method body. The change requires:
  1. Changing `HtnTaskLibrary.DecomposeBuild` signature from `(..., int originX, int originY, int originZ, ...)` to `(..., BuildOrigin? origin, ...)`
  2. Updating `HtnPlanner.cs` and `BuildGoalDecomposer.cs` callers
  3. Updating ~30 test calls across 3 test files
  It's feasible but needs direct file writes rather than the agent's edit tools.
