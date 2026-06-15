namespace Agent.Core;

/// <summary>
/// FIFO queue of pending tool invocations for the current plan.
/// The agent loop dequeues one action per tick and dispatches it
/// to the ToolEngine. Cleared when the plan is invalidated.
/// </summary>
public sealed class ActionQueue
{
    private readonly Queue<ActionData> _queue = new();

    public int Count => _queue.Count;
    public bool IsEmpty => _queue.Count == 0;

    public void Enqueue(ActionData action) => _queue.Enqueue(action);
    public void EnqueueAll(IEnumerable<ActionData> actions) { foreach (var a in actions) _queue.Enqueue(a); }
    public ActionData? Dequeue() => _queue.TryDequeue(out var a) ? a : null;
    public ActionData? Peek() => _queue.TryPeek(out var a) ? a : null;
    public void Clear() => _queue.Clear();
}
