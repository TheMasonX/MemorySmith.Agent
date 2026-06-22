namespace MemorySmith.Agent.Tests;

using global::Agent.Core;
using global::Agent.Planning;
using global::Agent.Planning.Goals;
using global::Agent.Tools;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Sprint 36 — AgentRuntime Decomposition + Observation-Driven Replanning.
///
/// Test coverage:
///   P0-A: TryInterruptOnDamageAsync uses ClearAndEnqueueAsync — stop sent before clear
///   P0-C: BuildGoalDecomposer/HtnTaskLibrary SearchedRadius retry gate
///   P1-C: (stub) LlmChatInterpreter BuildSystemPrompt includes registered tool names
/// </summary>
[TestFixture]
public class Sprint36Tests
{
    // ─── P0-A: TryInterruptOnDamageAsync stop-before-clear ───────────────────

    /// <summary>
    /// Sprint 36 P0-A: ClearAndEnqueueAsync sends the stop action BEFORE clearing
    /// the queue. Verify that "StopNow" appears in MockWorldAdapter.SentActions when
    /// a damage interrupt fires.
    ///
    /// Regression coverage: previously SendEmergencyStop() was fire-and-forget (no
    /// await), so new actions could be dispatched before JS received the stop signal.
    /// With ClearAndEnqueueAsync, the stop callback is awaited before the atomic
    /// clear+enqueue, guaranteeing correct stop-first ordering.
    /// </summary>
    [Test]
    public async Task TryInterruptOnDamageAsync_StopSentBeforeClear_StopNowInSentActions()
    {
        var adapter = new MockWorldAdapter();
        var journal = NullAgentJournal.Instance;
        var service = AgentBackgroundServiceTestHelper.BuildMinimal(adapter, journal);

        await service.StartAsync(CancellationToken.None);
        try
        {
            service.SetGoal(new SimpleGoal("Mine", "Mine blocks", ["mine"], _ => false));

            // First HealthEvent establishes the baseline (_previousHealth)
            adapter.PushEvent(new HealthEvent(20, 20, DateTimeOffset.UtcNow));

            // Large drop: 20 → 10 HP (|delta|=10 ≥ system threshold of 6 HP)
            await Task.Delay(30); // let first event be processed
            adapter.PushEvent(new HealthEvent(10, 20, DateTimeOffset.UtcNow));

            // Allow event loop to process the second event and fire interrupt
            await Task.Delay(200);

            var stopSent = adapter.SentActions.Any(a =>
                a.Tool.Equals("StopNow", StringComparison.OrdinalIgnoreCase));

            Assert.That(stopSent, Is.True,
                "TryInterruptOnDamageAsync must call ClearAndEnqueueAsync with a stop callback " +
                "that fires SendActionAsync('StopNow') before clearing the queue (Sprint 36 P0-A).");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task TryInterruptOnDamageAsync_SmallDrop_NoStop()
    {
        var adapter = new MockWorldAdapter();
        var journal = NullAgentJournal.Instance;
        var service = AgentBackgroundServiceTestHelper.BuildMinimal(adapter, journal);

        await service.StartAsync(CancellationToken.None);
        try
        {
            service.SetGoal(new SimpleGoal("Mine", "Mine blocks", ["mine"], _ => false));

            adapter.PushEvent(new HealthEvent(20, 20, DateTimeOffset.UtcNow));
            await Task.Delay(30);
            // Only 2 HP drop — below system threshold (6 HP)
            adapter.PushEvent(new HealthEvent(18, 20, DateTimeOffset.UtcNow));
            await Task.Delay(200);

            var stopSent = adapter.SentActions.Any(a =>
                a.Tool.Equals("StopNow", StringComparison.OrdinalIgnoreCase));

            Assert.That(stopSent, Is.False,
                "2 HP drop is below interrupt threshold — no stop should be sent.");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    // ─── P0-C: BuildGoalDecomposer / HtnTaskLibrary SearchedRadius retry gate ─

    /// <summary>
    /// Sprint 36 P0-C: When BuildFactKeys.LastFlatArea == 0 AND
    /// event:FlatAreaFound:SearchedRadius &lt; 48 (FlatAreaRetryRadius), DecomposeBuild
    /// should retry with the larger radius (48). This verifies the retry is emitted
    /// when there's still room to expand the search.
    /// </summary>
    [Test]
    public void DecomposeBuild_SearchedRadius32_AreaZero_RetryFindFlatAreaEmitted()
    {
        // Arrange: last scan found area=0 at radius=32 → retry with radius=48
        var state = WorldState.Empty.With(b =>
        {
            b.SetFact(BuildFactKeys.LastFlatArea,          "0",  FactSource.Observed);
            b.SetFact("event:FlatAreaFound:SearchedRadius", "32", FactSource.Observed);
        });

        var library = new HtnTaskLibrary();
        var blueprint = new Agent.Construction.Blueprint
        {
            Id = "test-house", Name = "Test House", Materials = []
        };

        // DecomposeBuild with requireOrigin=true and no stored origin
        var actions = library.DecomposeBuild(blueprint, [], 0, 0, 0, state, requireOrigin: true);

        // Assert: emits a FindFlatArea with radius >= 48
        var findFlat = actions.FirstOrDefault(a =>
            a.Tool.Equals("FindFlatArea", StringComparison.OrdinalIgnoreCase));

        Assert.That(findFlat, Is.Not.Null,
            "SearchedRadius=32, Area=0 → retry FindFlatArea must be emitted (Sprint 36 P0-C).");

        if (findFlat!.Arguments.TryGetValue("radius", out var rv))
        {
            var radius = Convert.ToInt32(rv);
            Assert.That(radius, Is.GreaterThanOrEqualTo(48),
                "Retry FindFlatArea must use at least radius=48 (FlatAreaRetryRadius).");
        }
    }

    [Test]
    public void DecomposeBuild_SearchedRadius48_AreaZero_NoRetry_EmptyPlan()
    {
        // Arrange: last scan found area=0 at radius=48 → already at max, no retry
        var state = WorldState.Empty.With(b =>
        {
            b.SetFact(BuildFactKeys.LastFlatArea,          "0",  FactSource.Observed);
            b.SetFact("event:FlatAreaFound:SearchedRadius", "48", FactSource.Observed);
        });

        var library = new HtnTaskLibrary();
        var blueprint = new Agent.Construction.Blueprint
        {
            Id = "test-house", Name = "Test House", Materials = []
        };

        var actions = library.DecomposeBuild(blueprint, [], 0, 0, 0, state, requireOrigin: true);

        // When SearchedRadius >= 48 and Area = 0, plan must be empty (no more retry).
        var retryFindFlat = actions.Any(a =>
            a.Tool.Equals("FindFlatArea", StringComparison.OrdinalIgnoreCase)
            && a.Arguments.TryGetValue("radius", out var rv)
            && Convert.ToInt32(rv) >= 48);

        Assert.That(retryFindFlat, Is.False,
            "SearchedRadius=48, Area=0 → no retry FindFlatArea >= 48 should be emitted. " +
            "Maximum search radius exhausted (Sprint 36 P0-C).");

        Assert.That(actions, Is.Empty,
            "When max radius exhausted and no flat area found, plan must be empty.");
    }

    [Test]
    public void DecomposeBuild_NoSearchedRadius_AreaZero_DefaultRetry()
    {
        // Arrange: area=0 but no SearchedRadius fact (first-ever zero-area result)
        // → should retry (SearchedRadius defaults to 0 < 48)
        var state = WorldState.Empty.With(b =>
        {
            b.SetFact(BuildFactKeys.LastFlatArea, "0", FactSource.Observed);
            // No SearchedRadius fact
        });

        var library = new HtnTaskLibrary();
        var blueprint = new Agent.Construction.Blueprint
        {
            Id = "test-house", Name = "Test House", Materials = []
        };

        var actions = library.DecomposeBuild(blueprint, [], 0, 0, 0, state, requireOrigin: true);

        // Should still retry since lastSearchedRadius defaults to 0 < 48
        var findFlat = actions.FirstOrDefault(a =>
            a.Tool.Equals("FindFlatArea", StringComparison.OrdinalIgnoreCase));

        Assert.That(findFlat, Is.Not.Null,
            "Area=0 without SearchedRadius fact defaults to radius=0 < 48, so retry should fire.");
    }

    // ─── P1-C: Tool names in LLM system prompt (scaffolding test) ─────────────

    /// <summary>
    /// Sprint 36 P1-C scaffolding: verify that registered tool names are exposed
    /// as a list that can be injected into the LLM system prompt.
    ///
    /// The full implementation wires ToolDispatcher.All into
    /// LlmChatInterpreter.BuildSystemPrompt. This test validates the data pipeline:
    /// that All returns the registered tools and their names are accessible.
    ///
    /// Update this test once P1-C wires the names into the actual prompt.
    /// </summary>
    [Test]
    public void ToolDispatcher_All_ExposesRegisteredToolNames()
    {
        var dispatcher = new ToolDispatcher();
        dispatcher.Register(new NullTool("MineBlock"));
        dispatcher.Register(new NullTool("GetStatus"));
        dispatcher.Register(new NullTool("SearchMemory"));
        dispatcher.Register(new NullTool("FindFlatArea"));

        var toolNames = dispatcher.All.Select(t => t.Name).ToList();

        Assert.That(toolNames, Contains.Item("MineBlock"),
            "ToolDispatcher.All should expose registered tool names for LLM prompt injection.");
        Assert.That(toolNames, Contains.Item("GetStatus"));
        Assert.That(toolNames, Contains.Item("SearchMemory"));
        Assert.That(toolNames, Contains.Item("FindFlatArea"));
        Assert.That(toolNames, Has.Count.EqualTo(4));
    }

    // ─── Local helpers ─────────────────────────────────────────────────────────

    file sealed class NullTool(string name) : ITool
    {
        public string Name        => name;
        public string Description => "test stub";
        public System.Text.Json.JsonElement InputSchema =>
            System.Text.Json.JsonDocument.Parse("{}").RootElement;
        public Task<ToolResult> ExecuteAsync(
            System.Text.Json.JsonElement arguments, CancellationToken ct = default)
            => Task.FromResult(new ToolResult(true, "ok"));
    }
}
