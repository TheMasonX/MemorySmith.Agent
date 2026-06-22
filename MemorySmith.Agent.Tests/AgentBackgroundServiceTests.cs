using Agent.Construction;
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
///
/// Sprint 1 additions:
///   1a — SlowChatInterpreter_DoesNotBlock_BlockMinedEvent
///   1b — Reconnect_AfterTwoFailures_ResumesCurrentGoal
///
/// Sprint 36 BLK-S36-03 test fixes:
///   - Chat registered as NoOp in SetUp to eliminate the 300ms settle cycle that preceded
///     the first PlanAsync call, making error-channel tests with maxConsecutiveFailures=1 robust.
///   - SlowChatInterpreter test updated: Sprint 35 P0-A removed inventory updates from
///     BlockMinedEvent; push ItemCollectedEvent instead so the inventory check still works.
///   - Error-channel test deadlines raised from 2s to 4s for CI reliability.
/// </summary>
[TestFixture]
public class AgentBackgroundServiceTests
{
    private MockWorldAdapter _adapter = null!;
    private ToolDispatcher _dispatcher = null!;
    private MockPlanner _planner = null!;

    [SetUp]
    public void SetUp()
    {
        _adapter    = new MockWorldAdapter();
        _dispatcher = new ToolDispatcher();
        _planner    = new MockPlanner();
        // Sprint 36 BLK-S36-03: Register Chat as a no-op so the startup
        // announcement message dispatches synchronously without a failure/settle
        // cycle. Without this, the 300ms settle before the first PlanAsync call
        // made the 2s deadline in error-channel tests unreliable under CI load.
        _dispatcher.Register(new NoOpTool("Chat"));
    }

    // -- Helpers -----------------------------------------------------------------------

    /// <summary>Creates a service with the default maxConsecutiveFailures (3).</summary>
    private WebUI.Blazor.AgentBackgroundService CreateService() =>
        new(_adapter, _dispatcher, NullLogger<WebUI.Blazor.AgentBackgroundService>.Instance, _planner);

    /// <summary>
    /// Creates a service with a custom maxConsecutiveFailures for faster failure detection
    /// in tests that verify the error-channel path.
    /// </summary>
    private WebUI.Blazor.AgentBackgroundService CreateService(int maxConsecutiveFailures) =>
        new(_adapter, _dispatcher, NullLogger<WebUI.Blazor.AgentBackgroundService>.Instance,
            _planner, maxConsecutiveFailures: maxConsecutiveFailures);

    // -- PlanAsync call verification ---------------------------------------------------

    [Test]
    public async Task PlanAsync_IsCalled_WhenQueueIsEmptyAndGoalIsSet()
    {
        // Arrange: planner returns a plan with one no-op action
        _planner.PlanToReturn = new ActionPlan(
            "GatherWood", ["FindTree"],
            [new ActionData { Tool = "NoOp" }]);

        _dispatcher.Register(new NoOpTool("NoOp"));

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

        _dispatcher.Register(new NoOpTool("Ping"));

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
    public async Task PlanCreation_LogsTaskRelevantInventoryForBuildGoal()
    {
        var logger = new TestLogger<WebUI.Blazor.AgentBackgroundService>();
        _planner.PlanToReturn = new ActionPlan(
            "Build:small-house",
            ["GatherMaterials", "Build", "Verify"],
            [new ActionData { Tool = "NoOp" }]);
        _dispatcher.Register(new NoOpTool("NoOp"));

        var service = new WebUI.Blazor.AgentBackgroundService(
            _adapter,
            _dispatcher,
            logger,
            _planner);

        var blueprint = new Blueprint
        {
            Id = "small-house",
            Name = "Small House",
            Materials = [new MaterialEntry("cobblestone", 2), new MaterialEntry("oak_planks", 1)]
        };

        service.SetGoal(new BuildGoal(blueprint, []));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1000));
        var task = service.StartAsync(cts.Token);
        await Task.Delay(600);
        cts.Cancel();
        try { await task; } catch (OperationCanceledException) { }

        Assert.That(
            logger.Entries.Any(e => e.Message.Contains("[plan]")
                && e.Message.Contains("cobblestone: 0/2")
                && e.Message.Contains("oak_planks: 0/1")),
            Is.True,
            "Plan logging should report task-relevant inventory status for build goals.");
    }

    [Test]
    public async Task GoalComplete_ClearsGoal_AfterIsCompletePredicate()
    {
        // Arrange: goal is already complete from the start
        _planner.PlanToReturn = new ActionPlan("ImmediateDone", [],
            [new ActionData { Tool = "NoOp" }]);
        _dispatcher.Register(new NoOpTool("NoOp"));

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
        _dispatcher.Register(new NoOpTool("NoOp"));
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
        _dispatcher.Register(new NoOpTool("NoOp"));
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

    [Test]
    public async Task BlockNotFoundEvent_MinedZero_WritesToErrorChannel_CausesGoalAbandonment()
    {
        _planner.PlanToReturn = new ActionPlan("Mine", [],
            [new ActionData { Tool = "NoOp" }]);
        _dispatcher.Register(new NoOpTool("NoOp"));

        var service = CreateService(maxConsecutiveFailures: 1);
        service.SetGoal(new SimpleGoal("Mine", "", [], _ => false));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var serviceTask = service.StartAsync(cts.Token);

        // Sprint 36 BLK-S36-03: raised from 2s to 4s — Chat NoOp registration (SetUp)
        // eliminates the 300ms settle cycle, but extra headroom guards against CI load.
        var planDeadline = DateTime.UtcNow.AddSeconds(4);
        while (_planner.PlanCalls.Count == 0 && DateTime.UtcNow < planDeadline)
            await Task.Delay(10);
        Assert.That(_planner.PlanCalls, Has.Count.GreaterThan(0),
            "Planner must be called before injecting the error event.");

        _adapter.PushEvent(new BlockNotFoundEvent("minecraft:oak_log", 0, DateTimeOffset.UtcNow));

        var abandonDeadline = DateTime.UtcNow.AddSeconds(3);
        while (service.CurrentGoal is not null && DateTime.UtcNow < abandonDeadline)
            await Task.Delay(20);

        cts.Cancel();
        try { await serviceTask; } catch (OperationCanceledException) { }

        Assert.That(service.CurrentGoal, Is.Null,
            "Goal should be abandoned after one blockNotFound(mined=0) failure " +
            "when maxConsecutiveFailures=1 (error channel path verified).");
    }

    [Test]
    public async Task ErrorEvent_WritesToErrorChannel_CausesGoalAbandonment()
    {
        _planner.PlanToReturn = new ActionPlan("Mine", [],
            [new ActionData { Tool = "NoOp" }]);
        _dispatcher.Register(new NoOpTool("NoOp"));

        var service = CreateService(maxConsecutiveFailures: 1);
        service.SetGoal(new SimpleGoal("Mine", "", [], _ => false));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var serviceTask = service.StartAsync(cts.Token);

        // Sprint 36 BLK-S36-03: raised from 2s to 4s for CI reliability.
        var planDeadline = DateTime.UtcNow.AddSeconds(4);
        while (_planner.PlanCalls.Count == 0 && DateTime.UtcNow < planDeadline)
            await Task.Delay(10);
        Assert.That(_planner.PlanCalls, Has.Count.GreaterThan(0),
            "Planner must be called before injecting the error event.");

        _adapter.PushEvent(new ErrorEvent("mine", "path blocked", DateTimeOffset.UtcNow));

        var abandonDeadline = DateTime.UtcNow.AddSeconds(3);
        while (service.CurrentGoal is not null && DateTime.UtcNow < abandonDeadline)
            await Task.Delay(20);

        cts.Cancel();
        try { await serviceTask; } catch (OperationCanceledException) { }

        Assert.That(service.CurrentGoal, Is.Null,
            "Goal should be abandoned after one game error event " +
            "when maxConsecutiveFailures=1 (error channel path verified).");
    }

    [Test]
    public async Task BlockNotFoundEvent_MinedGreaterThanZero_DoesNotSignalError()
    {
        _planner.PlanToReturn = new ActionPlan("Mine", [],
            [new ActionData { Tool = "NoOp" }]);
        _dispatcher.Register(new NoOpTool("NoOp"));

        var service = CreateService(maxConsecutiveFailures: 1);
        service.SetGoal(new SimpleGoal("Mine", "", [], _ => false));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serviceTask = service.StartAsync(cts.Token);

        // Sprint 36 BLK-S36-03: raised from 2s to 4s for CI reliability.
        var planDeadline = DateTime.UtcNow.AddSeconds(4);
        while (_planner.PlanCalls.Count == 0 && DateTime.UtcNow < planDeadline)
            await Task.Delay(10);
        Assert.That(_planner.PlanCalls, Has.Count.GreaterThan(0));

        _adapter.PushEvent(new BlockNotFoundEvent("minecraft:oak_log", 3, DateTimeOffset.UtcNow));

        await Task.Delay(800);

        var goalBeforeCancel = service.CurrentGoal;

        cts.Cancel();
        try { await serviceTask; } catch (OperationCanceledException) { }

        Assert.That(goalBeforeCancel, Is.Not.Null,
            "Goal should NOT be abandoned when blockNotFound has mined>0 " +
            "(partial success must not signal a game error).");
    }

    // -- Sprint 1a: non-blocking LLM ---------------------------------------------------

    /// <summary>
    /// Sprint 1a acceptance criterion: a slow LLM (6s mock) does NOT delay processing
    /// of subsequent item-collected events. Without the Channel fix, itemCollected would be
    /// queued behind the 6s LLM await; with it, the event loop returns immediately after
    /// writing the chat event to the channel.
    ///
    /// Sprint 36 BLK-S36-03: Updated from BlockMinedEvent to ItemCollectedEvent.
    /// Sprint 35 P0-A removed inventory updates from BlockMinedEvent — inventory truth now
    /// comes exclusively from ItemCollectedEvent (Mineflayer playerCollect). The underlying
    /// behaviour being tested (LLM channel non-blocking) is unchanged.
    /// </summary>
    [Test]
    public async Task SlowChatInterpreter_DoesNotBlock_BlockMinedEventProcessing()
    {
        var slowInterp = new SlowChatInterpreter(TimeSpan.FromSeconds(6));

        var service = new WebUI.Blazor.AgentBackgroundService(
            _adapter, _dispatcher,
            NullLogger<WebUI.Blazor.AgentBackgroundService>.Instance,
            _planner,
            chatInterpreter: slowInterp,
            reconnectDelays: []); // no reconnect retries needed for this test

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serviceTask = service.StartAsync(cts.Token);

        // Push a chat event first — goes to _chatChannel; ChatConsumerAsync will await 6s
        _adapter.PushEvent(new ChatEvent("Player1", "hello", 1, null, DateTimeOffset.UtcNow));

        // Sprint 36 BLK-S36-03: push ItemCollectedEvent instead of BlockMinedEvent.
        // Sprint 35 P0-A: ApplyBlockMined no longer updates inventory; only
        // ItemCollectedEvent (via playerCollect) provides the authoritative inventory update.
        _adapter.PushEvent(new ItemCollectedEvent("oak_log", 1, DateTimeOffset.UtcNow));

        // Assert: itemCollected updates inventory within 2s (well within the 6s LLM window)
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (service.WorldState.Inventory.GetValueOrDefault("oak_log") == 0
               && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        cts.Cancel();
        try { await serviceTask; } catch (OperationCanceledException) { }

        Assert.That(service.WorldState.Inventory.GetValueOrDefault("oak_log"), Is.GreaterThan(0),
            "itemCollected inventory update should complete within 2s even when LLM takes 6s " +
            "(Sprint 1a: non-blocking chat channel verified).");
    }

    // -- Sprint 1b: reconnect with exponential backoff ---------------------------------

    /// <summary>
    /// Sprint 1b acceptance criterion: AgentBackgroundService retries after two
    /// ConnectAsync failures and successfully connects on the third attempt.
    /// The current goal survives the reconnect loop unchanged.
    /// </summary>
    [Test]
    public async Task Reconnect_AfterTwoFailures_ResumesCurrentGoal()
    {
        // Adapter that fails the first two ConnectAsync calls, succeeds on the third
        var failingAdapter = new FailingWorldAdapter(failCount: 2);

        _planner.PlanToReturn = new ActionPlan("Mine", [],
            [new ActionData { Tool = "NoOp" }]);
        _dispatcher.Register(new NoOpTool("NoOp"));

        var service = new WebUI.Blazor.AgentBackgroundService(
            failingAdapter, _dispatcher,
            NullLogger<WebUI.Blazor.AgentBackgroundService>.Instance,
            _planner,
            reconnectDelays: [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero]); // instant retries

        service.SetGoal(new SimpleGoal("Mine", "", [], _ => false));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serviceTask = service.StartAsync(cts.Token);

        // Wait until the adapter successfully connects (3rd attempt)
        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (!failingAdapter.IsConnected && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        cts.Cancel();
        try { await serviceTask; } catch (OperationCanceledException) { }

        Assert.That(failingAdapter.ConnectAttempts, Is.EqualTo(3),
            "Should have attempted 3 connections (2 failures + 1 success).");
        Assert.That(service.CurrentGoal?.Name, Is.EqualTo("Mine"),
            "Goal should persist unchanged through reconnect attempts (Sprint 1b verified).");
    }
}

// ── Test helpers ─────────────────────────────────────────────────────────────────────────────

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

/// <summary>
/// Sprint 1a: IChatInterpreter that sleeps for a configurable duration before returning
/// NotAddressed. Simulates a slow LLM to verify the chat channel doesn't block the event loop.
/// </summary>
file sealed class SlowChatInterpreter(TimeSpan delay) : IChatInterpreter
{
    public async Task<ChatInterpretation> InterpretAsync(
        string username, string message, string botName,
        int onlinePlayers, Position botPosition, Position? playerPosition,
        WorldState state, CancellationToken ct = default)
    {
        await Task.Delay(delay, ct);
        return new ChatInterpretation(ChatIntentType.NotAddressed);
    }

    public void RecordBotSpoke() { }
}

/// <summary>
/// Sprint 1b: IWorldAdapter that throws on the first N ConnectAsync calls, then delegates
/// to an inner MockWorldAdapter. Verifies the reconnect retry loop.
/// </summary>
file sealed class FailingWorldAdapter : IWorldAdapter
{
    private int _connectAttempts;
    private readonly int _failCount;
    private readonly MockWorldAdapter _inner = new();

    public FailingWorldAdapter(int failCount) => _failCount = failCount;

    public bool IsConnected => _connectAttempts > _failCount && _inner.IsConnected;
    public int ConnectAttempts => _connectAttempts;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (++_connectAttempts <= _failCount)
            throw new System.IO.IOException($"Simulated connection failure {_connectAttempts}");
        return _inner.ConnectAsync(cancellationToken);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
        => _inner.DisconnectAsync(cancellationToken);

    public Task SendActionAsync(ActionData action, CancellationToken cancellationToken = default)
        => _inner.SendActionAsync(action, cancellationToken);

    public IAsyncEnumerable<WorldEvent> ReceiveEventsAsync(CancellationToken cancellationToken = default)
        => _inner.ReceiveEventsAsync(cancellationToken);
}
