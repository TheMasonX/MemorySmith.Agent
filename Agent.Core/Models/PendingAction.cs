namespace Agent.Core;

/// <summary>
/// Sprint 25 P0-D: Tracks the lifecycle of a dispatched action from dispatch
/// through acknowledgment/completion/failure/timeout.
///
/// Replaces the implicit "dispatched = done" assumption identified by both
/// external audits as the #1 reliability gap. Each dispatched action now has
/// a unique <see cref="CorrelationId"/> echoed by the Node.js adapter in its
/// result event, enabling end-to-end lifecycle tracking.
/// </summary>
public sealed record PendingAction(
    Guid CorrelationId,
    string ToolName,
    DateTimeOffset DispatchedAt,
    ActionLifecycle State)
{
    /// <summary>
    /// Transitions this pending action to a new lifecycle state.
    /// Returns a new record (records are immutable).
    /// </summary>
    public PendingAction WithState(ActionLifecycle newState) =>
        this with { State = newState };
}
