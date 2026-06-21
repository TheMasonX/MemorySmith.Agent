using Agent.Core;

namespace MemorySmith.Agent.Tests;

[TestFixture]
public class MockWorldAdapterTests
{
    [Test]
    public async Task ConnectAsync_SetsIsConnected()
    {
        var adapter = new MockWorldAdapter();
        Assert.That(adapter.IsConnected, Is.False);
        await adapter.ConnectAsync();
        Assert.That(adapter.IsConnected, Is.True);
    }

    [Test]
    public async Task DisconnectAsync_ClearsIsConnected()
    {
        var adapter = new MockWorldAdapter();
        await adapter.ConnectAsync();
        await adapter.DisconnectAsync();
        Assert.That(adapter.IsConnected, Is.False);
    }

    [Test]
    public async Task SendActionAsync_CapturesAction()
    {
        var adapter = new MockWorldAdapter();
        var action = new ActionData { Tool = "MoveTo", Arguments = { ["x"] = (object?)10 } };
        await adapter.SendActionAsync(action);

        Assert.That(adapter.SentActions, Has.Count.EqualTo(1));
        Assert.That(adapter.SentActions[0].Tool, Is.EqualTo("MoveTo"));
    }

    [Test]
    public async Task SendActionAsync_MultipleActions_AllCaptured()
    {
        var adapter = new MockWorldAdapter();
        await adapter.SendActionAsync(new ActionData { Tool = "MoveTo" });
        await adapter.SendActionAsync(new ActionData { Tool = "MineBlock" });
        await adapter.SendActionAsync(new ActionData { Tool = "GetStatus" });

        Assert.That(adapter.SentActions, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task ReceiveEventsAsync_YieldsQueuedEvent()
    {
        var adapter = new MockWorldAdapter();
        adapter.PushEvent(new HealthEvent(18, 20, DateTimeOffset.UtcNow));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var received = new List<WorldEvent>();

        try
        {
            await foreach (var ev in adapter.ReceiveEventsAsync(cts.Token))
            {
                received.Add(ev);
                break; // got one, stop
            }
        }
        catch (OperationCanceledException) { /* timeout = nothing received */ }

        Assert.That(received, Has.Count.EqualTo(1));
        Assert.That(received[0], Is.TypeOf<HealthEvent>());
    }

    [Test]
    public async Task ReceiveEventsAsync_MultipleEvents_YieldsAllInOrder()
    {
        var adapter = new MockWorldAdapter();
        adapter.PushEvent(new SpawnEvent(new Position(0, 64, 0), 20, 20, DateTimeOffset.UtcNow));
        adapter.PushEvent(new HealthEvent(20, 20, DateTimeOffset.UtcNow));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var received = new List<Type>();

        try
        {
            await foreach (var ev in adapter.ReceiveEventsAsync(cts.Token))
            {
                received.Add(ev.GetType());
                if (received.Count >= 2) break;
            }
        }
        catch (OperationCanceledException) { }

        Assert.That(received, Is.EqualTo(new[] { typeof(SpawnEvent), typeof(HealthEvent) }));
    }
}