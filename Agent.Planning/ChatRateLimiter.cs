namespace Agent.Planning;

/// <summary>
/// Token-bucket rate limiter for LLM chat evaluation calls.
///
/// Two limits are enforced:
/// - Per-player: minimum <see cref="PlayerCooldown"/> between evaluations per player.
///   Prevents a single player from flooding the LLM with rapid messages.
/// - Global: minimum <see cref="GlobalInterval"/> between any LLM calls.
///   Prevents burst overload when many players speak at once.
///
/// When the rate limit is exceeded the caller falls back to pattern matching.
/// All operations are thread-safe via a lock on <see cref="_lock"/>.
/// </summary>
public sealed class ChatRateLimiter
{
    /// <summary>Minimum time between LLM calls for the same player.</summary>
    public static readonly TimeSpan PlayerCooldown = TimeSpan.FromSeconds(3);

    /// <summary>Minimum time between any two LLM calls across all players.</summary>
    public static readonly TimeSpan GlobalInterval = TimeSpan.FromSeconds(1);

    private readonly Dictionary<string, DateTimeOffset> _playerTimes =
        new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastGlobalCall = DateTimeOffset.MinValue;
    private readonly object _lock = new();

    /// <summary>
    /// Attempts to acquire a slot for a new LLM call.
    /// Returns true and sets <paramref name="waitTime"/> to zero if allowed;
    /// returns false and sets <paramref name="waitTime"/> to how long to wait.
    /// </summary>
    public bool TryAcquire(string playerName, out TimeSpan waitTime)
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;

            // Per-player check
            if (_playerTimes.TryGetValue(playerName, out var lastPlayer))
            {
                var playerElapsed = now - lastPlayer;
                if (playerElapsed < PlayerCooldown)
                {
                    waitTime = PlayerCooldown - playerElapsed;
                    return false;
                }
            }

            // Global check
            var globalElapsed = now - _lastGlobalCall;
            if (globalElapsed < GlobalInterval)
            {
                waitTime = GlobalInterval - globalElapsed;
                return false;
            }

            // Acquire
            _playerTimes[playerName] = now;
            _lastGlobalCall = now;
            waitTime = TimeSpan.Zero;
            return true;
        }
    }

    /// <summary>Clears per-player history older than 5 minutes to prevent unbounded growth.</summary>
    public void Prune()
    {
        lock (_lock)
        {
            var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5);
            foreach (var key in _playerTimes.Keys.ToList())
                if (_playerTimes[key] < cutoff)
                    _playerTimes.Remove(key);
        }
    }
}
