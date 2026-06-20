namespace MemorySmith.Agent.Tests;

using Agent.Core;
using Agent.Planning;
using Agent.Tools;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Factory for creating minimal <see cref="WebUI.Blazor.AgentBackgroundService"/> instances in tests.
///
/// Sprint 27 P0-A: closes BLK-1 from the Sprint 26 council review.
/// The constructor parameters are derived from the ACTUAL current
/// <see cref="WebUI.Blazor.AgentBackgroundService"/> constructor — not from a stale
/// handoff template — to avoid parameter-mismatch compile errors in CI.
///
/// Current constructor signature (as of Sprint 27):
/// <c>AgentBackgroundService(
///     IWorldAdapter worldAdapter,
///     IToolCaller   toolCaller,
///     ILogger&lt;AgentBackgroundService&gt; logger,
///     IPlanner      planner,
///     IHubContext&lt;AgentHub&gt;? hubContext = null,
///     GoalFactory?  goalFactory = null,
///     IChatInterpreter? chatInterpreter = null,
///     string botName = "AgentBot",
///     int maxConsecutiveFailures = 3,
///     IAgentJournal? journal = null,
///     TimeSpan[]? reconnectDelays = null,
///     IReplanGovernor? replanGovernor = null,
///     ITimeProvider? timeProvider = null)</c>
/// </summary>
public static class AgentBackgroundServiceTestHelper
{
    /// <summary>
    /// Creates a minimal <see cref="WebUI.Blazor.AgentBackgroundService"/> suitable for
    /// integration tests. All optional parameters use safe no-op defaults so tests that
    /// only need the event-processing loop do not need to configure planning, chat, or SignalR.
    /// </summary>
    /// <param name="adapter">The mock world adapter supplying events and capturing sent actions.</param>
    /// <param name="journal">The journal implementation (use <see cref="NullAgentJournal.Instance"/> for no-op).</param>
    /// <param name="timeProvider">
    /// Optional time provider. Pass a <see cref="FakeTimeProvider"/> to control cooldown timing
    /// deterministically. Defaults to <see cref="SystemTimeProvider.Instance"/>.
    /// </param>
    public static WebUI.Blazor.AgentBackgroundService BuildMinimal(
        MockWorldAdapter adapter,
        IAgentJournal journal,
        ITimeProvider? timeProvider = null)
        => new(
            adapter,
            new ToolDispatcher(),
            NullLogger<WebUI.Blazor.AgentBackgroundService>.Instance,
            new MinimalNullPlanner(),
            journal: journal,
            timeProvider: timeProvider);
}

/// <summary>
/// <see cref="IPlanner"/> that always returns an empty plan.
/// Used by <see cref="AgentBackgroundServiceTestHelper"/> for tests that exercise
/// event processing (damage interrupt, health gate, etc.) rather than planning.
/// </summary>
file sealed class MinimalNullPlanner : IPlanner
{
    public Task<IPlan> PlanAsync(IGoal goal, WorldState state,
        CancellationToken ct = default)
        => Task.FromResult<IPlan>(new ActionPlan(goal.Name, goal.Phases.ToArray(), []));

    public Task<IPlan?> ReplanAsync(IPlan currentPlan, WorldState state,
        string failureReason, CancellationToken ct = default, IGoal? originalGoal = null)
        => Task.FromResult<IPlan?>(null);
}
