namespace Agent.Core.Runtime;

using Agent.Core;

/// <summary>
/// Sprint 36 P2-A: Marker interface for the six runtime manager components.
///
/// AgentBackgroundService is currently a ~80KB monolith. Sprint 36 defines the
/// decomposition target; Sprint 37 wires these interfaces to actual implementations
/// and makes AgentBackgroundService delegate to an AgentRuntime record:
///
///   protected override async Task ExecuteAsync(CancellationToken ct)
///   {
///       await runtime.ConnectAsync(ct);
///       while (!ct.IsCancellationRequested)
///           await runtime.TickAsync(ct);
///   }
///
/// Each interface boundary represents one responsibility from the god class.
/// </summary>
public interface IAgentRuntimeComponent { }

/// <summary>
/// Sprint 36 P2-A: Processes incoming chat events and produces an IntentDraft.
///
/// Owns the LLM interpreter pipeline and the transition from IntentDraft to Goal
/// (currently done in AgentBackgroundService.IntentDraftToGoal — moves here in Sprint 36).
///
/// Sprint 35 architectural lock: parsers never create goals. IntentManager maps
/// IntentDraft intent → GoalFactory call; the parser only populates the draft.
/// </summary>
public interface IIntentManager : IAgentRuntimeComponent
{
    /// <summary>Interprets a chat event and returns an IntentDraft, or null when ignored.</summary>
    Task<IntentDraft?> ProcessChatAsync(
        string username, string message, WorldState state, CancellationToken ct = default);
}

/// <summary>
/// Sprint 36 P2-A: Owns the plan lifecycle (initial plan + replan).
///
/// Currently split between AgentBackgroundService.DispatchActionsAsync (calls PlanAsync)
/// and IPlanner (HtnPlanner / PlannerRouter). Sprint 37: PlanningManager wraps IPlanner
/// and owns the replan governor integration.
/// </summary>
public interface IPlanningManager : IAgentRuntimeComponent
{
    /// <summary>Generates an initial or updated plan for the active goal.</summary>
    Task<ActionPlan> PlanAsync(IGoal goal, WorldState state, CancellationToken ct = default);

    /// <summary>Signals that the current plan should be reconsidered on the next tick.</summary>
    void RequestReplan();
}

/// <summary>
/// Sprint 36 P2-A: Dispatches a single action and returns when the tool call completes.
///
/// Currently embedded in AgentBackgroundService.DispatchActionsAsync. Extracting this
/// allows the recovery and observation-driven replanning loops to inject themselves
/// between dispatch and result without the dispatch logic being aware.
/// </summary>
public interface IExecutionManager : IAgentRuntimeComponent
{
    /// <summary>Calls the tool and returns the ActionOutcome.</summary>
    Task<ActionOutcome> DispatchAsync(ActionData action, CancellationToken ct = default);
}

/// <summary>
/// Sprint 36 P2-A: Handles error events and attempts recovery.
///
/// Currently TryRecoverFromGameErrorAsync in AgentBackgroundService. Extracting
/// decouples recovery strategy from the main dispatch loop.
/// </summary>
public interface IRecoveryManager : IAgentRuntimeComponent
{
    /// <summary>
    /// Attempts to recover from a game error. Returns true if recovery succeeded
    /// and the goal should continue; false if the goal should be abandoned.
    /// </summary>
    Task<bool> TryRecoverAsync(string errorMessage, WorldState state, CancellationToken ct = default);
}

/// <summary>
/// Sprint 36 P2-A: Owns WorldState as a read-model and applies events.
///
/// Currently WorldStateProjector (pure function) + WorldState (field in AgentBackgroundService).
/// Extracting creates a clear state-ownership boundary and enables the observation-driven
/// replanning loop (Sprint 36 P2-B) to read state after each event.
/// </summary>
public interface IStateManager : IAgentRuntimeComponent
{
    /// <summary>The current confirmed world state.</summary>
    WorldState Current { get; }

    /// <summary>Applies a world event and updates Current.</summary>
    void Apply(WorldEvent ev);
}

/// <summary>
/// Sprint 36 P2-A: Publishes agent status to the dashboard (SignalR hub / REST).
///
/// Currently PushStatusToDashboardAsync in AgentBackgroundService. Extracting removes
/// the HubContext dependency from the main agent loop.
/// </summary>
public interface IDashboardPublisher : IAgentRuntimeComponent
{
    /// <summary>Pushes the current agent status to all connected dashboard clients.</summary>
    Task PublishStatusAsync(CancellationToken ct = default);
}
