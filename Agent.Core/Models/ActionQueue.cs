namespace Agent.Core;

using System.Collections.Concurrent;

/// <summary>
/// FIFO queue of pending tool invocations for the current plan.
/// The agent loop dequeues one action per tick and dispatches it
/// to the ToolDispatcher. Cleared when the plan is invalidated.
///
/// Sprint 12: switched from Queue{T} to ConcurrentQueue{T} because AgentBackgroundService
/// accesses the queue from two concurrent tasks:
///   - DispatchActionsAsync: EnqueueAll, Dequeue, IsEmpty
///   - ChatConsumerAsync:    Enqueue (chat responses), Clear (via SetGoal / CancelGoal)
/// The non-thread-safe Queue caused corruption that manifested as an infinite
/// planning loop — the queue appeared empty immediately after EnqueueAll due
/// to concurrent Clear calls racing with the enqueue.
///
/// Sprint 23: added <see cref="ClearAndEnqueue"/> for the damage interrupt path —
/// an atomic clear+enqueue that cannot be interleaved with bulk EnqueueAll calls
/// from the planner or single Enqueue calls from the chat consumer. EnqueueAll
/// is now also lock-protected so the interrupt path observes a consistent state.
/// </summary>
public sealed class ActionQueue
{
    private readonly ConcurrentQueue<ActionData> _queue = new();
    private readonly object _lock = new();

    public int  Count   => _queue.Count;
    public bool IsEmpty => _queue.IsEmpty;

    public void Enqueue(ActionData action) => _queue.Enqueue(action);

    public void EnqueueAll(IEnumerable<ActionData> actions)
    {
        lock (_lock)
        {
            foreach (var a in actions)
                _queue.Enqueue(a);
        }
    }

    public ActionData? Dequeue() => _queue.TryDequeue(out var a) ? a : null;
    public ActionData? Peek()    => _queue.TryPeek(out var a)    ? a : null;

    public void Clear() => _queue.Clear();

    /// <summary>
    /// Atomically clears any pending actions and enqueues a single priority
    /// action in one lock-protected operation.
    /// <para>
    /// Sprint 23 B-3 — used by the damage interrupt path to atomically discard
    /// the current plan's remaining actions and insert a priority GetStatus so
    /// the planner can re-evaluate against fresh health/world state.
    /// </para>
    /// <para>
    /// Without this method, a concurrent <see cref="Enqueue"/> from
    /// <c>ChatConsumerAsync</c> (or a bulk <see cref="EnqueueAll"/> from the
    /// planner) could slip between a separate <see cref="Clear"/> and the
    /// priority Enqueue, defeating the interrupt by leaving a stale chat
    /// response or partial plan ahead of the GetStatus.
    /// </para>
    /// </summary>
    /// <param name="action">The priority action to enqueue after clearing.</param>
    public void ClearAndEnqueue(ActionData action)
    {
        lock (_lock)
        {
            _queue.Clear();
            _queue.Enqueue(action);
        }
    }
}
