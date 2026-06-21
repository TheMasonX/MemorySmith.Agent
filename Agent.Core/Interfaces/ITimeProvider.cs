namespace Agent.Core;

/// <summary>
/// Abstracts wall-clock time access to enable deterministic testing.
///
/// Sprint 27 P0-C: replaces direct <c>DateTimeOffset.UtcNow</c> calls in
/// <see cref="WebUI.Blazor.AgentBackgroundService"/> so that timing-sensitive
/// paths (damage-interrupt cooldown, replan interval, stall detection) can be
/// driven by a <see cref="FakeTimeProvider"/> in unit/integration tests rather
/// than requiring <c>Task.Delay</c> waits.
/// </summary>
public interface ITimeProvider
{
    /// <summary>Returns the current UTC time.</summary>
    DateTimeOffset UtcNow { get; }
}
