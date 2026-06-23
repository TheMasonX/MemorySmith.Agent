# Recovery & Safety Systems

**Feature ID:** F-SAFETY  
**Status:** Core (Stable, Graduated Retry in Sprint 40)  
**Location:** `WebUI.Blazor/AgentBackgroundService.cs`, `Agent.Core/ReplanGovernor.cs`

The agent has multiple recovery and safety systems that run concurrently with the main dispatch loop. These ensure the agent can handle errors, damage, stalls, and timeouts without human intervention.

## Safety Systems Overview

| System | Trigger | Response | Rate Limit |
|--------|---------|----------|------------|
| **Damage Interrupt** | Health drops below threshold (default 6 HP) for 3s | Clear queue, GetStatus, replan | 3s cooldown |
| **Game Error Recovery** | ErrorEvent or BlockNotFoundEvent | GetStatus, replan | Per-goal guard |
| **Stall Detection** | 3 identical plans with no inventory change | 10s delay, auto-recovery after graduated retry [10,20,30,60]s | 30s suppress |
| **Consecutive Failure Abandon** | 5+ consecutive tool failures | Abandon goal, GetStatus, await new goal | Per-goal guard |
| **Timeout Sweep** | Action dispatched >30s without completion | Mark TimedOut via CAS, allow re-dispatch | Every settle cycle |
| **Reconnection Recovery** | WebSocket disconnect / kick | Exponential backoff [2,4,8,16,32]s, max 5 retries | N/A |

## Damage Interrupt Detail

The most sophisticated safety system. When `ProcessEventsAsync` detects health dropping below `DamageInterruptThresholdHp`:

1. Records `_firstLowHealthTime` timestamp
2. If health stays below threshold for `LowHealthDurationMs` (3000ms):
   - Atomically clears the action queue
   - Enqueues `GetStatus` action
   - Sends chat: "I took damage, checking my status..."
3. When goal completes after recovery: sends "Safe now."
4. 3s cooldown prevents thrash during combat

Per-goal `DamageInterruptThresholdHp` override:
- `null` → system default (6 HP)
- `0` → never interrupt (e.g., for goals that involve taking damage)

## Conversation Recovery

The agent may send chat messages explaining recovery actions:
- Damage: "I took damage, checking my status..." → "Safe now."
- Stall: Chat notification with stall duration
- Game error: "Something went wrong, recovering..."

## Related

- [Recovery System Memory](../memories/Core/agent-recovery-system.json)
- [Emergency Stop](emergency-stop.md)
- [Damage Interrupt Guide](../guides/damage-interrupt.md)
- [Replan Governor Guide](../guides/replan-governor.md)
