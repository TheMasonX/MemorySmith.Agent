using Agent.Core;
using Agent.Tools;
using NUnit.Framework;
using System.Text.Json;

namespace MemorySmith.Agent.Tests;

[TestFixture]
public class CoreModelsTests
{
    [Test]
    public void WorldState_DefaultPosition_IsOrigin()
    {
        var state = new WorldState();
        Assert.Multiple(() =>
        {
            Assert.That(state.Position.X, Is.EqualTo(0));
            Assert.That(state.Position.Y, Is.EqualTo(64));
            Assert.That(state.Position.Z, Is.EqualTo(0));
            Assert.That(state.Health, Is.EqualTo(20));
        });
    }

    [Test]
    public void WorldState_With_UpdatesFact()
    {
        var state = new WorldState();
        var updated = state.With(b => b.SetFact("biome", "forest", FactSource.Observed));
        Assert.That(updated.Facts["biome"]?.ToString(), Is.EqualTo("forest"));
    }

    [Test]
    public void ActionQueue_EnqueueDequeue_WorksCorrectly()
    {
        var queue = new ActionQueue();
        var action = new ActionData { Tool = "MoveTo", Arguments = { ["x"] = (object?)10 } };
        queue.Enqueue(action);

        Assert.That(queue.Count, Is.EqualTo(1));
        var dequeued = queue.Dequeue();
        Assert.That(dequeued?.Tool, Is.EqualTo("MoveTo"));
        Assert.That(queue.IsEmpty, Is.True);
    }

    [Test]
    public void ActionQueue_EnqueueAll_AddsMultiple()
    {
        var queue = new ActionQueue();
        queue.EnqueueAll([
            new ActionData { Tool = "MoveTo" },
            new ActionData { Tool = "MineBlock" },
        ]);
        Assert.That(queue.Count, Is.EqualTo(2));
    }

    [Test]
    public void ToolResult_Success_HasCorrectFlag()
    {
        var result = new ToolResult(true, "Done");
        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Is.EqualTo("Done"));
    }

    [Test]
    public void ToolDispatcher_RegisterAndGet_FindsTool()
    {
        var dispatcher = new ToolDispatcher();
        var tool = new StubTool("TestTool");
        dispatcher.Register(tool);

        Assert.That(dispatcher.Get("TestTool"), Is.SameAs(tool));
        Assert.That(dispatcher.Get("nonexistent"), Is.Null);
    }

    [Test]
    public void ToolDispatcher_Get_IsCaseInsensitive()
    {
        var dispatcher = new ToolDispatcher();
        dispatcher.Register(new StubTool("MoveTo"));
        Assert.That(dispatcher.Get("moveto"), Is.Not.Null);
        Assert.That(dispatcher.Get("MOVETO"), Is.Not.Null);
    }
}

// ─── TSK-0115: ActionQueue concurrency and atomicity tests ──────────────────────

[TestFixture]
public class ActionQueueConcurrencyTests
{
    [Test]
    public async Task ClearAndEnqueue_IsAtomic_RelativeToConcurrentEnqueue()
    {
        // Verify that ClearAndEnqueue's lock prevents interleaving with Enqueue.
        var queue = new ActionQueue();
        queue.Enqueue(new ActionData { Tool = "MoveTo" });
        queue.Enqueue(new ActionData { Tool = "MineBlock" });

        var t1 = Task.Run(() =>
        {
            // Concurrent enqueue while ClearAndEnqueue holds the lock
            for (int i = 0; i < 10; i++)
                queue.Enqueue(new ActionData { Tool = $"Concurrent_{i}" });
        });

        var t2 = Task.Run(() =>
        {
            queue.ClearAndEnqueue(new ActionData { Tool = "GetStatus" });
        });

        await Task.WhenAll(t1, t2);

        // After ClearAndEnqueue, the queue should have GetStatus + any
        // concurrent enqueues that happened after the lock was released.
        // The key invariant: GetStatus is always first (it was the priority action).
        var first = queue.Dequeue();
        Assert.That(first?.Tool, Is.EqualTo("GetStatus"),
            "ClearAndEnqueue's priority action must be dequeued first");
    }

    [Test]
    public async Task ClearAndEnqueueAsync_StopCallbackFailure_StillClearsQueue()
    {
        // TSK-0119: stop callback failure should not prevent queue clear.
        var queue = new ActionQueue();
        queue.Enqueue(new ActionData { Tool = "MoveTo" });
        queue.Enqueue(new ActionData { Tool = "MineBlock" });

        await queue.ClearAndEnqueueAsync(
            new ActionData { Tool = "GetStatus" },
            stopCallback: () => throw new InvalidOperationException("Simulated send failure"));

        // Queue should be cleared and priority action enqueued despite the throw.
        Assert.That(queue.Count, Is.EqualTo(1));
        var first = queue.Dequeue();
        Assert.That(first?.Tool, Is.EqualTo("GetStatus"));
        Assert.That(queue.IsEmpty, Is.True);
    }

    [Test]
    public async Task ClearAndEnqueueAsync_StopCallbackSuccess_WorksNormally()
    {
        var queue = new ActionQueue();
        queue.Enqueue(new ActionData { Tool = "MoveTo" });
        var stopCalled = false;

        await queue.ClearAndEnqueueAsync(
            new ActionData { Tool = "GetStatus" },
            stopCallback: () => { stopCalled = true; return Task.CompletedTask; });

        Assert.That(stopCalled, Is.True, "Stop callback must be invoked");
        Assert.That(queue.Count, Is.EqualTo(1));
        Assert.That(queue.Dequeue()?.Tool, Is.EqualTo("GetStatus"));
    }

    [Test]
    public void ConcurrentEnqueueDequeue_DoesNotCorruptState()
    {
        // Stress test: multiple threads enqueue and dequeue concurrently.
        var queue = new ActionQueue();
        var errors = 0;

        var producers = Enumerable.Range(0, 4).Select(i => Task.Run(() =>
        {
            for (int j = 0; j < 25; j++)
                queue.Enqueue(new ActionData { Tool = $"P{i}_A{j}" });
        }));

        var consumers = Enumerable.Range(0, 2).Select(i => Task.Run(() =>
        {
            for (int j = 0; j < 50; j++)
            {
                var item = queue.Dequeue();
                if (item is not null && string.IsNullOrEmpty(item.Tool))
                    Interlocked.Increment(ref errors);
            }
        }));

        Task.WaitAll([.. producers, .. consumers]);

        // No corrupted items should have been observed
        Assert.That(errors, Is.EqualTo(0));
    }

    [Test]
    public void AllOperations_UseSameLock_ConsistentCount()
    {
        // Verify that Count, IsEmpty, and mutations all observe consistent state.
        var queue = new ActionQueue();
        Assert.That(queue.IsEmpty, Is.True);

        queue.Enqueue(new ActionData { Tool = "A" });
        queue.Enqueue(new ActionData { Tool = "B" });
        queue.Enqueue(new ActionData { Tool = "C" });
        Assert.That(queue.Count, Is.EqualTo(3));
        Assert.That(queue.IsEmpty, Is.False);

        Assert.That(queue.Peek()?.Tool, Is.EqualTo("A"));
        Assert.That(queue.Count, Is.EqualTo(3)); // Peek doesn't remove

        Assert.That(queue.Dequeue()?.Tool, Is.EqualTo("A"));
        Assert.That(queue.Count, Is.EqualTo(2));

        queue.Clear();
        Assert.That(queue.IsEmpty, Is.True);
        Assert.That(queue.Count, Is.EqualTo(0));
    }

    [Test]
    public void EnqueueAll_AddsAllActionsInOrder()
    {
        var queue = new ActionQueue();
        queue.EnqueueAll([
            new ActionData { Tool = "First" },
            new ActionData { Tool = "Second" },
            new ActionData { Tool = "Third" },
        ]);

        Assert.That(queue.Count, Is.EqualTo(3));
        Assert.That(queue.Dequeue()?.Tool, Is.EqualTo("First"));
        Assert.That(queue.Dequeue()?.Tool, Is.EqualTo("Second"));
        Assert.That(queue.Dequeue()?.Tool, Is.EqualTo("Third"));
        Assert.That(queue.IsEmpty, Is.True);
    }
}

/// <summary>Minimal ITool stub for registry tests.</summary>
file sealed class StubTool(string name) : ITool
{
    public string Name => name;
    public string Description => "stub";
    public JsonElement InputSchema => JsonDocument.Parse("{}").RootElement;
    public Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
        => Task.FromResult(new ToolResult(true));
}
