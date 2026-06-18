namespace Agent.Core;

/// <summary>
/// Sprint 19: 2-state replan governor implementation.
///
/// <para>Thread-safe — all state access is under <c>_lock</c>.</para>
///
/// <para><b>Plan fingerprint:</b> computed by the caller as
/// <c>GoalName:Tool1,Tool2,Tool3</c> (goal key + ordered action type sequence).
/// Action <em>parameters</em> are excluded because Wander coordinates change each cycle
/// while the plan structure remains identical.</para>
///
/// <para><b>ACTIVE state:</b> Each call to <see cref="Evaluate"/> compares the fingerprint
/// to the previous one. If identical, an internal counter increments. When the counter
/// reaches <see cref="IdenticalPlanThreshold"/> (3), the governor transitions to STALLED.</para>
///
/// <para><b>STALLED state:</b> <see cref="Evaluate"/> returns <see cref="ReplanVerdict.Stalled"/>
/// until one of three recovery conditions is met:
/// <list type="number">
///   <item><see cref="RecordProgress"/> — inventory/position change resets the governor.</item>
///   <item><see cref="Reset"/> — new goal or user command resets the governor.</item>
///   <item>60-second timeout — auto-retry with one permitted plan attempt.</item>
/// </list></para>
///
/// <para>Council confidence: 70% (Seat 3: 85%, Seat 1: 55%).
/// Full 4-state governor (PLANNING/EXECUTING/BACKING_OFF/STALLED) deferred to Sprint 20.</para>
/// </summary>
public sealed class ReplanGovernor : IReplanGovernor
{
    /// <summary>
    /// Number of consecutive identical plan fingerprints required to trigger STALLED.
    /// </summary>
    private const int IdenticalPlanThreshold = 3;

    /// <summary>
    /// How long the governor stays in STALLED before allowing one retry attempt.
    /// </summary>
    private static readonly TimeSpan StalledRecoveryTimeout = TimeSpan.FromSeconds(60);

    private readonly object _lock = new();

    private string? _lastFingerprint;
    private int _identicalPlanCount;
    private bool _isStalled;
    private DateTimeOffset _stalledAt = DateTimeOffset.MinValue;

    /// <inheritdoc/>
    public bool IsStalled
    {
        get { lock (_lock) return _isStalled; }
    }

    /// <inheritdoc/>
    public ReplanVerdict Evaluate(string planFingerprint)
    {
        lock (_lock)
        {
            if (_isStalled)
            {
                // Auto-recovery: allow one retry after timeout
                if ((DateTimeOffset.UtcNow - _stalledAt) >= StalledRecoveryTimeout)
                {
                    _isStalled = false;
                    _identicalPlanCount = 1;
                    _lastFingerprint = planFingerprint;
                    return ReplanVerdict.Proceed;
                }
                return ReplanVerdict.Stalled;
            }

            if (string.Equals(planFingerprint, _lastFingerprint, StringComparison.Ordinal))
            {
                _identicalPlanCount++;
                if (_identicalPlanCount >= IdenticalPlanThreshold)
                {
                    _isStalled = true;
                    _stalledAt = DateTimeOffset.UtcNow;
                    return ReplanVerdict.Stalled;
                }
            }
            else
            {
                _lastFingerprint = planFingerprint;
                _identicalPlanCount = 1;
            }

            return ReplanVerdict.Proceed;
        }
    }

    /// <inheritdoc/>
    public void RecordProgress()
    {
        lock (_lock)
        {
            _identicalPlanCount = 0;
            _isStalled = false;
        }
    }

    /// <inheritdoc/>
    public void Reset()
    {
        lock (_lock)
        {
            _lastFingerprint = null;
            _identicalPlanCount = 0;
            _isStalled = false;
            _stalledAt = DateTimeOffset.MinValue;
        }
    }
}
