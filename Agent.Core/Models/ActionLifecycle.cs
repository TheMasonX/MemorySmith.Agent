namespace Agent.Core;

/// <summary>
/// Sprint 25 P0-D: Lifecycle states for a dispatched action.
///
/// Tracks the progression from dispatch through completion or failure,
/// closing the "dispatched != done" gap identified by both external audits.
///
/// State transitions:
///   Dispatched → Acknowledged → Completed
///   Dispatched → Acknowledged → Failed
///   Dispatched → TimedOut (no response within timeout window)
///   Dispatched → Failed (tool returned failure result)
/// </summary>
public enum ActionLifecycle
{
    /// <summary>Action sent to the adapter; no response yet.</summary>
    Dispatched,

    /// <summary>Adapter acknowledged receipt (reserved for future wire-level ACK).</summary>
    Acknowledged,

    /// <summary>Adapter reported successful completion via result event.</summary>
    Completed,

    /// <summary>Action failed (tool error, adapter error, or exception).</summary>
    Failed,

    /// <summary>No response received within the timeout window (default 30s).</summary>
    TimedOut,
}
