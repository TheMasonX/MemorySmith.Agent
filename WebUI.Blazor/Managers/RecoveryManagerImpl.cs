namespace WebUI.Blazor.Managers;

using Agent.Core;
using Agent.Core.Runtime;
using Microsoft.Extensions.Logging;

/// <summary>
/// Sprint 39 P2: Concrete implementation of <see cref="IRecoveryManager"/>.
///
/// Sprint 39 stub: recovery logic remains in
/// AgentBackgroundService.TryRecoverFromGameErrorAsync until the full ABS
/// decomposition is completed in Sprint 40. This class exists to complete the
/// AgentRuntime record and make all six manager interfaces resolvable via DI.
///
/// Sprint 40 target: extract TryRecoverFromGameErrorAsync into this class,
/// eliminating the circular dependency where recovery calls SetGoal on ABS.
/// The recovery logic needs: IChatInterpreter (to parse recovery intent), GoalFactory
/// (to create the recovery goal), and the agent's SetGoal entry point — all of which
/// will be available via the injected AgentRuntime once ABS is fully decomposed.
/// </summary>
public sealed class RecoveryManagerImpl : IRecoveryManager
{
    private readonly ILogger<RecoveryManagerImpl> _logger;

    public RecoveryManagerImpl(ILogger<RecoveryManagerImpl> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Sprint 39 stub — always returns false. Recovery is handled by
    /// AgentBackgroundService.TryRecoverFromGameErrorAsync until Sprint 40.
    /// </remarks>
    public Task<bool> TryRecoverAsync(
        string errorMessage,
        WorldState state,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "[recovery] stub invoked (deferred to ABS.TryRecoverFromGameErrorAsync until Sprint 40): {Error}",
            errorMessage.Length > 80 ? errorMessage[..80] : errorMessage);
        return Task.FromResult(false);
    }

    /// <summary>
    /// Sprint 57: Structured recovery using <see cref="ExecutionContext"/>.
    /// Reads recovery context (attempt count, last error) and capabilities
    /// to make a deterministic recovery decision instead of string-parsing.
    /// </summary>
    public Task<bool> TryRecoverAsync(
        ExecutionContext context,
        CancellationToken ct = default)
    {
        var rc = context.RecoveryContext;

        if (rc.IsExhausted)
        {
            _logger.LogWarning(
                "[recovery] exhausted ({Attempts}/{Max}) for goal {Goal}: {Error}",
                rc.AttemptCount, RecoveryContext.MaxAttempts,
                context.GoalName, rc.LastError);
            return Task.FromResult(false);
        }

        _logger.LogDebug(
            "[recovery] stub invoked via ExecutionContext (attempt {Attempt}/{Max}, goal={Goal}, error={Error})",
            rc.AttemptCount, RecoveryContext.MaxAttempts,
            context.GoalName, rc.LastError);
        return Task.FromResult(false);
    }
}
