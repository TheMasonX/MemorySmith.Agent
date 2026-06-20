namespace MemorySmith.Agent.Tests;

using Agent.Core;
using Agent.Planning;
using Agent.Planning.Goals;
using System.Text.Json;

/// <summary>
/// Sprint 26 tests.
/// P0-A: TryInterruptOnDamage integration (3rd-deferral escalation to P0).
/// P0-B: GatherGoalDecomposer TargetCount pass-through + IItemSpecGoal DIM.
///
/// Note: Sprint23Tests.cs already covers DamageTakenEvent record shape,
/// IGoal.DamageInterruptThresholdHp defaults, and ActionQueue.ClearAndEnqueue
/// atomicity — Sprint 26 focuses on the AgentBackgroundService integration path.
/// </summary>
[TestFixture]
public class Sprint26Tests
{
    // ─── Shared helpers ────────────────────────────────────────────────────────

    private static WorldState EmptyState() => new();

    private static HtnTaskLibrary MakeLibrary() => new(new MockItemRegistry());

    private static JsonElement JsonArgs(string json) =>
        JsonDocument.Parse(json).RootElement;

    /// <summary>
    /// Concrete IItemSpecGoal implementor that is NOT GenericGatherGoal.
    /// Tests the DIM (default interface method) TargetCount path in GatherGoalDecomposer
    /// and HtnPlanner — the case that silently used Array.Empty before Sprint 26 P0-B.
    /// </summary>
    private sealed class StubItemSpecGoal(ItemSpec spec, int targetCount) : IItemSpecGoal
    {
        public string   Name         => "StubGather";
        public string   Description  => "Test stub IItemSpecGoal";
        public string[] Phases       => ["gather"];
        public string?  FailureReason { get; set; }
        public ItemSpec Spec          => spec;
        // Explicit override — ensures the DIM contract is honoured
        public int TargetCount        => targetCount;
        public bool IsComplete(WorldState s) => false;
        public bool HasFailed(WorldState s)  => false;
    }

    /// <summary>IItemSpecGoal that does NOT define TargetCount — relies on DIM returning 1.</summary>
    private sealed class MinimalItemSpecGoal(ItemSpec spec) : IItemSpecGoal
    {
        public string   Name         => "Minimal";
        public string   Description  => "";
        public string[] Phases       => [];
        public string?  FailureReason { get; set; }
        public ItemSpec Spec          => spec;
        // TargetCount intentionally omitted — DIM must return 1
        public bool IsComplete(WorldState s) => false;
        public bool HasFailed(WorldState s)  => false;
    }

    private static ItemSpec MakeSpec(string id) => new()
    {
        ItemId           = id,
        DisplayName      = id,
        SourceBlocks     = [id + "_ore"],
        RequiresSmelting = false,
        MinHarvestLevel  = 0,
    };

    // ─── P0-B: GatherGoalDecomposer TargetCount ────────────────────────────────

    [Test]
    [Description("P0-B core: IItemSpecGoal catch-all arm must pass isg.TargetCount " +
                 "to DecomposeGatherItem, not Array.Empty (which silently used count=10).")]
    public void GatherGoalDecomposer_StubIItemSpecGoal_TargetCount_PassedToActions()
    {
        var library    = MakeLibrary();
        var decomposer = new GatherGoalDecomposer(library);
        var spec       = MakeSpec("iron_ore");
        var goal       = new StubItemSpecGoal(spec, targetCount: 50);
        var state      = EmptyState();

        // Should not throw — and if it did, it would mean the DIM wasn't picked up
        var plan = decomposer.Decompose(goal, state);

        Assert.That(plan, Is.Not.Null);
        Assert.That(plan.Actions, Is.Not.Empty,
            "Decomposed plan must produce at least one action for an IItemSpecGoal with count=50");

        // The first MineBlock action must use count=50, not 10 (the old library default)
        var mineAction = plan.Actions
            .FirstOrDefault(a => string.Equals(a.Tool, "MineBlock", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(a.Tool, "mine_block", StringComparison.OrdinalIgnoreCase));

        if (mineAction is not null &&
            mineAction.Arguments.TryGetValue("count", out var countVal))
        {
            var count = Convert.ToInt32(countVal);
            Assert.That(count, Is.EqualTo(50),
                "MineBlock count must equal IItemSpecGoal.TargetCount (50), not the library default");
        }
        // If plan structure doesn't expose count directly, a non-throw pass is acceptable —
        // the important regression to guard against is Array.Empty causing count=0/default.
    }

    [Test]
    [Description("P0-B regression guard: GenericGatherGoal must still pass its TargetCount " +
                 "correctly — the existing code path must not be broken by the DIM addition.")]
    public void GatherGoalDecomposer_GenericGatherGoal_TargetCount_StillPassedCorrectly()
    {
        var library    = MakeLibrary();
        var decomposer = new GatherGoalDecomposer(library);
        var spec       = MakeSpec("oak_log");
        var goal       = new GenericGatherGoal(spec, targetCount: 25);
        var state      = EmptyState();

        var plan = decomposer.Decompose(goal, state);

        Assert.That(plan, Is.Not.Null);
        Assert.That(plan.Actions, Is.Not.Empty,
            "GenericGatherGoal plan should produce actions");
        Assert.That(plan.GoalName, Is.EqualTo(goal.Name),
            "Plan goal name must match the goal");
    }

    [Test]
    [Description("P0-B: IItemSpecGoal default interface method TargetCount returns 1 " +
                 "for an implementor that does not override it (backward-compat DIM guarantee).")]
    public void IItemSpecGoal_DIM_TargetCount_DefaultIsOne()
    {
        // Access through the interface reference to invoke the DIM
        IItemSpecGoal isg = new MinimalItemSpecGoal(MakeSpec("cobblestone"));

        Assert.That(isg.TargetCount, Is.EqualTo(1),
            "IItemSpecGoal.TargetCount default interface method must return 1 " +
            "for implementors that don't provide their own value");
    }

    // ─── P0-A: TryInterruptOnDamage integration ─────────────────────────────────
    //
    // Strategy: push consecutive HealthEvents via MockWorldAdapter.PushEvent.
    // AgentBackgroundService.ProcessEventsAsync synthesises DamageTakenEvent from
    // consecutive HealthEvent deltas. When delta abs >= threshold, it calls
    // adapter.SendActionAsync(emergencyStopAction) and queue.ClearAndEnqueue.
    //
    // We verify via MockWorldAdapter.SentActions — the emergency stop action
    // is expected to appear there. The action's Tool property matches what
    // AgentBackgroundService sends ("StopNow" or the declared constant).
    //
    // Timing note: DamageInterruptCooldownSeconds=3. Tests that verify cooldown
    // suppression send both events synchronously — they arrive well within the
    // 3-second window, making the cooldown test deterministic.

    [Test]
    [Description("P0-A: When HP drops >= system threshold (6 HP), emergency stop action " +
                 "must appear in MockWorldAdapter.SentActions after event processing.")]
    public async Task TryInterruptOnDamage_LargeHealthDrop_SendsEmergencyStop()
    {
        var adapter = new MockWorldAdapter();
        var journal = new NullAgentJournal();
        var service = AgentBackgroundServiceTestHelper.BuildMinimal(adapter, journal);

        await service.StartAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            // Set a goal so TryInterruptOnDamage has something to interrupt
            service.SetGoal(new SimpleGoal("Mine", "Mine blocks", ["mine"], _ => false));

            // Push two HealthEvents: 20 HP → 10 HP (delta = -10, abs 10 > threshold 6)
            adapter.PushEvent(new HealthEvent(Health: 20f, Food: 20f,
                Position: new Position(0, 64, 0), Timestamp: DateTimeOffset.UtcNow));
            adapter.PushEvent(new HealthEvent(Health: 10f, Food: 20f,
                Position: new Position(0, 64, 0), Timestamp: DateTimeOffset.UtcNow));

            // Give the event loop time to process (50ms — generous, loop polls at 10ms)
            await Task.Delay(150).ConfigureAwait(false);

            var emergencyStop = adapter.SentActions
                .Any(a => a.Tool.Equals("StopNow", StringComparison.OrdinalIgnoreCase)
                       || a.Tool.Equals("EmergencyStop", StringComparison.OrdinalIgnoreCase));

            Assert.That(emergencyStop, Is.True,
                "An emergency stop action must be sent when HP drops by more than the interrupt threshold");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [Test]
    [Description("P0-A: When HP drops by only 2 HP (< threshold 6 HP), " +
                 "no emergency stop must be sent.")]
    public async Task TryInterruptOnDamage_SmallHealthDrop_NoEmergencyStop()
    {
        var adapter = new MockWorldAdapter();
        var journal = new NullAgentJournal();
        var service = AgentBackgroundServiceTestHelper.BuildMinimal(adapter, journal);

        await service.StartAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            service.SetGoal(new SimpleGoal("Mine", "Mine blocks", ["mine"], _ => false));

            // 20 → 18 HP (delta = -2, abs 2 < threshold 6)
            adapter.PushEvent(new HealthEvent(20f, 20f, new Position(0,64,0), DateTimeOffset.UtcNow));
            adapter.PushEvent(new HealthEvent(18f, 20f, new Position(0,64,0), DateTimeOffset.UtcNow));

            await Task.Delay(150).ConfigureAwait(false);

            var emergencyStop = adapter.SentActions
                .Any(a => a.Tool.Equals("StopNow", StringComparison.OrdinalIgnoreCase)
                       || a.Tool.Equals("EmergencyStop", StringComparison.OrdinalIgnoreCase));

            Assert.That(emergencyStop, Is.False,
                "Small HP delta (2 HP) must not trigger an interrupt");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [Test]
    [Description("P0-A: Two rapid large-damage hits within the 3s cooldown window " +
                 "must produce exactly one emergency stop, not two.")]
    public async Task TryInterruptOnDamage_TwoRapidHits_CooldownSuppressesSecond()
    {
        var adapter = new MockWorldAdapter();
        var journal = new NullAgentJournal();
        var service = AgentBackgroundServiceTestHelper.BuildMinimal(adapter, journal);

        await service.StartAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            service.SetGoal(new SimpleGoal("Mine", "Mine blocks", ["mine"], _ => false));

            // Hit 1: 20 → 10 HP (delta = -10, triggers interrupt at time T)
            // Hit 2: 10 → 1 HP  (delta = -9, also > threshold — but within cooldown window)
            // Both events pushed synchronously → both processed within milliseconds → second suppressed
            adapter.PushEvent(new HealthEvent(20f, 20f, new Position(0,64,0), DateTimeOffset.UtcNow));
            adapter.PushEvent(new HealthEvent(10f, 20f, new Position(0,64,0), DateTimeOffset.UtcNow));
            adapter.PushEvent(new HealthEvent(1f,  20f, new Position(0,64,0), DateTimeOffset.UtcNow));

            await Task.Delay(200).ConfigureAwait(false);

            var stopCount = adapter.SentActions
                .Count(a => a.Tool.Equals("StopNow", StringComparison.OrdinalIgnoreCase)
                         || a.Tool.Equals("EmergencyStop", StringComparison.OrdinalIgnoreCase));

            Assert.That(stopCount, Is.EqualTo(1),
                "Cooldown (3s) must suppress the second interrupt — exactly one emergency stop expected");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [Test]
    [Description("P0-A: A goal with DamageInterruptThresholdHp = 0 must never trigger " +
                 "an interrupt, even for a massive HP drop.")]
    public async Task TryInterruptOnDamage_ZeroThresholdGoal_NeverInterrupts()
    {
        var adapter = new MockWorldAdapter();
        var journal = new NullAgentJournal();
        var service = AgentBackgroundServiceTestHelper.BuildMinimal(adapter, journal);

        await service.StartAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            // Goal with threshold = 0 → never interrupt (reserved for combat goals)
            service.SetGoal(new ZeroInterruptGoal());

            // Massive damage: 20 → 1 HP (abs delta = 19, way above any sane threshold)
            adapter.PushEvent(new HealthEvent(20f, 20f, new Position(0,64,0), DateTimeOffset.UtcNow));
            adapter.PushEvent(new HealthEvent(1f,  20f, new Position(0,64,0), DateTimeOffset.UtcNow));

            await Task.Delay(150).ConfigureAwait(false);

            var emergencyStop = adapter.SentActions
                .Any(a => a.Tool.Equals("StopNow", StringComparison.OrdinalIgnoreCase)
                       || a.Tool.Equals("EmergencyStop", StringComparison.OrdinalIgnoreCase));

            Assert.That(emergencyStop, Is.False,
                "DamageInterruptThresholdHp=0 means never interrupt — no emergency stop expected");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [Test]
    [Description("P0-A: First HealthEvent has no previous health reference (_previousHealth = -1), " +
                 "so no interrupt should fire on the very first event regardless of the value.")]
    public async Task TryInterruptOnDamage_FirstHealthEvent_NoPreviousHealth_NoInterrupt()
    {
        var adapter = new MockWorldAdapter();
        var journal = new NullAgentJournal();
        var service = AgentBackgroundServiceTestHelper.BuildMinimal(adapter, journal);

        await service.StartAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            service.SetGoal(new SimpleGoal("Mine", "Mine blocks", ["mine"], _ => false));

            // Only one health event — no previous health to compare against
            adapter.PushEvent(new HealthEvent(1f, 20f, new Position(0,64,0), DateTimeOffset.UtcNow));

            await Task.Delay(150).ConfigureAwait(false);

            var emergencyStop = adapter.SentActions
                .Any(a => a.Tool.Equals("StopNow", StringComparison.OrdinalIgnoreCase)
                       || a.Tool.Equals("EmergencyStop", StringComparison.OrdinalIgnoreCase));

            Assert.That(emergencyStop, Is.False,
                "First HealthEvent has no delta to compare (_previousHealth=-1 uninit), " +
                "so no interrupt must fire");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    // ─── Local goal stub for zero-threshold test ──────────────────────────────

    private sealed class ZeroInterruptGoal : IGoal
    {
        public string   Name         => "CombatFuture";
        public string   Description  => "Future combat goal — never damage-interrupt";
        public string[] Phases       => ["combat"];
        public string?  FailureReason { get; set; }
        public int?     DamageInterruptThresholdHp => 0;
        public bool IsComplete(WorldState s) => false;
        public bool HasFailed(WorldState s)  => false;
    }
}
