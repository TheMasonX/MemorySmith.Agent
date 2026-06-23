namespace WebUI.Blazor.Managers;

using Agent.Core;
using Agent.Core.Runtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

/// <summary>
/// Sprint 39 P2: Concrete implementation of <see cref="IDashboardPublisher"/>.
///
/// Sends agent status updates to connected Blazor dashboard clients via SignalR
/// (client method: "agentStatusUpdated"). Reads the current world state from
/// <see cref="IStateManager"/> to populate health, food, position, and inventory count.
///
/// Sprint 40 target: AgentBackgroundService.PublishStatusAsync delegates here,
/// eliminating the direct IHubContext dependency in the 80KB god class.
/// When the full ABS decomposition is complete, this class will also receive the
/// current IGoal so GoalName can be populated in the status payload.
/// </summary>
public sealed class DashboardPublisherImpl : IDashboardPublisher
{
    private readonly IHubContext<AgentHub>? _hubContext;
    private readonly IStateManager          _stateManager;
    private readonly ILogger<DashboardPublisherImpl> _logger;

    public DashboardPublisherImpl(
        IHubContext<AgentHub>?          hubContext,
        IStateManager                   stateManager,
        ILogger<DashboardPublisherImpl> logger)
    {
        _hubContext   = hubContext;
        _stateManager = stateManager;
        _logger       = logger;
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

            // Sprint 40: replace anonymous object with typed AgentStatusUpdate from Dtos.cs
            // once GoalName is accessible via the decomposed pipeline.
            await _hubContext.Clients.All.SendAsync("agentStatusUpdated", new
            {
                isRunning      = true,
                goalName       = (string?)null,          // Sprint 40: wire from IGoal
                health         = state.Health,
                food           = state.Food,
                posX           = state.Position.X,
                posY           = state.Position.Y,
                posZ           = state.Position.Z,
                inventoryCount = state.Inventory.Count,
                isInventoryStale = state.IsInventoryStale,
            }, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[dashboard] failed to publish status update via SignalR");
        }
    }
}
