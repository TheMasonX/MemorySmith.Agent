namespace Agent.Planning;

using Agent.Planning.Llm;

/// <summary>
/// Sliding-window rate limiter for LLM chat evaluations.
///
/// Enforces two independent limits configured via <see cref="ChatOptions"/>:
/// - Per-player cooldown: minimum N seconds between calls for the same player.
/// - Global throughput: at most M calls per rolling 60-second window.
///
/// Defaults (from <see cref="ChatOptions"/>): 3 s per-player, 5 per minute globally.
///
/// When either limit is exceeded the caller must fall back to deterministic
/// pattern matching — no blocking wait occurs here.
///
/// Thread-safe via a single lock on <see cref="_lock"/>.
/// Call <see cref="Prune"/> periodically to prevent unbounded dictionary growth.
/// </summary>
public sealed class ChatRateLimiter(ChatOptions options)
{
    private readonly Dictionary<string, DateTimeOffset> _playerTimes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<DateTimeOffset> _globalWindow = new();
    private readonly object _lock = new();

    /// <summary>
    /// Attempts to acquire a rate-limit token for a new LLM call.
    ///
    /// Returns true and sets <paramref name="waitTime"/> to zero when allowed.
    /// Returns false and sets <paramref name="waitTime"/> to the remaining hold-off
    /// when either limit is exceeded.
    /// </summary>
    public bool TryAcquire(string playerName, out TimeSpan waitTime)
    {
        lock (_lock)
        {
            var now       = DateTimeOffset.UtcNow;
            var cooldown  = TimeSpan.FromSeconds(options.PlayerCooldownSeconds);
            var window    = TimeSpan.FromMinutes(1);
            var windowStart = now - window;

            // ── Per-player cooldown ───────────────────────────────────────────
            if (_playerTimes.TryGetValue(playerName, out var lastPlayer))
            {
                var elapsed = now - lastPlayer;
                if (elapsed < cooldown)
                {
                    waitTime = cooldown - elapsed;
                    return false;
                }
            }

            // ── Global sliding-window ─────────────────────────────────────────
            // Evict entries older than the window
            while (_globalWindow.Count > 0 && _globalWindow.Peek() < windowStart)
                _globalWindow.Dequeue();

            if (_globalWindow.Count >= options.GlobalPerMinuteMax)
            {
                // Wait until the oldest entry in the window expires
                var oldest = _globalWindow.Peek();
                waitTime = oldest + window - now;
                return false;
            }

            // ── Acquire ───────────────────────────────────────────────────────
            _playerTimes[playerName] = now;
            _globalWindow.Enqueue(now);
            waitTime = TimeSpan.Zero;
            return true;
        }
    }

    /// <summary>
    /// Evicts per-player entries older than 5 minutes to prevent unbounded growth.
    /// Safe to call from a background timer.
    /// </summary>
    public void Prune()
    {
        lock (_lock)
        {
            var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5);
            foreach (var key in _playerTimes.Keys.Where(k => _playerTimes[k] < cutoff).ToList())
                _playerTimes.Remove(key);
        }
    }
}
