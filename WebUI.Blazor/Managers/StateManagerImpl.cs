namespace WebUI.Blazor.Managers;

using Agent.Core;
using Agent.Core.Runtime;
using Microsoft.Extensions.Logging;

/// <summary>
/// Sprint 39 P2: Concrete implementation of <see cref="IStateManager"/>.
///
/// Owns a <see cref="WorldState"/> read-model and applies <see cref="WorldEvent"/>
/// updates via <see cref="WorldStateProjector"/>. Thread-safe: Apply and Current
/// are protected by a lock.
///
/// Design note: WorldState is an immutable record; <see cref="Apply"/> produces a new
/// instance rather than mutating in place, so the lock scope is minimal (assignment only).
///
/// Sprint 40 target: AgentBackgroundService.ProcessEventsAsync delegates Apply calls
/// here, making _worldState a read-through property on this manager.
/// </summary>
public sealed class StateManagerImpl : IStateManager
{
    private readonly WorldStateProjector _projector = new();
    private readonly ILogger<StateManagerImpl> _logger;
    private readonly object _lock = new();

    private WorldState _current = new WorldState();

    public StateManagerImpl(ILogger<StateManagerImpl> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public WorldState Current
    {
        get { lock (_lock) return _current; }
    }

    /// <inheritdoc/>
    public void Apply(WorldEvent ev)
    {
        lock (_lock)
        {
            _current = _projector.Apply(_current, ev);
        }
        _logger.LogDebug("[state] applied {EventType}", ev.GetType().Name);
    }

    /// <summary>
    /// Replaces the entire state snapshot (e.g. on agent reconnect or goal transition).
    /// Sprint 40: ABS.SetGoal will call this when IsInventoryStale needs resetting.
    /// </summary>
    public void Reset(WorldState state)
    {
        lock (_lock)
        {
            _current = state;
        }
        _logger.LogDebug("[state] reset to provided WorldState (AgentId={AgentId})", state.AgentId);
    }

    /// <inheritdoc/>
    public ExecutionContext BuildContext(
        IGoal? goal,
        int queueDepth,
        int consecutiveFailures,
        string? lastFailureReason,
        RecoveryContext? recoveryContext = null)
    {
        var state = Current;
        var capabilities = ExecutionCapabilities.FromWorldState(state);
        return new ExecutionContext(
            goal,
            state,
            queueDepth,
            consecutiveFailures,
            lastFailureReason,
            capabilities,
            recoveryContext ?? RecoveryContext.None);
    }
}
