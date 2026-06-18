namespace Agent.Core;

/// <summary>
/// Sprint 19: Minimal replan governor — prevents infinite replan loops
/// when the environment offers no progress.
///
/// <para>States:</para>
/// <list type="bullet">
///   <item>ACTIVE — replanning allowed. Tracks plan fingerprints.</item>
///   <item>STALLED — replanning suppressed until progress, reset, or timeout.</item>
/// </list>
///
/// Consumers call <see cref="Evaluate"/> after each plan creation with the plan's
/// fingerprint. If 3+ consecutive identical fingerprints arrive with no intervening
/// <see cref="RecordProgress"/>, the governor transitions to STALLED and returns
/// <see cref="ReplanVerdict.Stalled"/>.
///
/// Recovery from STALLED: <see cref="RecordProgress"/> (inventory or position change),
/// <see cref="Reset"/> (new goal), or automatic 60-second timeout.
/// </summary>
public interface IReplanGovernor
{
    /// <summary>
    /// Evaluate a plan fingerprint and return a verdict.
    /// Call AFTER creating a plan, BEFORE enqueueing its actions.
    /// </summary>
    ReplanVerdict Evaluate(string planFingerprint);

    /// <summary>
    /// Signal that meaningful progress was made (inventory change, position delta,
    /// successful progress-signal tool). Resets the identical-plan counter and
    /// clears STALLED state if active.
    /// </summary>
    void RecordProgress();

    /// <summary>
    /// Hard reset — called on new goal or goal cancellation.
    /// Clears all tracking state and returns to ACTIVE.
    /// </summary>
    void Reset();

    /// <summary>True when the governor is in STALLED state.</summary>
    bool IsStalled { get; }
}

/// <summary>Verdict returned by <see cref="IReplanGovernor.Evaluate"/>.</summary>
public enum ReplanVerdict
{
    /// <summary>Plan is distinct or governor is active — proceed with enqueueing.</summary>
    Proceed,

    /// <summary>3+ identical plans with no progress — replanning is suppressed.</summary>
    Stalled,
}
