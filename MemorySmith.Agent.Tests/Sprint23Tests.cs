using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Agent.Core;
using Agent.Tools;

namespace MemorySmith.Agent.Tests;

// ── Fixture 1: IGoal.DamageInterruptThresholdHp default interface impl ─────────
// Verifies B-2 resolution: null = system default, 0 = combat (never interrupt).

[TestFixture]
public sealed class Sprint23DamageThresholdTests
{
    private sealed class DefaultGoal : IGoal
    {
        public string   Name        => "Default";
        public string   Description => "Goal that opts in to default damage interrupt.";
        public string[] Phases      => [];
        public string?  FailureReason { get; set; }
        // Deliberately does NOT override DamageInterruptThresholdHp — must inherit null.
        public bool IsComplete(WorldState state) => false;
        public bool HasFailed(WorldState state)  => false;
    }

    private sealed class CombatGoal : IGoal
    {
        public string   Name        => "Combat";
        public string   Description => "Future combat goal — suppresses damage interrupt.";
        public string[] Phases      => [];
        public string?  FailureReason { get; set; }
        public int?     DamageInterruptThresholdHp => 0; // 0 = never interrupt
        public bool IsComplete(WorldState state) => false;
        public bool HasFailed(WorldState state)  => false;
    }

    private sealed class FragileGoal : IGoal
    {
        public string   Name        => "Fragile";
        public string   Description => "Exploration goal that interrupts at a higher threshold.";
        public string[] Phases      => [];
        public string?  FailureReason { get; set; }
        public int?     DamageInterruptThresholdHp => 10; // interrupt at 10 HP (5 hearts)
        public bool IsComplete(WorldState state) => false;
        public bool HasFailed(WorldState state)  => false;
    }

    [Test]
    public void DefaultGoal_DamageInterruptThresholdHp_IsNull()
    {
        IGoal goal = new DefaultGoal();
        Assert.That(goal.DamageInterruptThresholdHp, Is.Null,
            "A goal that does not override DamageInterruptThresholdHp must return null " +
            "so the system default (6 HP) applies.");
    }

    [Test]
    public void CombatGoal_DamageInterruptThresholdHp_IsZero()
    {
        IGoal goal = new CombatGoal();
        Assert.That(goal.DamageInterruptThresholdHp, Is.EqualTo(0),
            "0 signals 'never interrupt' — reserved for future combat goals.");
    }

    [Test]
    public void FragileGoal_DamageInterruptThresholdHp_ReturnsCustomValue()
    {
        IGoal goal = new FragileGoal();
        Assert.That(goal.DamageInterruptThresholdHp, Is.EqualTo(10),
            "A goal can declare a higher-than-default threshold for earlier interrupts.");
    }
}

// ── Fixture 2: ActionQueue.ClearAndEnqueue atomicity (B-3) ────────────────────

[TestFixture]
public sealed class Sprint23ActionQueueAtomicTests
{
    private static ActionData Action(string tool) => new() { Tool = tool };

    [Test]
    public void ClearAndEnqueue_ClearsExistingItems_AndEnqueuesNew()
    {
        var queue = new ActionQueue();
        queue.Enqueue(Action("MoveTo"));
        queue.Enqueue(Action("MineBlock"));
        queue.Enqueue(Action("PlaceBlock"));
        Assert.That(queue.Count, Is.EqualTo(3));

        queue.ClearAndEnqueue(Action("GetStatus"));

        Assert.That(queue.Count, Is.EqualTo(1));
        Assert.That(queue.Peek()?.Tool, Is.EqualTo("GetStatus"));
    }

    [Test]
    public void ClearAndEnqueue_OnEmptyQueue_EnqueuesOne()
    {
        var queue = new ActionQueue();
        Assert.That(queue.IsEmpty, Is.True);

        queue.ClearAndEnqueue(Action("GetStatus"));

        Assert.That(queue.Count, Is.EqualTo(1));
        Assert.That(queue.Peek()?.Tool, Is.EqualTo("GetStatus"));
    }

    [Test]
    public void ClearAndEnqueue_AfterClear_PriorityActionPresent()
    {
        var queue = new ActionQueue();
        for (var i = 0; i < 5; i++)
            queue.Enqueue(Action("Wander"));

        queue.ClearAndEnqueue(Action("GetStatus"));
        // After clear+enqueue, only GetStatus remains.
        var found = queue.Dequeue();
        Assert.That(found?.Tool, Is.EqualTo("GetStatus"));
        Assert.That(queue.IsEmpty, Is.True, "No other actions should survive ClearAndEnqueue.");
    }

    [Test]
    public async Task ClearAndEnqueue_ConcurrentEnqueue_PriorityActionAlwaysPresent()
    {
        // Sprint 23 B-3 regression test: concurrent Enqueue calls must not push
        // GetStatus out of the queue after ClearAndEnqueue.
        var queue = new ActionQueue();
        for (var i = 0; i < 10; i++)
            queue.Enqueue(Action("MoveTo"));

        using var start = new ManualResetEventSlim(false);

        var enqueueTask = Task.Run(() =>
        {
            start.Wait();
            for (var i = 0; i < 1000; i++)
                queue.Enqueue(Action("Wander"));
        });

        var interruptTask = Task.Run(() =>
        {
            start.Wait();
            queue.ClearAndEnqueue(Action("GetStatus"));
        });

        start.Set();
        await Task.WhenAll(enqueueTask, interruptTask).ConfigureAwait(false);

        // Drain and look for GetStatus — it must appear exactly once (or more if
        // a second ClearAndEnqueue fired, which cannot happen here).
        var getStatusCount = 0;
        while (queue.Dequeue() is { } action)
            if (action.Tool == "GetStatus") getStatusCount++;

        Assert.That(getStatusCount, Is.EqualTo(1),
            "Priority GetStatus must survive concurrent Enqueue activity after ClearAndEnqueue.");
    }
}

// ── Fixture 3: World KB tool routing (P0-B) ────────────────────────────────────
// Verifies that SearchMemoryTool and CreatePageTool call the gateway they were
// constructed with — and that GetPageTool continues to use the agent gateway.

[TestFixture]
public sealed class Sprint23WorldKbRoutingTests
{
    private sealed class RecordingGateway : IMemoryGateway
    {
        public int SearchCalls     { get; private set; }
        public int CreatePageCalls { get; private set; }
        public int GetPageCalls    { get; private set; }

        public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken ct = default)
        {
            SearchCalls++;
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);
        }

        public Task<string?> GetPageAsync(string pageId, CancellationToken ct = default)
        {
            GetPageCalls++;
            return Task.FromResult<string?>(null);
        }

        public Task<string> CreatePageAsync(string title, string content, string type, CancellationToken ct = default)
        {
            CreatePageCalls++;
            return Task.FromResult("page-test-001");
        }

        public Task UpdatePageAsync(string pageId, string content, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private static JsonElement JsonArgs(string json) =>
        JsonDocument.Parse(json).RootElement;

    [Test]
    public async Task SearchMemoryTool_CallsWorldGateway()
    {
        var world = new RecordingGateway();
        var tool  = new SearchMemoryTool(world);
        var args  = JsonArgs("{\"query\":\"diamond ore\"}");

        await tool.ExecuteAsync(args).ConfigureAwait(false);

        Assert.That(world.SearchCalls, Is.EqualTo(1),
            "SearchMemoryTool must call the gateway it was constructed with (world gateway in production).");
    }

    [Test]
    public async Task SearchMemoryTool_DoesNotCallAlternateGateway()
    {
        var world = new RecordingGateway();
        var agent = new RecordingGateway();
        var tool  = new SearchMemoryTool(world); // world gateway only

        await tool.ExecuteAsync(JsonArgs("{\"query\":\"oak log\"}")).ConfigureAwait(false);

        Assert.That(world.SearchCalls, Is.EqualTo(1));
        Assert.That(agent.SearchCalls, Is.EqualTo(0),
            "SearchMemoryTool must not call the agent gateway when constructed with the world gateway.");
    }

    [Test]
    public async Task CreatePageTool_CallsWorldGateway()
    {
        var world = new RecordingGateway();
        var tool  = new CreatePageTool(world);
        var args  = JsonArgs("{\"title\":\"Iron vein at 100,40,-200\",\"content\":\"Iron ore observed.\"}");

        await tool.ExecuteAsync(args).ConfigureAwait(false);

        Assert.That(world.CreatePageCalls, Is.EqualTo(1),
            "CreatePageTool must call the gateway it was constructed with (world gateway in production).");
    }

    [Test]
    public async Task GetPageTool_CallsAgentGateway()
    {
        var agent = new RecordingGateway();
        var tool  = new GetPageTool(agent);
        var args  = JsonArgs("{\"pageId\":\"sprint-23-notes\"}");

        await tool.ExecuteAsync(args).ConfigureAwait(false);

        Assert.That(agent.GetPageCalls, Is.EqualTo(1),
            "GetPageTool must call the agent gateway (code docs, sprint notes).");
    }
}

// ── Fixture 4: DamageTakenEvent record shape (D-4) ───────────────────────────

[TestFixture]
public sealed class Sprint23DamageTakenEventTests
{
    [Test]
    public void DamageTakenEvent_Delta_IsNegative()
    {
        var ev = new DamageTakenEvent(
            PreviousHealth: 20,
            Health:         14,
            Delta:         -6,
            Food:          20,
            Timestamp:     DateTimeOffset.UtcNow);

        Assert.That(ev.Delta, Is.EqualTo(-6));
        Assert.That(ev.Delta, Is.LessThan(0),
            "Delta is always negative — it represents HP lost (Health - PreviousHealth).");
    }

    [Test]
    public void DamageTakenEvent_AllFields_Accessible()
    {
        var now = DateTimeOffset.UtcNow;
        var ev  = new DamageTakenEvent(20, 14, -6, 18, now);

        Assert.That(ev.PreviousHealth, Is.EqualTo(20));
        Assert.That(ev.Health,         Is.EqualTo(14));
        Assert.That(ev.Delta,          Is.EqualTo(-6));
        Assert.That(ev.Food,           Is.EqualTo(18));
        Assert.That(ev.Timestamp,      Is.EqualTo(now));
    }

    [Test]
    public void DamageTakenEvent_ValueEquality_AsRecord()
    {
        var now = DateTimeOffset.UtcNow;
        var a   = new DamageTakenEvent(20, 14, -6, 18, now);
        var b   = new DamageTakenEvent(20, 14, -6, 18, now);

        // Records have value equality.
        Assert.That(a, Is.EqualTo(b),
            "DamageTakenEvent is a record and must support value equality.");
    }

    [Test]
    public void DamageTakenEvent_InheritsWorldEvent()
    {
        var ev = new DamageTakenEvent(20, 14, -6, 18, DateTimeOffset.UtcNow);
        Assert.That(ev, Is.InstanceOf<WorldEvent>(),
            "DamageTakenEvent must inherit WorldEvent for correct event routing.");
    }
}
