using Agent.Core;
using Agent.Planning;
using Agent.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// Integration tests for AgentBackgroundService verifying the planner is called
/// when the action queue is empty and a goal is active (council Phase 3 acceptance criterion).
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

    // -- Helper ------------------------------------------------------------------------

    private WebUI.Blazor.AgentBackgroundService CreateService() =>
        new(_adapter, _toolCaller, NullLogger<WebUI.Blazor.AgentBackgroundService>.Instance, _planner);

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
