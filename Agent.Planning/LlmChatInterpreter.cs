namespace Agent.Planning;

using Agent.Core;

/// <summary>
/// <see cref="IChatInterpreter"/> implementation that combines LLM-powered evaluation
/// with pattern-matching fallback and distance-based "closest agent" filtering.
///
/// Evaluation pipeline for each incoming chat message:
///   1. Distance gate: if the player is &gt; 64 blocks away AND didn't name this bot,
///      skip without calling the LLM.
///   2. Rate limit check: if the per-player or global rate limit is exceeded, skip LLM
///      and use pattern matching.
///   3. LLM call (throttled, 5-second timeout): if the LLM is available and within rate
///      limits, call <see cref="IChatLlmClient.EvaluateAsync"/>.
///   4. Pattern-matching fallback: if LLM is null, unavailable, or returns null, fall
///      back to <see cref="ChatInterpreter"/>.
///
/// When the LLM returns <see cref="ChatIntentType.Unknown"/> (intent = "clarify"), the
/// bot asks a clarifying question in chat ("Did you mean me?") rather than ignoring.
///
/// The 64-block "closest agent" distance is intentionally generous — the agent should
/// lean toward responding rather than silently ignoring in ambiguous cases.
/// </summary>
public sealed class LlmChatInterpreter(
    IChatLlmClient? llmClient,
    ChatInterpreter patternFallback,
    ChatRateLimiter rateLimiter) : IChatInterpreter
{
    /// <summary>Maximum distance for non-addressed messages to be considered.
    /// If the player is farther than this, the LLM is skipped entirely and the
    /// pattern-matcher's "not addressed" decision is accepted without override.</summary>
    private const double MaxResponseDistance = 64.0;

    // ── IChatInterpreter ────────────────────────────────────────────────

    public async Task<ChatInterpretation> InterpretAsync(
        string username, string message, string botName,
        int onlinePlayers, Position botPosition, Position? playerPosition,
        WorldState state, CancellationToken ct = default)
    {
        // 1. Quick pattern-match: determine if clearly addressed or clearly not.
        var quick = patternFallback.Interpret(username, message, botName, onlinePlayers, state);

        // 2. Distance gate: if clearly not addressed AND player is far away, skip LLM.
        if (quick.IntentType == ChatIntentType.NotAddressed
            && playerPosition.HasValue
            && Distance(botPosition, playerPosition.Value) > MaxResponseDistance)
        {
            return quick;
        }

        // 3. If pattern matching returned a confident non-trivial result, trust it.
        //    (LLM adds value for ambiguous cases, not for clear "gather 10 wood" matches.)
        if (quick.IntentType is ChatIntentType.CreateGoal
                              or ChatIntentType.CancelGoal
                              or ChatIntentType.QueryHelp)
        {
            return quick;
        }

        // 4. LLM evaluation (rate-limited).
        if (llmClient is not null && rateLimiter.TryAcquire(username, out _))
        {
            var currentGoal = state.Facts.TryGetValue("currentGoal", out var cg) && cg is string s
                ? s : null;

            var llmResult = await llmClient.EvaluateAsync(
                botName, botPosition, username, message,
                onlinePlayers, playerPosition, currentGoal, ct);

            if (llmResult is not null)
                return llmResult;
        }

        // 5. Fallback to pattern matching.
        return quick;
    }

    public void RecordBotSpoke() => patternFallback.RecordBotSpoke();

    // ── Helpers ────────────────────────────────────────────────────

    private static double Distance(Position a, Position b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
