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
///   P0-B: CallWithOutcomeAsync returns (ToolResult, ActionOutcome) and does NOT double-log
///   P0-C: BuildGoalDecomposer/HtnTaskLibrary SearchedRadius retry gate
///   P1-B: WorldStateProjector.ApplyItemCrafted updates inventory correctly
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

    // ─── P0-B: CallWithOutcomeAsync ───────────────────────────────────────────

    /// <summary>
    /// Sprint 36 P0-B: CallWithOutcomeAsync must return a (ToolResult, ActionOutcome)
    /// tuple with correct Goal/Tool metadata, and must NOT produce duplicate journal
    /// entries. CallAsync already writes one ActionCompleted entry; CallWithOutcomeAsync
    /// must not call LogOutcome on top of that (audit finding #4 — fixed this sprint).
    /// </summary>
    [Test]
    public async Task CallWithOutcomeAsync_Success_ReturnsOutcomeAndDoesNotDoubleLog()
    {
        var journal = new SpyJournal();
        var dispatcher = new ToolDispatcher(journal);
        dispatcher.Register(new NullTool("GetStatus"));

        using var doc = System.Text.Json.JsonDocument.Parse("{}");
        var goalId = Guid.NewGuid();
        var (result, outcome) = await dispatcher.CallWithOutcomeAsync(goalId, "GetStatus", doc.RootElement);

        Assert.That(result.Success, Is.True, "ToolResult must be Success.");
        Assert.That(outcome.Success, Is.True, "ActionOutcome must reflect Success.");
        Assert.That(outcome.GoalId, Is.EqualTo(goalId), "GoalId must be threaded through.");
        Assert.That(outcome.ToolName, Is.EqualTo("GetStatus"), "ToolName must be set.");
        // Sprint 37: CallWithOutcomeAsync intentionally does NOT call LogOutcome
        // (anti-double-log guard — audit #4). The caller (DispatchActionsAsync) calls
        // _journal?.LogOutcome(outcome) after CallWithOutcomeAsync returns.
        // Verify: journal still empty after CallWithOutcomeAsync (no double log).
        Assert.That(journal.Entries, Has.Count.EqualTo(0),
            "CallWithOutcomeAsync must NOT produce a journal entry. " +
            "Caller (DispatchActionsAsync) calls _journal?.LogOutcome(outcome) explicitly.");
        // Simulate what the caller does — exactly one entry produced.
        ((IAgentJournal)journal).LogOutcome(outcome);  // default interface method — needs interface cast
        Assert.That(journal.Entries, Has.Count.EqualTo(1),
            "After caller calls LogOutcome, exactly one ActionCompleted entry exists.");
    }

    /// <summary>
    /// Sprint 36 P0-B: A tool that returns failure must produce a failed ActionOutcome.
    /// GoalId and ToolName must be correctly threaded through on failure paths.
    /// </summary>
    [Test]
    public async Task CallWithOutcomeAsync_ToolFailure_ReturnsFailedOutcome()
    {
        var dispatcher = new ToolDispatcher();
        dispatcher.Register(new FailingTool("BadTool"));

        using var doc = System.Text.Json.JsonDocument.Parse("{}");
        var goalId = Guid.NewGuid();
        var (result, outcome) = await dispatcher.CallWithOutcomeAsync(goalId, "BadTool", doc.RootElement);

        Assert.That(result.Success, Is.False, "ToolResult must be failure.");
        Assert.That(outcome.Success, Is.False, "ActionOutcome must reflect failure.");
        Assert.That(outcome.GoalId, Is.EqualTo(goalId), "GoalId must be threaded through on failure.");
        Assert.That(outcome.ToolName, Is.EqualTo("BadTool"), "ToolName must be set on failure.");
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
        var state = new WorldState().With(b =>
        {
            b.SetFact(BuildFactKeys.LastFlatArea,          "0",  FactSource.Observed);
            b.SetFact("event:FlatAreaFound:SearchedRadius", "32", FactSource.Observed);
        });

        var library = new HtnTaskLibrary();
        var blueprint = new global::Agent.Construction.Blueprint
        {
            Id = "test-house", Name = "Test House", Materials = []
        };

        // DecomposeBuild with requireOrigin=true and no stored origin
        var actions = library.DecomposeBuild(blueprint, [], new BuildOrigin(0, 0, 0, BuildOriginSource.AutoScanned), state, requireOrigin: true);

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
        var state = new WorldState().With(b =>
        {
            b.SetFact(BuildFactKeys.LastFlatArea,          "0",  FactSource.Observed);
            b.SetFact("event:FlatAreaFound:SearchedRadius", "48", FactSource.Observed);
        });

        var library = new HtnTaskLibrary();
        var blueprint = new global::Agent.Construction.Blueprint
        {
            Id = "test-house", Name = "Test House", Materials = []
        };

        var actions = library.DecomposeBuild(blueprint, [], new BuildOrigin(0, 0, 0, BuildOriginSource.AutoScanned), state, requireOrigin: true);

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
        var state = new WorldState().With(b =>
        {
            b.SetFact(BuildFactKeys.LastFlatArea, "0", FactSource.Observed);
            // No SearchedRadius fact
        });

        var library = new HtnTaskLibrary();
        var blueprint = new global::Agent.Construction.Blueprint
        {
            Id = "test-house", Name = "Test House", Materials = []
        };

        var actions = library.DecomposeBuild(blueprint, [], new BuildOrigin(0, 0, 0, BuildOriginSource.AutoScanned), state, requireOrigin: true);

        // Should still retry since lastSearchedRadius defaults to 0 < 48
        var findFlat = actions.FirstOrDefault(a =>
            a.Tool.Equals("FindFlatArea", StringComparison.OrdinalIgnoreCase));

        Assert.That(findFlat, Is.Not.Null,
            "Area=0 without SearchedRadius fact defaults to radius=0 < 48, so retry should fire.");
    }

    // ─── P1-B: ItemCraftedEvent inventory tests ────────────────────────────────

    /// <summary>
    /// Sprint 36 P1-B: WorldStateProjector.ApplyItemCrafted must add the crafted
    /// item to inventory, mirroring the ApplyItemCollected pattern from Sprint 35.
    /// </summary>
    [Test]
    public void ItemCraftedEvent_UpdatesInventory()
    {
        var projector = new WorldStateProjector();
        var updated = projector.Apply(new WorldState(),
            new ItemCraftedEvent("iron_pickaxe", 1, DateTimeOffset.UtcNow));

        Assert.That(updated.Inventory.TryGetValue("iron_pickaxe", out var count), Is.True,
            "ItemCraftedEvent must add the crafted item to inventory (Sprint 36 P1-B).");
        Assert.That(count, Is.EqualTo(1));
    }

    /// <summary>
    /// Sprint 36 P1-B: ItemCraftedEvent must strip the minecraft: namespace prefix
    /// so inventory keys are always bare item IDs (mirrors ApplyItemCollected normalization).
    /// </summary>
    [Test]
    public void ItemCraftedEvent_StripsMinecraftPrefix()
    {
        var projector = new WorldStateProjector();
        var updated = projector.Apply(new WorldState(),
            new ItemCraftedEvent("minecraft:iron_pickaxe", 1, DateTimeOffset.UtcNow));

        Assert.That(updated.Inventory.TryGetValue("iron_pickaxe", out var count), Is.True,
            "minecraft: prefix must be stripped from crafted item key (Sprint 36 P1-B).");
        Assert.That(count, Is.EqualTo(1));
        Assert.That(updated.Inventory.ContainsKey("minecraft:iron_pickaxe"), Is.False,
            "Raw namespace-prefixed key must not appear in inventory.");
    }

    // ─── P1-C: Tool names in LLM system prompt (scaffolding test) ─────────────

    /// <summary>
    /// Sprint 36 P1-C scaffolding: verify that registered tool names are exposed
    /// as a list that can be injected into the LLM system prompt.
    ///
    /// The full implementation wires ToolDispatcher.RegisteredNames into
    /// LlmChatInterpreter.BuildSystemPrompt via Program.cs DI. This test validates
    /// the data pipeline — that RegisteredNames returns all keys (including aliases)
    /// in deterministic sorted order.
    /// </summary>
    [Test]
    public void ToolDispatcher_RegisteredNames_IncludesAliasesAndIsSorted()
    {
        var dispatcher = new ToolDispatcher();
        dispatcher.Register(new NullTool("MineBlock"));
        dispatcher.Register(new NullTool("GetStatus"));
        dispatcher.Register("Status", dispatcher.Get("GetStatus")!);   // alias
        dispatcher.Register(new NullTool("SearchMemory"));
        dispatcher.Register(new NullTool("FindFlatArea"));

        var names = dispatcher.RegisteredNames;

        // Aliases must be included
        Assert.That(names, Contains.Item("GetStatus"),
            "RegisteredNames must include canonical tool names.");
        Assert.That(names, Contains.Item("Status"),
            "RegisteredNames must include alias keys (Sprint 36 P1-C audit fix #2).");
        Assert.That(names, Contains.Item("MineBlock"));
        Assert.That(names, Contains.Item("SearchMemory"));
        Assert.That(names, Contains.Item("FindFlatArea"));

        // Must be sorted (case-insensitive)
        var sorted = names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.That(names.ToList(), Is.EqualTo(sorted),
            "RegisteredNames must be in case-insensitive alphabetical order (deterministic for LLM prompt).");
    }

    // ─── Local helpers ─────────────────────────────────────────────────────────

    private sealed class NullTool(string name) : ITool
    {
        public string Name        => name;
        public string Description => "test stub";
        public System.Text.Json.JsonElement InputSchema =>
            System.Text.Json.JsonDocument.Parse("{}").RootElement;
        public Task<ToolResult> ExecuteAsync(
            System.Text.Json.JsonElement arguments, CancellationToken ct = default)
            => Task.FromResult(new ToolResult(true, "ok"));
    }

    private sealed class FailingTool(string name) : ITool
    {
        public string Name        => name;
        public string Description => "always fails";
        public System.Text.Json.JsonElement InputSchema =>
            System.Text.Json.JsonDocument.Parse("{}").RootElement;
        public Task<ToolResult> ExecuteAsync(
            System.Text.Json.JsonElement arguments, CancellationToken ct = default)
            => Task.FromResult(new ToolResult(false, "intentional failure"));
    }

    private sealed class SpyJournal : IAgentJournal
    {
        private readonly List<JournalEntry> _entries = [];
        public List<JournalEntry> Entries => _entries;
        public int Count => _entries.Count;
        public IReadOnlyList<JournalEntry> All => _entries.AsReadOnly();
        public void Log(JournalEntry entry) => _entries.Add(entry);
        public IReadOnlyList<JournalEntry> Recent(int count) =>
            _entries.TakeLast(count).Reverse().ToList().AsReadOnly();
        public IReadOnlyList<JournalEntry> Query(
            JournalEntryType? type = null,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null) =>
            _entries.Where(e =>
                (type is null || e.Type == type) &&
                (from is null || e.Timestamp >= from) &&
                (to is null || e.Timestamp <= to))
            .ToList().AsReadOnly();
        public void Clear() => _entries.Clear();
    }
}
