# Emergency Stop

**Feature ID:** F-ESTOP  
**Status:** Core (Stable)  
**Location:** `WebUI.Blazor/AgentBackgroundService.cs` (~line 1600), `MineflayerAdapter/index.js` (handleStop)

Emergency stop (`StopNow`) bypasses the command queue and clears adapter state immediately. It is the highest-priority control path — used for user-initiated halt or critical error.

## Flow

```
C# StopNow
  → ActionData {action:"stop", correlationId: new Guid()}
  → _queue.ClearAndEnqueueAsync(stopAction)
     → Async stop callback (in-flight action cleanup)
     → lock { clear queue; enqueue stop }
  → CAS-transition all Dispatched correlations → TimedOut
  → WebSocket: {"action":"stop"}
     → Node.js handleStop:
       → _stopRequested = true
       → cmdQueue.length = 0
       → bot.pathfinder.stop()
       → Cancel active dig
       → sendEvent("stopComplete")
  → ProcessEventsAsync logs stop acknowledgment
```

## Guard Pattern

All long-running handlers in the Node.js adapter check `_stopRequested` periodically:
- Before each `bot.dig()` call
- In pathfinder goto callbacks
- Between craft iterations

## Key Difference: Normal vs Emergency Stop

| Aspect | Normal Stop | Emergency Stop |
|--------|-------------|----------------|
| Queue | Drains naturally | Cleared immediately |
| In-flight action | Completes normally | Cancelled mid-execution |
| After stop | Agent idle, connected | Agent idle, connected |
| Use case | Graceful halt | User halt or critical error |

## REST Endpoint

```
POST /api/agent/stop?emergency=true
```

## Related

- [Emergency Stop Memory](../memories/Core/agent-emergency-stop.json)
- [Recovery & Safety Systems](recovery-safety.md)
- [Action Dispatch Correlation](../memories/Core/agent-action-correlation.json)
