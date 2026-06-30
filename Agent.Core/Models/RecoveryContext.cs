namespace Agent.Core;

/// <summary>
/// Structured recovery state carried inside <see cref="ExecutionContext"/>.
///
/// Replaces the loose recovery fields in AgentBackgroundService
/// (_lastAbandonedGoalName, _lastRecoveredGoalName) with a typed record
/// that tracks error identity, attempt count, and cooldown.
///
/// Sprint 57: Introduced as part of the ExecutionContext architecture.
/// </summary>
/// <param name="LastError">The last error message received, or null.</param>
/// <param name="AttemptCount">Consecutive recovery attempts for the current error.</param>
/// <param name="LastAttemptAt">When the last recovery was attempted.</param>
/// <param name="GoalName">The goal name for which recovery was last attempted.</param>
public sealed record RecoveryContext(
    string? LastError,
    int AttemptCount,
    DateTimeOffset LastAttemptAt,
    string? GoalName)
{
    /// <summary>Empty recovery context — no errors, no attempts.</summary>
    public static readonly RecoveryContext None = new(null, 0, DateTimeOffset.MinValue, null);

    /// <summary>Maximum recovery attempts before the goal is abandoned.</summary>
    public const int MaxAttempts = 3;

    /// <summary>True when the recovery attempt limit has been exhausted.</summary>
    public bool IsExhausted => AttemptCount >= MaxAttempts;

    /// <summary>
    /// Returns a copy recording a new recovery attempt for the given error.
    /// Increments the attempt count and updates the timestamp.
    /// </summary>
    public RecoveryContext RecordAttempt(string error, DateTimeOffset now) =>
        this with
        {
            LastError = error,
            AttemptCount = AttemptCount + 1,
            LastAttemptAt = now,
            GoalName = GoalName, // preserve current goal name; set separately
        };

    /// <summary>Returns a copy targeting the given goal name.</summary>
    public RecoveryContext WithGoal(string goalName) =>
        this with { GoalName = goalName };
}
