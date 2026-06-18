namespace Agent.Memory;

/// <summary>
/// Configuration for RestMemoryGateway and MemorySmithItemRegistry.
/// Bind from appsettings.json section "Agent:Memory".
/// </summary>
public sealed record RestMemoryGatewayOptions
{
    /// <summary>Base URL of the MemorySmith instance (e.g. http://localhost:5000).</summary>
    public string BaseUrl { get; init; } = "http://localhost:5000";

    /// <summary>
    /// Optional API key sent as X-Api-Key header.
    /// Required when MemorySmith is configured with ApiKey authentication.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>HTTP request timeout in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>Default minimum role for pages created by the agent.</summary>
    public string DefaultPageRole { get; init; } = "Anonymous";

    /// <summary>
    /// Time-to-live for the <see cref="MemorySmithItemRegistry"/> in-memory cache, in seconds.
    /// Each unique item ID is cached for this duration after its first successful lookup.
    /// Set to 0 to disable caching (every call issues a fresh HTTP request).
    /// Default: 60 seconds.
    /// </summary>
    public int ItemCacheTtlSeconds { get; init; } = 60;

    // ── World KB (Sprint 22) ──────────────────────────────────────────────────────────────
    // A separate MemorySmith instance dedicated to Minecraft world data
    // (block locations, exploration notes, crafting observations). Keeping world knowledge
    // isolated prevents codebase docs and council reviews from polluting the bot's
    // in-world SearchMemory results.

    /// <summary>
    /// Base URL of the world-specific MemorySmith instance.
    /// Default: http://127.0.0.1:6869 (agent KB runs on 6868 by convention).
    /// When null or empty, tools that use the world KB fall back to <see cref="BaseUrl"/>.
    /// Configure via appsettings.json: Agent:Memory:WorldKbUrl.
    /// See Data/Pages/Guides/world-kb-deployment.md for setup instructions.
    /// </summary>
    public string? WorldKbUrl { get; init; } = "http://127.0.0.1:6869";

    /// <summary>Optional API key for the world KB instance (same semantics as <see cref="ApiKey"/>).</summary>
    public string? WorldApiKey { get; init; }

    /// <summary>HTTP request timeout in seconds for world KB calls. Default: 30.</summary>
    public int WorldTimeoutSeconds { get; init; } = 30;
}
