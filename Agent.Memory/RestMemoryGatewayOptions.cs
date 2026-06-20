namespace Agent.Memory;

/// <summary>
/// Configuration for RestMemoryGateway and MemorySmithItemRegistry.
/// Bind from appsettings.json section "Agent:Memory".
/// </summary>
public sealed record RestMemoryGatewayOptions
{
    /// <summary>Base URL of the MemorySmith instance (e.g. http://localhost:5000).</summary>
    public string BaseUrl { get; init; } = "http://localhost:5000";

    /// <summary>Optional API key sent as X-Api-Key header.</summary>
    public string? ApiKey { get; init; }

    /// <summary>HTTP request timeout in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>Default minimum role for pages created by the agent.</summary>
    public string DefaultPageRole { get; init; } = "Anonymous";

    /// <summary>TTL for MemorySmithItemRegistry in-memory cache, in seconds.</summary>
    public int ItemCacheTtlSeconds { get; init; } = 60;

    // ── World KB (Sprint 22) ──
    /// <summary>
    /// Base URL of the world-specific MemorySmith instance.
    /// <para>
    /// <strong>Sprint 23 B-1 MIGRATION NOTE:</strong> Default changed from
    /// <c>"http://127.0.0.1:6869"</c> to <see langword="null"/>. If you relied on
    /// the implicit localhost default, set <c>WorldKbUrl</c> explicitly in
    /// <c>appsettings.json</c> (path <c>Agent:Memory:WorldKbUrl</c>).
    /// </para>
    /// <para>
    /// When <see langword="null"/> or empty, the agent KB (<see cref="BaseUrl"/>)
    /// is used for world observations and a startup warning is logged so the
    /// misconfiguration is visible.
    /// </para>
    /// <para>
    /// See <c>Data/Pages/Guides/world-kb-deployment.md</c> for setup instructions
    /// (provisioning a second MemorySmith instance on a different port, schema
    /// notes, and recommended retention).
    /// </para>
    /// </summary>
    public string? WorldKbUrl { get; init; }

    /// <summary>Optional API key for the world KB instance.</summary>
    public string? WorldApiKey { get; init; }

    /// <summary>HTTP request timeout in seconds for world KB calls. Default: 30.</summary>
    public int WorldTimeoutSeconds { get; init; } = 30;
}
