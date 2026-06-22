using Agent.Core;
using Agent.Planning;
using Agent.Planning.Goals;
using Agent.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// Sprint 21: Tests for inventory freshness gate, governor pre-plan check,
/// D-2 BlockNotFound fact verification, and TryParseTruncatedJson gather/build support.
/// </summary>

// ── P0-A: WorldState.IsInventoryStale freshness gate ─────────────────────────

[TestFixture]
public sealed class Sprint21FreshnessGateTests
{
    private static ItemSpec MakeDirtSpec() => new()
    {
        ItemId       = "dirt",
        DisplayName  = "Dirt",
        SourceBlocks = ["dirt"],
        RequiresSmelting  = false,
        MinHarvestLevel   = 0,
    };

    /// <summary>
    /// FAILING TEST (written before implementation):
    /// After SetGoal is called, WorldState.IsInventoryStale = true.
    /// GenericGatherGoal.IsComplete must return false even when inventory shows enough items.
    /// Root cause of bug: after admin /clear, WorldState still shows old inventory;
    /// IsComplete returns true immediately and the goal never actually gathers items.
    /// </summary>
    [Test]
    public void GatherGoal_WithStaleInventory_DoesNotFalseComplete()
    {
        // Arrange: state with stale inventory — 12 dirt cached from previous session,
        // but IsInventoryStale=true because SetGoal was just called.
        var state = new WorldState
        {
            Inventory        = new Dictionary<string, int> { ["dirt"] = 12 },
            IsInventoryStale = true,
        };
        var goal = new GenericGatherGoal(MakeDirtSpec(), targetCount: 5);

        // Act
        var complete = goal.IsComplete(state);

        // Assert: stale inventory must not satisfy IsComplete
        Assert.That(complete, Is.False,
            "GenericGatherGoal must not complete when IsInventoryStale=true, " +
            "even if inventory appears to have enough items. " +
            "This prevents false-completion after admin /clear.");
    }

    [Test]
    public void GatherGoal_WithFreshInventory_CompletesNormally()
    {
        // Arrange: fresh inventory after GetStatus confirmed items
        var state = new WorldState
        {
            Inventory        = new Dictionary<string, int> { ["dirt"] = 12 },
            IsInventoryStale = false,
        };
        var goal = new GenericGatherGoal(MakeDirtSpec(), targetCount: 5);

        Assert.That(goal.IsComplete(state), Is.True,
            "GenericGatherGoal should complete when inventory is sufficient and freshness confirmed.");
    }

    [Test]
    public void GatherGoal_DefaultWorldState_IsNotStale()
    {
        // Default WorldState should have IsInventoryStale=false
        var state = new WorldState { Inventory = new Dictionary<string, int> { ["dirt"] = 5 } };
        Assert.That(state.IsInventoryStale, Is.False,
            "Default WorldState must not be stale — only SetGoal marks it stale.");
    }

    [Test]
    public void WorldState_SetInventoryStale_RoundTrips()
    {
        var state = new WorldState();
        Assert.That(state.IsInventoryStale, Is.False);

        var stale = state.With(b => b.SetInventoryStale(true));
        Assert.That(stale.IsInventoryStale, Is.True,
            "SetInventoryStale(true) should set the flag.");

        var fresh = stale.With(b => b.SetInventoryStale(false));
        Assert.That(fresh.IsInventoryStale, Is.False,
            "SetInventoryStale(false) should clear the flag.");
    }

    [Test]
    public void WorldStateProjector_ApplyStatus_ClearsStaleness()
    {
        // Arrange: stale state
        var stale = new WorldState
        {
            Inventory        = new Dictionary<string, int> { ["dirt"] = 0 },
            IsInventoryStale = true,
        };
        var projector = new WorldStateProjector();
        var statusEv = new StatusEvent(
            new Position(0, 64, 0), Health: 20, Food: 20,
            Inventory: new Dictionary<string, int> { ["dirt"] = 5 },
            GameMode: null,
            Timestamp: DateTimeOffset.UtcNow);

        // Act: apply status event
        var result = projector.Apply(stale, statusEv);

        // Assert: inventory fresh after status
        Assert.That(result.IsInventoryStale, Is.False,
            "ApplyStatus must clear IsInventoryStale so goals can complete after GetStatus.");
        Assert.That(result.Inventory.GetValueOrDefault("dirt"), Is.EqualTo(5),
            "Inventory should reflect the StatusEvent values.");
    }

    [Test]
    public void GatherGoal_RequiresSmelting_AlsoRespectsStaleness()
    {
        // Smeltable items (e.g. iron_ingot) should also respect the stale flag
        var ironIngotSpec = new ItemSpec
        {
            ItemId           = "iron_ingot",
            DisplayName      = "Iron Ingot",
            SourceBlocks     = ["iron_ore"],
            RequiresSmelting = true,
            MinHarvestLevel  = 1,
        };
        var state = new WorldState
        {
            Inventory        = new Dictionary<string, int> { ["iron_ingot"] = 10 },
            IsInventoryStale = true,
        };
        var goal = new GenericGatherGoal(ironIngotSpec, targetCount: 3);

        Assert.That(goal.IsComplete(state), Is.False,
            "Even smelted-product goals must respect IsInventoryStale.");
    }
}

// ── P0-B: Governor pre-PlanAsync IsStalled check ─────────────────────────────

[TestFixture]
public sealed class Sprint21GovernorPrePlanTests
{
    private MockWorldAdapter _adapter = null!;
    private ToolDispatcher   _dispatcher = null!;
    private MockPlanner      _planner = null!;

    [SetUp]
    public void SetUp()
    {
        _adapter    = new MockWorldAdapter();
        _dispatcher = new ToolDispatcher();
        _planner    = new MockPlanner();
    }

    /// <summary>
    /// FAILING TEST (written before implementation):
    /// When governor.IsStalled == true before PlanAsync is called, PlanAsync must NOT be called.
    /// Current bug: PlanAsync is called every 2s even during STALL because the governor check
    /// happens AFTER PlanAsync returns. Fix: check IsStalled BEFORE calling PlanAsync.
    /// </summary>
    [Test]
    public async Task StalledGovernor_BlocksPlanAsync_BeforeItIsEvenCalled()
    {
        // Arrange: a governor that is stalled from the very start (IsStalled=true)
        var stalledGov = new AlwaysStalledGovernor();

        _planner.PlanToReturn = new ActionPlan(
            "GatherDirt", [],
            [new ActionData { Tool = "NoOp" }]);
        _dispatcher.Register(new Sprint21NoOpTool("NoOp"));

        var service = new WebUI.Blazor.AgentBackgroundService(
            _adapter, _dispatcher,
            NullLogger<WebUI.Blazor.AgentBackgroundService>.Instance,
            _planner,
            replanGovernor: stalledGov);

        service.SetGoal(new SimpleGoal("GatherDirt", "", [], _ => false));

        // Act: run for 250ms — enough for the old code to call PlanAsync once,
        // but the fixed code delays 10s during STALL so PlanAsync is never reached.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var task = service.StartAsync(cts.Token);
        await Task.Delay(200);
        cts.Cancel();
        try { await task; } catch (OperationCanceledException) { }

        // Assert: PlanAsync must NOT have been called (STALL blocks it at pre-check)
        Assert.That(_planner.PlanCalls, Is.Empty,
            "PlanAsync must not be called when governor.IsStalled=true. " +
            "The pre-plan check should skip PlanAsync and delay 10s instead.");
    }

    /// <summary>
    /// Complementary: when governor is NOT stalled, PlanAsync IS called normally.
    /// </summary>
    [Test]
    public async Task NotStalledGovernor_AllowsPlanAsync()
    {
        // No governor (default null) — PlanAsync runs normally
        _planner.PlanToReturn = new ActionPlan(
            "GatherDirt", [],
            [new ActionData { Tool = "NoOp" }]);
        _dispatcher.Register(new Sprint21NoOpTool("NoOp"));

        var service = new WebUI.Blazor.AgentBackgroundService(
            _adapter, _dispatcher,
            NullLogger<WebUI.Blazor.AgentBackgroundService>.Instance,
            _planner);

        service.SetGoal(new SimpleGoal("GatherDirt", "", [], _ => false));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var task = service.StartAsync(cts.Token);
        var deadline = DateTime.UtcNow.AddMilliseconds(400);
        while (_planner.PlanCalls.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        cts.Cancel();
        try { await task; } catch (OperationCanceledException) { }

        Assert.That(_planner.PlanCalls, Has.Count.GreaterThan(0),
            "PlanAsync should be called normally when governor is not stalled.");
    }
}

// ── P1-B: D-2 — BlockNotFound fact producer verification ────────────────────

[TestFixture]
public sealed class Sprint21BlockNotFoundFactTests
{
    [Test]
    public void BlockNotFoundEvent_SetsBlockNotFoundFact_InWorldState()
    {
        // Arrange
        var projector = new WorldStateProjector();
        var ev = new BlockNotFoundEvent("oak_log", 0, DateTimeOffset.UtcNow);

        // Act
        var result = projector.Apply(new WorldState(), ev);

        // Assert: the fact key that HtnTaskLibrary.GatherItemDecompose reads
        Assert.That(result.Facts.TryGetValue("event:BlockNotFound:Block", out var v), Is.True,
            "WorldStateProjector must set 'event:BlockNotFound:Block' on BlockNotFoundEvent. " +
            "GatherItemDecompose reads this fact to decide whether to insert a Wander step.");
        Assert.That(v?.ToString(), Is.EqualTo("oak_log"),
            "Fact value must equal the event's Block property.");
    }

    [Test]
    public void BlockNotFoundEvent_MinedCountStored_AsWell()
    {
        var projector = new WorldStateProjector();
        var ev = new BlockNotFoundEvent("stone", 3, DateTimeOffset.UtcNow);
        var result = projector.Apply(new WorldState(), ev);

        Assert.That(result.Facts.TryGetValue("event:BlockNotFound:MinedCount", out var v), Is.True);
        Assert.That(v?.ToString(), Is.EqualTo("3"));
    }

    [Test]
    public void GatherItemDecompose_InsertsWander_WhenBlockNotFoundFactMatches()
    {
        // Verify that HtnTaskLibrary inserts Wander when event:BlockNotFound:Block matches spec
        var lib = new HtnTaskLibrary();

        // State where oak_log was not found
        var state = new WorldState();
        state = state.With(b => b.SetFact("event:BlockNotFound:Block", "oak_log", FactSource.Observed));  // Sprint 33 P1-3;

        var spec = new ItemSpec
        {
            ItemId       = "oak_log",
            DisplayName  = "Oak Log",
            SourceBlocks = ["oak_log", "birch_log", "spruce_log"],
            RequiresSmelting  = false,
            MinHarvestLevel   = 0,
        };

        var actions = lib.DecomposeGatherItem(spec, ["10"], state);

        Assert.That(actions.Any(a => a.Tool.Equals("Wander", StringComparison.OrdinalIgnoreCase)),
            Is.True,
            "Wander should be inserted when event:BlockNotFound:Block matches a source block.");
    }

    [Test]
    public void GatherItemDecompose_NoWander_WhenNoBlockNotFoundFact()
    {
        var lib = new HtnTaskLibrary();
        var state = new WorldState(); // no BlockNotFound fact

        var spec = new ItemSpec
        {
            ItemId       = "oak_log",
            DisplayName  = "Oak Log",
            SourceBlocks = ["oak_log", "birch_log"],
            RequiresSmelting  = false,
            MinHarvestLevel   = 0,
        };

        var actions = lib.DecomposeGatherItem(spec, ["10"], state);

        Assert.That(actions.Any(a => a.Tool.Equals("Wander", StringComparison.OrdinalIgnoreCase)),
            Is.False,
            "Wander must NOT appear on the first gather attempt (no BlockNotFound fact).");
    }
}

// ── P1-C: TryParseTruncatedJson gather/build support ────────────────────────

[TestFixture]
public sealed class Sprint21TruncatedJsonGatherTests
{
    private static ChatInterpretation? Parse(string json)
    {
        var method = typeof(LlmChatInterpreter)
            .GetMethod("TryParseTruncatedJson",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (method is null)
        {
            Assert.Ignore("TryParseTruncatedJson not found via reflection — skipping.");
            return null;
        }
        // Sprint 38 P1-B: TryParseTruncatedJson now takes an optional IntentManager parameter.
        return (ChatInterpretation?)method.Invoke(null, new object?[] { json, null });
    }

    [Test]
    public void TruncatedGatherJson_ExtractsGoalName()
    {
        // Truncated JSON from llama3.2:3b hitting num_predict limit on "gather sand"
        var json = @"{ ""addressed"": ""yes"", ""intent"": ""gather"", ""item"": ""sand"", ""count"": 10, ""response"": ""Sure, I'll gather";
        var result = Parse(json);

        Assert.That(result, Is.Not.Null, "Should parse truncated gather JSON.");
        Assert.That(result!.IntentType, Is.EqualTo(ChatIntentType.CreateGoal),
            "gather intent should map to CreateGoal.");
        Assert.That(result.GoalName, Is.EqualTo("GatherItem:sand"),
            "Goal name should be GatherItem:sand from item field.");
    }

    [Test]
    public void TruncatedGatherJson_DefaultsCountToTen_WhenCountMissing()
    {
        var json = @"{ ""addressed"": ""yes"", ""intent"": ""gather"", ""item"": ""dirt"", ""response"": ""Let me";
        var result = Parse(json);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.GoalName, Is.EqualTo("GatherItem:dirt"));
        Assert.That(result.GoalParameters?.TryGetValue("count", out var c) == true && c is int count && count == 10,
            Is.True, "Default count should be 10 when count field is absent.");
    }

    [Test]
    public void TruncatedBuildJson_ExtractsBlueprint()
    {
        var json = @"{ ""addressed"": ""yes"", ""intent"": ""build"", ""blueprint"": ""small-house"", ""response"": ""Building";
        var result = Parse(json);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.IntentType, Is.EqualTo(ChatIntentType.CreateGoal));
        Assert.That(result.GoalName, Is.EqualTo("Build:small-house"));
    }

    [Test]
    public void TruncatedGatherJson_WithNoItem_ReturnsUnknown()
    {
        // intent=gather but item field is cut off entirely
        var json = @"{ ""addressed"": ""yes"", ""intent"": ""gather"", ""response"": ""Sure";
        var result = Parse(json);

        // Should parse but without a goal (no item to gather)
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.IntentType, Is.EqualTo(ChatIntentType.Unknown),
            "gather without item should be Unknown (cannot create goal without item).");
    }

    [Test]
    public void TruncatedStatusJson_StillWorksAfterP1CChanges()
    {
        // Regression: existing status truncation must still work
        var json = @"{ ""addressed"": ""yes"", ""intent"": ""status"", ""response"": ""I'm mining";
        var result = Parse(json);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.IntentType, Is.EqualTo(ChatIntentType.QueryStatus));
    }
}

// ── Test helpers ─────────────────────────────────────────────────────────────

/// <summary>
/// Sprint 21: IReplanGovernor that always reports IsStalled=true.
/// Used to verify the pre-plan IsStalled check in DispatchActionsAsync.
/// </summary>
file sealed class AlwaysStalledGovernor : IReplanGovernor
{
    public bool IsStalled => true;
    public ReplanVerdict Evaluate(string _) => ReplanVerdict.Stalled;
    public void RecordProgress() { }
    public void Reset() { }
}

/// <summary>Sprint 21: local no-op tool (avoids name conflict with AgentBackgroundServiceTests).</summary>
file sealed class Sprint21NoOpTool(string name) : ITool
{
    public string Name => name;
    public string Description => "no-op for Sprint 21 tests";
    public System.Text.Json.JsonElement InputSchema =>
        System.Text.Json.JsonDocument.Parse("{}").RootElement;
    public Task<ToolResult> ExecuteAsync(
        System.Text.Json.JsonElement arguments, CancellationToken ct = default)
        => Task.FromResult(new ToolResult(true, "no-op"));
}
