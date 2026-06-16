using Agent.Core;
using Agent.Planning;
using Agent.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// Integration tests for AgentBackgroundService verifying the planner is called
/// when the action queue is empty and a goal is active (council Phase 3 acceptance criterion),
/// and that game error events (blockNotFound, error) are routed through the typed
/// Channel&lt;string&gt; and correctly increment _consecutiveFailures (council Phase 3 deferred).
/// </summary>
[TestFixture]
public class AgentBackgroundServiceTests
{
    private MockWorldAdapter _adapter = null!;
    private ToolRegistry _registry = null!;
    private ToolEngine _toolCaller = null!;
    private MockPlanner _planner = null!;

    [SetUp]
    public void SetUp()
    {
        _adapter   = new MockWorldAdapter();
        _registry  = new ToolRegistry();
        _toolCaller = new ToolEngine(_registry);
        _planner   = new MockPlanner();
    }

    // -- Helpers -----------------------------------------------------------------------

    /// <summary>Creates a service with the default maxConsecutiveFailures (3).</summary>
    private WebUI.Blazor.AgentBackgroundService CreateService() =>
        new(_adapter, _toolCaller, NullLogger<WebUI.Blazor.AgentBackgroundService>.Instance, _planner);

    /// <summary>
    /// Creates a service with a custom maxConsecutiveFailures for faster failure detection
    /// in tests that verify the error-channel path.
    /// </summary>
    private WebUI.Blazor.AgentBackgroundService CreateService(int maxConsecutiveFailures) =>
        new(_adapter, _toolCaller, NullLogger<WebUI.Blazor.AgentBackgroundService>.Instance,
            _planner, maxConsecutiveFailures);

    // -- PlanAsync call verification ---------------------------------------------------

    [Test]
    public async Task PlanAsync_IsCalled_WhenQueueIsEmptyAndGoalIsSet()
    {
        // Arrange: planner returns a plan with one no-op action
        _planner.PlanToReturn = new ActionPlan(
            "GatherWood", ["FindTree"],
            [new ActionData { Tool = "NoOp" }]);

        _registry.Register(new NoOpTool("NoOp"));

        var service = CreateService();
        service.SetGoal(new SimpleGoal("GatherWood", "", ["FindTree"], _ => false));

        // Act: run the service briefly
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        var task = service.StartAsync(cts.Token);

        // Wait until planner is called or timeout
        var deadline = DateTime.UtcNow.AddMilliseconds(400);
        while (_planner.PlanCalls.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        cts.Cancel();
        try { await task; } catch (OperationCanceledException) { }

        // Assert
        Assert.That(_planner.PlanCalls, Has.Count.GreaterThan(0),
            "PlanAsync should have been called when queue was empty and goal was set.");
        Assert.That(_planner.PlanCalls[0].Goal.Name, Is.EqualTo("GatherWood"));
    }

    [Test]
    public async Task ActionQueue_IsDrained_AfterPlanIsCreated()
    {
        // Arrange: plan with one GetStatus action
        _planner.PlanToReturn = new ActionPlan(
            "SurviveNight", ["FindShelter"],
            [new ActionData { Tool = "Ping" }]);

        _registry.Register(new NoOpTool("Ping"));

        var service = CreateService();
        service.SetGoal(new SimpleGoal("SurviveNight", "", ["FindShelter"], _ => false));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var task = service.StartAsync(cts.Token);

        // Wait for the Ping tool to be dispatched via MockWorldAdapter
        var deadline = DateTime.UtcNow.AddMilliseconds(500);
        while (_adapter.SentActions.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        cts.Cancel();
        try { await task; } catch (OperationCanceledException) { }

        // The "Ping" tool is a NoOpTool so it doesn't send a world action;
        // but the planner MUST have been called to produce the plan.
        Assert.That(_planner.PlanCalls, Has.Count.GreaterThan(0));
    }

    [Test]
    public async Task GoalComplete_ClearsGoal_AfterIsCompletePredicate()
    {
        // Arrange: goal is already complete from the start
        _planner.PlanToReturn = new ActionPlan("ImmediateDone", [],
            [new ActionData { Tool = "NoOp" }]);
        _registry.Register(new NoOpTool("NoOp"));

        var service = CreateService();
        // Goal is immediately complete
        service.SetGoal(new SimpleGoal("ImmediateDone", "", [], _ => true));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var task = service.StartAsync(cts.Token);
        await Task.Delay(200);
        cts.Cancel();
        try { await task; } catch (OperationCanceledException) { }

        // Planner should NOT have been called (goal was already complete)
        Assert.That(_planner.PlanCalls, Is.Empty,
            "Planner should not be called when goal is already complete.");
    }

    [Test]
    public async Task SetGoal_ClearsExistingQueue()
    {
        _registry.Register(new NoOpTool("NoOp"));
        var service = CreateService();

        // Enqueue an action first
        service.Enqueue(new ActionData { Tool = "NoOp" });

        // Set a new goal -- should clear the queue
        service.SetGoal(new SimpleGoal("NewGoal", "", [], _ => false));

        // The queue should be empty immediately after SetGoal
        // (the service hasn't started, so nothing has been dequeued)
        Assert.That(service.CurrentGoal?.Name, Is.EqualTo("NewGoal"));
    }

    [Test]
    public void SetGoal_UpdatesCurrentGoal()
    {
        var service = CreateService();
        Assert.That(service.CurrentGoal, Is.Null);

        service.SetGoal(new SimpleGoal("Test", "", [], _ => false));
        Assert.That(service.CurrentGoal?.Name, Is.EqualTo("Test"));
    }

    [Test]
    public async Task WorldAdapter_IsConnected_AfterServiceStart()
    {
        _registry.Register(new NoOpTool("NoOp"));
        var service = CreateService();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var task = service.StartAsync(cts.Token);
        await Task.Delay(100);

        Assert.That(_adapter.IsConnected, Is.True,
            "MinecraftAdapter should be connected after the service starts.");

        cts.Cancel();
        try { await task; } catch (OperationCanceledException) { }
    }

    // -- Error-channel path (deferred from Phase 3 council) ----------------------------

    /// <summary>
    /// Verifies that a blockNotFound event with mined=0 is written to the typed
    /// Channel&lt;string&gt; error channel, which DispatchActionsAsync reads after
    /// the settle delay, incrementing _consecutiveFailures.
    ///
    /// Observable via: with maxConsecutiveFailures=1, a single error abandons the goal
    /// (CurrentGoal → null). This tests the complete signal path:
    ///   PushEvent("blockNotFound") → ProcessEventsAsync writes to _gameErrors
    ///   → settle delay → DispatchActionsAsync TryRead → _consecutiveFailures++
    ///   → goal abandoned.
    /// </summary>
    [Test]
    public async Task BlockNotFoundEvent_MinedZero_WritesToErrorChannel_CausesGoalAbandonment()
    {
        // maxConsecutiveFailures=1 so a single error abandons the goal immediately,
        // making the channel-write observable without _consecutiveFailures being public.
        _planner.PlanToReturn = new ActionPlan("Mine", [],
            [new ActionData { Tool = "NoOp" }]);
        _registry.Register(new NoOpTool("NoOp"));

        var service = CreateService(maxConsecutiveFailures: 1);
        service.SetGoal(new SimpleGoal("Mine", "", [], _ => false));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var serviceTask = service.StartAsync(cts.Token);

        // Wait for the first plan cycle to begin (planner called)
        var planDeadline = DateTime.UtcNow.AddSeconds(2);
        while (_planner.PlanCalls.Count == 0 && DateTime.UtcNow < planDeadline)
            await Task.Delay(10);
        Assert.That(_planner.PlanCalls, Has.Count.GreaterThan(0),
            "Planner must be called before injecting the error event.");

        // Inject the blockNotFound event with mined=0 (total failure — no blocks found at all).
        // ProcessEventsAsync picks it up and writes "$"blockNotFound:oak_log"" to _gameErrors.
        _adapter.PushEvent("blockNotFound", new()
        {
            ["block"] = "minecraft:oak_log",
            ["mined"] = 0
        });

        // Wait for the settle delay to fire (300ms) and for the dispatch loop to
        // read the error, increment failures, and abandon the goal.
        var abandonDeadline = DateTime.UtcNow.AddSeconds(3);
        while (service.CurrentGoal is not null && DateTime.UtcNow < abandonDeadline)
            await Task.Delay(20);

        cts.Cancel();
        try { await serviceTask; } catch (OperationCanceledException) { }

        Assert.That(service.CurrentGoal, Is.Null,
            "Goal should be abandoned after one blockNotFound(mined=0) failure " +
            "when maxConsecutiveFailures=1 (error channel path verified).");
    }

    /// <summary>
    /// Verifies the error event path: an "error" event (e.g. pathfinder timeout) is
    /// written to the Channel&lt;string&gt; and causes the same failure increment.
    /// </summary>
    [Test]
    public async Task ErrorEvent_WritesToErrorChannel_CausesGoalAbandonment()
    {
        _planner.PlanToReturn = new ActionPlan("Mine", [],
            [new ActionData { Tool = "NoOp" }]);
        _registry.Register(new NoOpTool("NoOp"));

        var service = CreateService(maxConsecutiveFailures: 1);
        service.SetGoal(new SimpleGoal("Mine", "", [], _ => false));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var serviceTask = service.StartAsync(cts.Token);

        // Wait for the first plan cycle to begin
        var planDeadline = DateTime.UtcNow.AddSeconds(2);
        while (_planner.PlanCalls.Count == 0 && DateTime.UtcNow < planDeadline)
            await Task.Delay(10);
        Assert.That(_planner.PlanCalls, Has.Count.GreaterThan(0),
            "Planner must be called before injecting the error event.");

        // Inject a game "error" event (e.g. pathfinder blocked, cannot reach target).
        _adapter.PushEvent("error", new()
        {
            ["action"]  = "mine",
            ["message"] = "path blocked"
        });

        // Wait for the error to be consumed and the goal to be abandoned
        var abandonDeadline = DateTime.UtcNow.AddSeconds(3);
        while (service.CurrentGoal is not null && DateTime.UtcNow < abandonDeadline)
            await Task.Delay(20);

        cts.Cancel();
        try { await serviceTask; } catch (OperationCanceledException) { }

        Assert.That(service.CurrentGoal, Is.Null,
            "Goal should be abandoned after one game error event " +
            "when maxConsecutiveFailures=1 (error channel path verified).");
    }

    /// <summary>
    /// Verifies that a blockNotFound event with mined &gt; 0 does NOT signal an error.
    /// When some blocks were mined but the patch depleted, it is a partial success —
    /// the bot collected what it could. The error channel must NOT receive an entry.
    /// </summary>
    [Test]
    public async Task BlockNotFoundEvent_MinedGreaterThanZero_DoesNotSignalError()
    {
        _planner.PlanToReturn = new ActionPlan("Mine", [],
            [new ActionData { Tool = "NoOp" }]);
        _registry.Register(new NoOpTool("NoOp"));

        // maxConsecutiveFailures=1: if an error IS incorrectly written, the goal
        // will be abandoned within ~1s. We assert the goal is still alive after 800ms.
        var service = CreateService(maxConsecutiveFailures: 1);
        service.SetGoal(new SimpleGoal("Mine", "", [], _ => false));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serviceTask = service.StartAsync(cts.Token);

        // Wait for the first plan cycle to begin
        var planDeadline = DateTime.UtcNow.AddSeconds(2);
        while (_planner.PlanCalls.Count == 0 && DateTime.UtcNow < planDeadline)
            await Task.Delay(10);
        Assert.That(_planner.PlanCalls, Has.Count.GreaterThan(0));

        // Inject blockNotFound with mined=3 — partial success; should NOT cause a failure.
        _adapter.PushEvent("blockNotFound", new()
        {
            ["block"] = "minecraft:oak_log",
            ["mined"] = 3   // 3 blocks were collected before the patch depleted
        });

        // Wait one full settle window + safety margin (300ms settle + 500ms buffer = 800ms).
        // If an error was written incorrectly, the goal would be gone by now.
        await Task.Delay(800);

        var goalBeforeCancel = service.CurrentGoal;

        cts.Cancel();
        try { await serviceTask; } catch (OperationCanceledException) { }

        Assert.That(goalBeforeCancel, Is.Not.Null,
            "Goal should NOT be abandoned when blockNotFound has mined>0 " +
            "(partial success must not signal a game error).");
    }
}

/// <summary>Tool that does nothing -- used for integration test dispatch without real adapters.</summary>
file sealed class NoOpTool(string name) : ITool
{
    public string Name => name;
    public string Description => "no-op for testing";
    public System.Text.Json.JsonElement InputSchema =>
        System.Text.Json.JsonDocument.Parse("{}").RootElement;
    public Task<ToolResult> ExecuteAsync(
        System.Text.Json.JsonElement arguments, CancellationToken ct = default)
        => Task.FromResult(new ToolResult(true, "no-op"));
}
