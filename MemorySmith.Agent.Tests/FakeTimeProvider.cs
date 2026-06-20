namespace MemorySmith.Agent.Tests;

using Agent.Core;

/// <summary>
/// Test-only <see cref="ITimeProvider"/> that returns a controllable time value.
///
/// Sprint 27 P0-C: enables deterministic timing tests for damage-interrupt cooldown,
/// replan interval, and stall detection without relying on <c>Task.Delay</c>.
///
/// Usage:
/// <code>
/// var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
/// var service = AgentBackgroundServiceTestHelper.BuildMinimal(adapter, journal, clock);
/// // advance time past cooldown
/// clock.Advance(TimeSpan.FromSeconds(5));
/// </code>
/// </summary>
public sealed class FakeTimeProvider : ITimeProvider
{
    private DateTimeOffset _current;

    /// <summary>Initialises the clock at <paramref name="startTime"/>.</summary>
    public FakeTimeProvider(DateTimeOffset startTime) => _current = startTime;

    /// <summary>Initialises the clock at <c>DateTimeOffset.UtcNow</c>.</summary>
    public FakeTimeProvider() : this(DateTimeOffset.UtcNow) { }

    /// <inheritdoc />
    public DateTimeOffset UtcNow => _current;

    /// <summary>Moves the clock forward by <paramref name="duration"/>.</summary>
    public void Advance(TimeSpan duration) => _current = _current.Add(duration);

    /// <summary>Sets the clock to an absolute value.</summary>
    public void Set(DateTimeOffset value) => _current = value;
}
