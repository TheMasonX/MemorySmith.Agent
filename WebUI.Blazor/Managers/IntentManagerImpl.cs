namespace WebUI.Blazor.Managers;

using Agent.Core;
using Agent.Core.Runtime;
using Agent.Planning;
using Microsoft.Extensions.Logging;

/// <summary>
/// Sprint 39 P2: Concrete implementation of <see cref="IIntentManager"/>.
///
/// Bridges the general-purpose <see cref="IChatInterpreter"/> with the typed
/// <see cref="IntentManager"/> goal-request builder. This is step 1 of the
/// decomposed AgentRuntime pipeline:
///   ProcessChatAsync → IntentDraft → PlanningManager → ExecutionManager
///
/// Note on parameter mismatch: <see cref="IIntentManager.ProcessChatAsync"/> exposes
/// a slim (username, message, state) signature, while <see cref="IChatInterpreter.InterpretAsync"/>
/// needs botName, onlinePlayers, and playerPosition. This class bridges that gap:
///   botName          — stored from DI configuration (MinecraftAdapterConfig.BotUsername)
///   onlinePlayers    — defaulted to 1 (Sprint 40: wire live counter from IWorldAdapter)
///   playerPosition   — null (distance-gate degrades gracefully to name-based addressing)
///
/// Sprint 40 target: AgentBackgroundService.HandleChatEventAsync delegates here.
/// </summary>
public sealed class IntentManagerImpl : IIntentManager
{
    private readonly IChatInterpreter _chatInterpreter;
    private readonly IntentManager    _goalMapper;
    private readonly string           _botName;
    private readonly ILogger<IntentManagerImpl> _logger;

    /// Default player count when exact live count is unavailable.
    /// Sprint 40: replace with IWorldAdapter.OnlinePlayerCount.
    private const int DefaultOnlinePlayers = 1;

    public IntentManagerImpl(
        IChatInterpreter chatInterpreter,
        IntentManager    goalMapper,
        string           botName,
        ILogger<IntentManagerImpl> logger)
    {
        _chatInterpreter = chatInterpreter;
        _goalMapper      = goalMapper;
        _botName         = botName;
        _logger          = logger;
    }

    /// <inheritdoc/>
    public Task<IntentDraft?> ProcessChatAsync(
        string username,
        string message,
        WorldState state,
        CancellationToken ct = default)
    {
        _logger.LogDebug("[intent] ProcessChatAsync user={User} bot={Bot}", username, _botName);

        return _chatInterpreter.InterpretAsync(
            username,
            message,
            _botName,
            DefaultOnlinePlayers,
            state.Position,
            playerPosition: null,
            state,
            ct);
    }

    /// <summary>
    /// Maps an <see cref="IntentDraft"/> to a typed <see cref="GoalRequest"/> for the planner,
    /// or null for intents that produce no goal (cancel, status, help, etc.).
    /// </summary>
    public GoalRequest? MapToGoalRequest(IntentDraft draft) =>
        _goalMapper.BuildGoalRequest(draft);
}
