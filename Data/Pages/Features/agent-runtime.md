# Agent Runtime

**Feature ID:** F-RUNTIME  
**Status:** Core (Stable)  
**Location:** `WebUI.Blazor/AgentBackgroundService.cs` (~1940 lines)

The agent runtime is the central nervous system of MemorySmith.Agent. It is a `BackgroundService` (IHostedService) that owns the entire agent lifecycle — connecting to Minecraft, running the planning/dispatch/settle loop, processing world events, and handling chat.

## How It Works

The runtime runs **3 concurrent async loops** coordinated via thread-safe data structures:

| Loop | Method | Purpose |
|------|--------|---------|
| Event Processing | `ProcessEventsAsync` | Reads world events from the adapter, applies them to WorldState via the projector, routes them to correlation completion or error handling |
| Action Dispatch | `DispatchActionsAsync` | The core control loop: plans goals, dequeues actions, dispatches them to the world adapter, settles after each action, replans when needed |
| Chat Consumer | `ChatConsumerAsync` | Reads chat messages from a Channel and routes them through the chat interpreter |

### Lifecycle

```
Start → Connect (max 5 retries, exponential backoff)
       → Launch 3 loops
       → [Running: Plan → Dispatch → Settle → Replan]
       → Disconnect/Error → Reconnect (preserves current goal)
       → Max retries exhausted → Stopped
```

## Synchronization

- **Action queue**: `ConcurrentQueue<ActionData>` + lock for bulk operations
- **Correlation tracking**: `ConcurrentDictionary<Guid, PendingAction>` with CAS (`TryUpdate`) for thread-safe state transitions
- **Chat channel**: Single-reader/single-writer `Channel<WorldEvent>`
- **Game errors**: Single-writer `Channel<string>`
- **Cycle outcomes**: `ConcurrentQueue<ActionOutcome>` for LLM evaluation

## Related

- [Action Dispatch & Correlation](../memories/Core/agent-action-correlation.json)
- [Recovery System](../memories/Core/agent-recovery-system.json)
- [Emergency Stop](emergency-stop.md)
- [Architecture Overview](../architecture.md)
