namespace Agent.Core;

/// <summary>
/// Production <see cref="ITimeProvider"/> that delegates to <see cref="DateTimeOffset.UtcNow"/>.
/// Registered as a singleton in DI; use <see cref="Instance"/> when constructing outside DI
/// (e.g. in tests that do not need time control).
/// </summary>
public sealed class SystemTimeProvider : ITimeProvider
{
    /// <summary>Shared singleton — safe to use as a no-op default.</summary>
    public static readonly SystemTimeProvider Instance = new();

    private SystemTimeProvider() { }

    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
