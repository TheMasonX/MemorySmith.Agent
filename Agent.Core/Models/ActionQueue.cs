namespace Agent.Core;

using System.Collections.Concurrent;

/// <summary>
/// FIFO queue of pending tool invocations for the current plan.
/// The agent loop dequeues one action per tick and dispatches it
/// to the ToolDispatcher. Cleared when the plan is invalidated.
///
/// Sprint 12: switched from <see cref="Queue{T}"/> to
/// <see cref="ConcurrentQueue{T}"/> because <see cref="AgentBackgroundService"/>
/// accesses the queue from two concurrent tasks:
///   - <c>DispatchActionsAsync</c>: EnqueueAll, Dequeue, IsEmpty
///   - <c>ChatConsumerAsync</c>:    Enqueue (chat responses), Clear (via SetGoal / CancelGoal)
/// The non-thread-safe Queue caused corruption that manifested as an infinite
/// planning loop — the queue appeared empty immediately after EnqueueAll due
/// to concurrent Clear calls racing with the enqueue.
/// </summary>
public sealed class ActionQueue
{
    private readonly ConcurrentQueue<ActionData> _queue = new();

    public int  Count   => _queue.Count;
    public bool IsEmpty => _queue.IsEmpty;

    public void Enqueue(ActionData action) => _queue.Enqueue(action);

    public void EnqueueAll(IEnumerable<ActionData> actions)
    {
        foreach (var a in actions)
            _queue.Enqueue(a);
    }

    public ActionData? Dequeue() => _queue.TryDequeue(out var a) ? a : null;
    public ActionData? Peek()    => _queue.TryPeek(out var a)    ? a : null;

    public void Clear() => _queue.Clear();
}
