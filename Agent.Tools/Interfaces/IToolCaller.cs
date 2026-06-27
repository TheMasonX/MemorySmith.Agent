namespace Agent.Tools;

using Agent.Core;
using System.Text.Json;

/// <summary>
/// Dispatches a named tool call from the LLM. Validates arguments against the
/// registered tool's JSON schema before execution. Prevents unsafe raw execution.
///
/// Sprint 37 P0-B: <see cref="CallWithOutcomeAsync"/> added as a default interface
/// method so existing implementors (test doubles, mock tool callers) do not need to
/// change. <see cref="ToolDispatcher"/> provides a richer override that also journals
/// the execution path separately from the outer dispatch loop.
/// </summary>
public interface IToolCaller
{
    /// <summary>Validates and executes a named tool. Returns a failure result (never throws).</summary>
    Task<ToolResult> CallAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sprint 37 P0-B: Executes the named tool and wraps the result in an
    /// <see cref="ActionOutcome"/> for structured journal logging and LLM observation.
    ///
    /// <para>
    /// Default implementation: delegates to <see cref="CallAsync"/> and wraps the
    /// result using <see cref="ActionOutcome.Succeeded"/> / <see cref="ActionOutcome.Failed"/>.
    /// </para>
    ///
    /// <para>
    /// <see cref="ToolDispatcher"/> overrides this with a richer implementation that
    /// avoids the double-journal-entry problem (see ToolDispatcher.CallWithOutcomeAsync
    /// XML doc for details).
    /// </para>
    ///
    /// <para>
    /// NOTE: Callers should call <c>_journal?.LogOutcome(outcome)</c> explicitly after
    /// this method returns. The default implementation does NOT log to the journal —
    /// that responsibility belongs to the outer dispatch loop.
    /// </para>
    /// </summary>
    async Task<(ToolResult Result, ActionOutcome Outcome)> CallWithOutcomeAsync(
        Guid goalId, string toolName, JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var result = await CallAsync(toolName, arguments, cancellationToken);
        var outcome = result.Success
            ? ActionOutcome.Succeeded(goalId, toolName, result.Message ?? "Success")
            : ActionOutcome.Failed(goalId, toolName, result.Message ?? "Failed");
        return (result, outcome);
    }
}
