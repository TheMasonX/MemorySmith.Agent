namespace WebUI.Blazor.Managers;

using Agent.Core;
using Agent.Core.Runtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using WebUI.Blazor.Dashboard;

/// <summary>
/// Sprint 39 P2+: Concrete implementation of <see cref="IDashboardPublisher"/>.
///
/// Sends agent status updates to connected dashboard clients via SignalR
/// (<see cref="DashboardHubEvents.SnapshotUpdated"/>). Reads the current world
/// state from <see cref="IStateManager"/> to populate health, food, position,
/// and inventory.
///
/// Goal name is set externally via <see cref="SetCurrentGoal"/> — called by
/// AgentBackgroundService.SetGoal until the full ABS decomposition completes.
/// </summary>
public sealed class DashboardPublisherImpl : IDashboardPublisher
{
    private readonly IHubContext<AgentHub>? _hubContext;
    private readonly IStateManager          _stateManager;
    private readonly ILogger<DashboardPublisherImpl> _logger;

    /// <summary>Current goal name, set by AgentBackgroundService.SetGoal.</summary>
    private string? _currentGoalName;
    /// <summary>Current goal description, set by AgentBackgroundService.SetGoal.</summary>
    private string? _currentGoalDescription;
    /// <summary>Running count of consecutive failures, set externally.</summary>
    private int _consecutiveFailures;
    /// <summary>Sprint 55 Wave C: observed entities for dashboard display.</summary>
    private IReadOnlyList<ObservedEntityDto>? _nearbyEntities;
    /// <summary>Sprint 55 Wave C: block below the bot's feet.</summary>
    private string? _blockBelow;

    public DashboardPublisherImpl(
        IHubContext<AgentHub>?          hubContext,
        IStateManager                   stateManager,
        ILogger<DashboardPublisherImpl> logger)
    {
        _hubContext   = hubContext;
        _stateManager = stateManager;
        _logger       = logger;
    }

    /// <summary>
    /// Sprint 55 Wave C: updates the observed entities for the next dashboard publish.
    /// </summary>
    public void SetNearbyEntities(IReadOnlyList<ObservedEntityDto>? entities)
    {
        _nearbyEntities = entities;
    }

    /// <summary>
    /// Sprint 55 Wave C: updates the block below for the next dashboard publish.
    /// </summary>
    public void SetBlockBelow(string? blockName)
    {
        _blockBelow = blockName;
    }

    /// <summary>
    /// Sets the current goal metadata so subsequent publishes include it.
    /// Call from AgentBackgroundService.SetGoal when the goal changes.
    /// </summary>
    public void SetCurrentGoal(string? goalName, string? goalDescription)
    {
        _currentGoalName        = goalName;
        _currentGoalDescription = goalDescription;
    }

    /// <summary>
    /// Sets the consecutive failure count for the status payload.
    /// Call from AgentBackgroundService after each failure/success.
    /// </summary>
    public void SetConsecutiveFailures(int count)
    {
        _consecutiveFailures = count;
    }

    /// <inheritdoc/>
    public async Task PublishStatusAsync(CancellationToken ct = default)
    {
        if (_hubContext is null)
        {
            _logger.LogDebug("[dashboard] hubContext is null — skipping publish");
            return;
        }

        try
        {
            var state = _stateManager.Current;
            var inv   = state.Inventory ?? new Dictionary<string, int>();

            var update = new AgentStatusUpdate(
                Status: state.AgentId is not null && _currentGoalName is not null
                    ? "active" : "idle",
                Goal: _currentGoalName,
                GoalDescription: _currentGoalDescription,
                Health: state.Health,
                Food: state.Food,
                X: state.Position.X,
                Y: state.Position.Y,
                Z: state.Position.Z,
                QueuedActions: 0,   // Sprint 40+: wire from ActionQueue
                ConsecutiveFailures: _consecutiveFailures,
                Inventory: inv,
                NearbyEntities: _nearbyEntities,
                BlockBelow: _blockBelow
            );

            await _hubContext.Clients.Group("dashboard")
                .SendAsync(DashboardHubEvents.SnapshotUpdated, update, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[dashboard] failed to publish status update via SignalR");
        }
    }
}
