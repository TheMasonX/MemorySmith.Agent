namespace WebUI.Blazor.Managers;

using Agent.Core;
using Agent.Core.Runtime;
using Agent.Tools;
using Microsoft.Extensions.Logging;
using System.Text.Json;

/// <summary>
/// Sprint 39 P2: Concrete implementation of <see cref="IExecutionManager"/>.
///
/// Wraps <see cref="IToolCaller"/> to dispatch a single <see cref="ActionData"/> and
/// return a structured <see cref="ActionOutcome"/>.
///
/// Design note: ActionData.Arguments is Dictionary&lt;string,object?&gt; but
/// IToolCaller.CallWithOutcomeAsync expects JsonElement. This class performs a
/// JSON round-trip (serialize → parse → JsonElement) for the conversion.
/// Sprint 40 may introduce a direct ActionData→JsonElement path to avoid the allocation.
///
/// GoalId: defaults to Guid.Empty at this interface boundary. Call
/// <see cref="SetCurrentGoal"/> when the active goal changes so ActionOutcomes
/// carry the correct GoalId for correlation and journal logging.
///
/// Sprint 40 target: AgentBackgroundService.DispatchActionsAsync inner loop delegates here.
/// </summary>
public sealed class ExecutionManagerImpl : IExecutionManager
{
    private readonly IToolCaller _toolCaller;
    private readonly ILogger<ExecutionManagerImpl> _logger;

    private Guid _currentGoalId = Guid.Empty;

    public ExecutionManagerImpl(IToolCaller toolCaller, ILogger<ExecutionManagerImpl> logger)
    {
        _toolCaller = toolCaller;
        _logger     = logger;
    }

    /// <summary>
    /// Notifies this manager that the active goal has changed.
    /// Call from AgentBackgroundService.SetGoal (Sprint 40).
    /// </summary>
    public void SetCurrentGoal(IGoal? goal) =>
        _currentGoalId = goal?.Id ?? Guid.Empty;

    /// <inheritdoc/>
    public async Task<ActionOutcome> DispatchAsync(
        ActionData action,
        CancellationToken ct = default)
    {
        _logger.LogDebug("[execution] dispatching tool {Tool}", action.Tool);

        JsonElement argsElement;
        try
        {
            var json = JsonSerializer.Serialize(action.Arguments);
            argsElement = JsonDocument.Parse(json).RootElement;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[execution] failed to serialize arguments for tool {Tool}", action.Tool);
            return ActionOutcome.Failed(_currentGoalId, action.Tool,
                $"Argument serialization error: {ex.Message}");
        }

        var (_, outcome) = await _toolCaller.CallWithOutcomeAsync(
            _currentGoalId, action.Tool, argsElement, ct);
        return outcome;
    }
}
