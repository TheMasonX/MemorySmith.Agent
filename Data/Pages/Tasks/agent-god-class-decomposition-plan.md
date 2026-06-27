# AgentBackgroundService God Class Decomposition Plan

**File:** `WebUI.Blazor/AgentBackgroundService.cs` — ~1800 lines, ~40+ methods, ~15+ mutable fields
**Risk:** HIGH — single-file monolith makes testing, debugging, and extension fragile

## Extraction Candidates

Each extraction target is an independent bounded context. Order by risk/reward (lowest risk first).

---

### TSK-0100: Extract `ActionCorrelationTracker` (LOW RISK)

**Lines:** ~1430–1550 (correlation helpers) + `_correlatedActions` field
**Methods:** `TransitionCorrelatedAction`, `CompleteCorrelatedActionByTool`, `HasPendingActionOfTool`, `FailCorrelatedActionByTool`, `SweepTimedOutActions`, `IsFireAndForgetTool`
**State:** `ConcurrentDictionary<Guid, PendingAction> _correlatedActions`
**Contract:** Pure state machine — no DI beyond `ILogger`. All methods are `private` → extract to internal class, expose via interface `IActionCorrelationTracker`.

```csharp
public interface IActionCorrelationTracker
{
    void Track(Guid correlationId, string toolName);
    bool CompleteByTool(string toolName);
    bool FailByTool(string toolName);
    bool HasPendingOfTool(string toolName);
    void SweepTimedOut(TimeSpan timeout);
    void Clear();
}
```

**Dependencies:** None (pure collections). Easiest first step.
**Testability:** Instantiate with `ILogger`, call methods, assert state transitions.

---

### TSK-0101: Extract `AgentInventoryFormatter` (LOW RISK)

**Lines:** ~1554–1645 (inventory helpers)
**Methods:** `SummarizeInventory`, `SummarizeTaskRelevantInventory`, `RleCompressActions`
**State:** None (pure static/instance methods on `IGoal` + `WorldState`)
**Contract:** Static utility → sealed class with `IGoal` and `WorldState` params.

```csharp
public sealed class AgentInventoryFormatter
{
    public string Summarize(WorldState state, int maxItems = 5);
    public string SummarizeForGoal(IGoal goal, WorldState state);
    public static string RleCompress(IEnumerable<string> tools);
}
```

**Dependencies:** None. Pure formatting logic.
**Testability:** Pass `WorldState` and `IGoal`, assert string output.

---

### TSK-0102: Extract `AgentDashboardPusher` (LOW RISK)

**Lines:** ~1702–1762 (dashboard methods)
**Methods:** `PushStatusToDashboardAsync`, `PushChatToDashboardAsync`, `PushGoalToDashboardAsync`
**State:** `HttpClient? _httpClient` (injected)
**Contract:** Interface `IAgentDashboardPusher` with 3 async methods.

```csharp
public interface IAgentDashboardPusher
{
    Task PushStatusAsync(string status, CancellationToken ct);
    Task PushChatAsync(string type, string? who, string text, CancellationToken ct);
    Task PushGoalAsync(IGoal? goal, WorldState state, CancellationToken ct);
}
```

**Dependencies:** `HttpClient`, `ILogger`. Independent of agent lifecycle.
**Testability:** Mock `HttpClient` via `IHttpClientFactory`.

---

### TSK-0103: Extract `AgentChatHandler` (MEDIUM RISK)

**Lines:** ~708–870 (chat consumer + handler)
**Methods:** `ChatConsumerAsync`, `HandleChatEventAsync`, `TryCreateGoalFromChatAsync`
**State:** `_chatChannel`, `_currentGoal`, `_worldState`, `_intentManager`, `chatInterpreter`
**Contract:** Background loop reading from `Channel<WorldEvent>`, dispatching to goal factory.

```csharp
public interface IAgentChatHandler
{
    Task StartAsync(CancellationToken ct);
    void EnqueueChat(WorldEvent chatEvent);
}
```

**Dependencies:** `IChatInterpreter`, `IIntentManager`, `IPlanner`, `IAgentJournal`, `ILogger`, `WorldState` (needs snapshot or reference). Must coordinate with `DispatchActionsAsync` on goal transitions.
**Challenge:** Multiple writers to `_currentGoal` — needs synchronization with dispatch loop.
**Testability:** Medium — send chat events, assert goal created/updated.

---

### TSK-0104: Extract `AgentGoalManager` (MEDIUM RISK)

**Lines:** ~189–320 (SetGoal, CancelGoal, SetBuildOrigin, GetPendingActions, TryCompleteCurrentGoalFromWorldUpdate)
**Methods:** `SetGoal`, `CancelGoal`, `SetBuildOrigin`, `GetPendingActions`, `TryCompleteCurrentGoalFromWorldUpdate`
**State:** `_currentGoal`, `_consecutiveFailures`, `_pendingActions`, `_pendingLock`, `_lastAbandonedGoalName`, `_lastRecoveredGoalName`
**Contract:** Single writer, multiple readers for `_currentGoal`. Must expose `CurrentGoal` as property.

```csharp
public interface IAgentGoalManager
{
    IGoal? CurrentGoal { get; }
    int ConsecutiveFailures { get; }
    void SetGoal(IGoal goal);
    void CancelGoal();
    bool TryCompleteFromWorldUpdate(WorldState state);
    IReadOnlyList<ActionData> GetPendingActions();
}
```

**Dependencies:** `IAgentJournal`, `ILogger`. Needs `lock` for `_pendingActions`.
**Testability:** Set goal, assert state transitions. Mock `IGoal.IsComplete`.

---

### TSK-0105: Extract `AgentEventProcessor` (MEDIUM RISK)

**Lines:** ~404–640 (ProcessEventsAsync — the big switch)
**Methods:** `ProcessEventsAsync`, `TryRouteAsError`
**State:** `_worldState`, `_projector`, `_gameErrors`, `_cycleOutcomes`, `_correlatedActions`, `_currentGoal`
**Contract:** Background loop reading `WorldEvent` from the adapter. Projects into `WorldState` and logs outcomes.

```csharp
public interface IAgentEventProcessor
{
    Task ProcessEventsAsync(CancellationToken ct);
    WorldState WorldState { get; }
}
```

**Challenge:** 25+ event types, each with different projection logic. WorldStateProjector is separate but event routing is inline.
**Testability:** Feed `WorldEvent` objects through channel, assert `WorldState` updates.

---

### TSK-0106: Extract `AgentRecoveryHandler` (MEDIUM RISK)

**Lines:** ~1806–1890 (recovery method)
**Methods:** `TryRecoverFromGameErrorAsync`, `GoalNamesMatch`, `TryInterruptOnDamageAsync`
**State:** `_lastRecoveredGoalName`, `_lastAbandonedGoalName`, `_currentGoal`, `chatInterpreter`, `_intentManager`
**Contract:** Called when `_consecutiveFailures >= 2` or immediate recovery events fire.

```csharp
public interface IAgentRecoveryHandler
{
    Task TryRecoverAsync(string errorMessage, CancellationToken ct);
    Task TryInterruptOnDamageAsync(DamageTakenEvent damage, CancellationToken ct);
}
```

**Dependencies:** `IChatInterpreter`, `IIntentManager`, `IAgentJournal`, `ILogger`.
**Testability:** Mock chat interpreter, assert goal created/switched.

---

### TSK-0107: Extract `DispatchActionsAsync` Into `AgentActionDispatcher` (HIGH RISK — Largest Piece)

**Lines:** ~990–1428 (the main dispatch/planning loop — ~440 lines)
**Methods:** `DispatchActionsAsync` (internal mega-function)
**State:** `_queue`, `_currentGoal`, `_worldState`, `_actionDispatchedThisCycle`, `_cycleInventorySnapshot`, `_lastReplanAt`, `_consecutiveFailures`, `_lastFailureReason`, `_lastActionDispatchedAt`, `_lastStallWarnedAt`, `_cycleOutcomes`, `_correlatedActions`, `_pendingActions`
**Contract:** The heart of the agent — plans, dispatches, settles, replans. Tightly coupled to EVERYTHING.

**This must be the LAST extraction** after TSK-0100 through TSK-0106 are complete, because it depends on all of them.

**Recommended approach for TSK-0107:**
1. First extract pure functions: `MapNodeActionToToolName`, `IsNonGoalFailureAction`, `IsFireAndForgetTool`
2. Then extract action lifecycle: `_correlatedActions` → `IActionCorrelationTracker` (TSK-0100)
3. Then extract formatting: inventory/dashboard helpers
4. Then extract sub-loops: chat, events, recovery
5. Finally, `DispatchActionsAsync` becomes a thin orchestrator calling the extracted services

---

## Recommended Execution Order

| Step | Task | Risk | Depends On | Effort |
|------|------|------|------------|--------|
| 1 | TSK-0100: ActionCorrelationTracker | LOW | None | ~1h |
| 2 | TSK-0101: AgentInventoryFormatter | LOW | None | ~30min |
| 3 | TSK-0102: AgentDashboardPusher | LOW | None | ~30min |
| 4 | TSK-0103: AgentChatHandler | MEDIUM | TSK-0104 | ~2h |
| 5 | TSK-0104: AgentGoalManager | MEDIUM | None | ~1h |
| 6 | TSK-0105: AgentEventProcessor | MEDIUM | TSK-0104 | ~2h |
| 7 | TSK-0106: AgentRecoveryHandler | MEDIUM | TSK-0103, TSK-0104 | ~1h |
| 8 | TSK-0107: AgentActionDispatcher | HIGH | ALL above | ~3h |

**Total estimated effort:** ~11h for complete decomposition.

## Risk Mitigation

- Each extraction produces a **zero-functional-change** refactor — no behavior change, just relocation
- Validate with existing test suite after each step (`dotnet test`)
- The `private` → `internal` access modifier change is safe and allows unit testing
- Keep original methods as delegates to extracted class during transition period
- Remove delegate forwarding in a final cleanup pass after all consumers are updated
