using Agent.Core;
using System.Runtime.CompilerServices;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// In-memory IWorldAdapter for test isolation.
/// Captures sent actions and supplies world events from a push queue.
/// </summary>
public sealed class MockWorldAdapter : IWorldAdapter
{
    private bool _connected;
    private readonly Queue<WorldEvent> _eventQueue = new();

    public bool IsConnected => _connected;
    public List<ActionData> SentActions { get; } = [];

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _connected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _connected = false;
        return Task.CompletedTask;
    }

    public Task SendActionAsync(ActionData action, CancellationToken cancellationToken = default)
    {
        SentActions.Add(action);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<WorldEvent> ReceiveEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_eventQueue.TryDequeue(out var ev))
                yield return ev;
            else
                await Task.Delay(10, cancellationToken);
        }
    }

    /// <summary>Push a world event into the receive queue.</summary>
    public void PushEvent(WorldEvent ev) => _eventQueue.Enqueue(ev);

    /// <summary>Push a typed world event by event type and payload.</summary>
    public void PushEvent(string eventType, Dictionary<string, object?> payload) =>
        _eventQueue.Enqueue(new WorldEvent(eventType, payload, DateTimeOffset.UtcNow));
}