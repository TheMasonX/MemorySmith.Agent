namespace Agent.Core;

using System.Collections.Generic;

/// <summary>
/// FIFO queue of pending tool invocations for the current plan.
/// The agent loop dequeues one action per tick and dispatches it
/// to the ToolDispatcher. Cleared when the plan is invalidated.
///
/// TSK-0113: all operations now use the same lock for consistent semantics.
/// Previously, <c>ConcurrentQueue</c> was used with mixed lock coverage —
/// some operations were lock-free while others (EnqueueAll, ClearAndEnqueue)
/// held a lock. This meant <c>ClearAndEnqueue</c>'s atomicity guarantee was
/// weaker than callers assumed, since a concurrent <c>Enqueue</c> or <c>Clear</c>
/// could observe stale state between the clear and enqueue steps.
///
/// Fix: replaced <c>ConcurrentQueue</c> with a plain <c>Queue</c> protected
/// by a single lock for all operations. This makes every read and write
/// consistently ordered under the same lock.
/// </summary>
public sealed class ActionQueue
{
    private readonly Queue<ActionData> _queue = new();
    private readonly object _lock = new();

    public int Count
    {
        get { lock (_lock) return _queue.Count; }
    }

    public bool IsEmpty
    {
        get { lock (_lock) return _queue.Count == 0; }
    }

    public void Enqueue(ActionData action)
    {
        lock (_lock)
        {
            _queue.Enqueue(action);
        }
    }

    public void EnqueueAll(IEnumerable<ActionData> actions)
    {
        lock (_lock)
        {
            foreach (var a in actions)
                _queue.Enqueue(a);
        }
    }

    public ActionData? Dequeue()
    {
        lock (_lock)
        {
            if (_queue.Count == 0) return null;
            return _queue.Dequeue();
        }
    }

    public ActionData? Peek()
    {
        lock (_lock)
        {
            if (_queue.Count == 0) return null;
            return _queue.Peek();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _queue.Clear();
        }
    }

    /// <summary>
    /// Atomically clears any pending actions and enqueues a single priority
    /// action in one lock-protected operation.
    /// <para>
    /// Sprint 23 B-3 — used by the damage interrupt path to atomically discard
    /// the current plan's remaining actions and insert a priority GetStatus so
    /// the planner can re-evaluate against fresh health/world state.
    /// </para>
    /// <para>
    /// TSK-0113: all mutate operations now share the same lock, so this
    /// clear+enqueue is truly atomic relative to <see cref="Enqueue"/>,
    /// <see cref="Clear"/>, and all other operations.
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

    /// <summary>
    /// Sprint 35 P0-D: Async variant that sends a stop signal to the adapter
    /// BEFORE clearing the queue, ensuring in-flight JS actions are halted
    /// before new plan actions start.
    /// <para>
    /// When <paramref name="stopCallback"/> is provided (non-null), it is
    /// awaited before the lock is acquired. This allows the caller to send
    /// a "stop" WebSocket message to the adapter without blocking the lock.
    /// </para>
    /// <para>
    /// Usage in AgentBackgroundService during replan with active Dispatched entries:
    /// <code>
    /// await _actionQueue.ClearAndEnqueueAsync(priorityAction,
    ///     stopCallback: () => _bridge.SendAsync(ActionData.Stop(), ct));
    /// </code>
    /// </para>
    /// </summary>
    public async Task ClearAndEnqueueAsync(ActionData action, Func<Task>? stopCallback = null)
    {
        // Send stop BEFORE acquiring the lock so the adapter receives the signal
        // while any in-flight action is still running on the JS side.
        if (stopCallback is not null)
            await stopCallback();

        lock (_lock)
        {
            _queue.Clear();
            _queue.Enqueue(action);
        }
    }
}
