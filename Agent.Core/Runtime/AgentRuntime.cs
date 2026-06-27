namespace Agent.Core.Runtime;

/// <summary>
/// Sprint 36 P2-A: Value-object that groups the six runtime manager components.
///
/// Sprint 37 integration target:
///   AgentBackgroundService becomes:
///
///     protected override async Task ExecuteAsync(CancellationToken ct)
///     {
///         await worldAdapter.ConnectAsync(ct);
///         while (!ct.IsCancellationRequested)
///             await runtime.TickAsync(ct);
///     }
///
///   where runtime.TickAsync:
///     1. ProcessEventsAsync → IStateManager.Apply + IIntentManager.ProcessChatAsync
///     2. DispatchActionsAsync → IPlanningManager.PlanAsync + IExecutionManager.DispatchAsync
///     3. After each ActionOutcome: LLM evaluates (Sprint 36 P2-B observation loop)
///     4. Recovery: IRecoveryManager.TryRecoverAsync on error events
///     5. Dashboard: IDashboardPublisher.PublishStatusAsync after state change
///
/// All six components are required — use NullXxx implementations for tests that
/// don't need a specific manager.
///
/// Sprint 36 NOTE: this record is a definition target only. AgentBackgroundService
/// is NOT refactored yet (Sprint 37). Adding the record now locks the interface
/// contract so PR diffs can validate the decomposition before wiring.
/// </summary>
public sealed record AgentRuntime(
    IIntentManager    IntentManager,
    IPlanningManager  PlanningManager,
    IExecutionManager ExecutionManager,
    IRecoveryManager  RecoveryManager,
    IStateManager     StateManager,
    IDashboardPublisher DashboardPublisher);
