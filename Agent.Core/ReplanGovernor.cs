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
/// reaches <see cref="_threshold"/> (3), the governor transitions to STALLED.</para>
///
/// <para><b>STALLED state:</b> <see cref="Evaluate"/> returns <see cref="ReplanVerdict.Stalled"/>
/// until one of three recovery conditions is met:
/// <list type="number">
///   <item><see cref="RecordProgress"/> — inventory/position change resets the governor.</item>
///   <item><see cref="Reset"/> — new goal or user command resets the governor.</item>
///   <item>Graduated retry delay [10, 20, 30, 60]s — Sprint 40 P0-C replaced single 60s timeout.</item>
/// </list></para>
///
/// <para>Council confidence: 70% (Seat 3: 85%, Seat 1: 55%).
/// Full 4-state governor (PLANNING/EXECUTING/BACKING_OFF/STALLED) deferred to Sprint 20.</para>
/// </summary>
public sealed class ReplanGovernor : IReplanGovernor
{
    private const int Default_threshold = 3;

    /// <summary>
    /// Graduated stall recovery delays in seconds.
    /// Sprint 52: reduced from [10, 20, 30, 60] to [5, 10, 20, 30] for faster recovery.
    /// </summary>
    private static readonly int[] DefaultStallGraduatedDelaysSec = [5, 10, 20, 30];

    private readonly int _threshold;
    private readonly int[] _graduatedDelaysSec;
    private readonly ITimeProvider _timeProvider;
    private readonly object _lock = new();

    private string? _lastFingerprint;
    private int _identicalPlanCount;
    private bool _isStalled;
    private DateTimeOffset _stalledAt = DateTimeOffset.MinValue;
    private int _stallAttempt; // counts consecutive stalls for graduated backoff

    /// <summary>
    /// Creates a governor with configurable thresholds. Defaults are suitable for
    /// production (3 identical plans, graduated 10→20→30→60s recovery).
    /// Tests can override for speed.
    /// </summary>
    public ReplanGovernor(
        int identicalPlanThreshold = Default_threshold,
        int[]? stallGraduatedDelaysSec = null,
        ITimeProvider? timeProvider = null)
    {
        _threshold = identicalPlanThreshold;
        _graduatedDelaysSec = stallGraduatedDelaysSec ?? DefaultStallGraduatedDelaysSec;
        _timeProvider = timeProvider ?? SystemTimeProvider.Instance;
    }

    /// <summary>
    /// Current stall recovery delay based on attempt count.
    /// Returns the graduated delay for the current stall attempt, capped at the last value.
    /// Uses <c>_stallAttempt - 1</c> (clamped at 0) because <c>_stallAttempt</c> is
    /// incremented when the stall is declared, so the first stall reads index 0 = 5s.
    /// Sprint 59 (TSK-0342): fixed off-by-one that skipped tier 0 (5s).
    /// </summary>
    public TimeSpan CurrentStallDelay
    {
        get
        {
            lock (_lock)
            {
                var idx = Math.Min(Math.Max(0, _stallAttempt - 1), _graduatedDelaysSec.Length - 1);
                return TimeSpan.FromSeconds(_graduatedDelaysSec[idx]);
            }
        }
    }

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
                // Auto-recovery: allow one retry after graduated timeout.
                // Sprint 59 (TSK-0342): do NOT reset _stallAttempt on auto-recovery —
                // only RecordProgress/Reset should clear it so backoff escalates:
                // 5→10→20→30→30s instead of resetting to 5s every cycle.
                var idx = Math.Min(Math.Max(0, _stallAttempt - 1), _graduatedDelaysSec.Length - 1);
                var timeout = TimeSpan.FromSeconds(_graduatedDelaysSec[idx]);
                if ((_timeProvider.UtcNow - _stalledAt) >= timeout)
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
                if (_identicalPlanCount >= _threshold)
                {
                    _isStalled = true;
                    _stalledAt = _timeProvider.UtcNow;
                    _stallAttempt++; // increment so next recovery uses longer delay
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
            _stallAttempt = 0;
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
            _stallAttempt = 0;
        }
    }

    /// <inheritdoc/>
    public bool TryAutoRecover()
    {
        lock (_lock)
        {
            if (!_isStalled) return false;
            // Sprint 59 (TSK-0342): use _stallAttempt - 1 (clamped) so the first
            // stall reads tier 0 (5s), and auto-recovery does NOT reset the attempt
            // counter — only RecordProgress/Reset should clear it so backoff escalates.
            var idx = Math.Min(Math.Max(0, _stallAttempt - 1), _graduatedDelaysSec.Length - 1);
            var timeout = TimeSpan.FromSeconds(_graduatedDelaysSec[idx]);
            if ((_timeProvider.UtcNow - _stalledAt) >= timeout)
            {
                _isStalled = false;
                _identicalPlanCount = 1; // start at 1 so first retry doesn't immediately re-stall
                _lastFingerprint = null; // clear fingerprint so Evaluate starts fresh
                // Note: _stallAttempt intentionally NOT reset here — only RecordProgress/Reset
                // clears the counter so graduated backoff (5→10→20→30s) actually escalates.
                return true;
            }
            return false;
        }
    }
}
