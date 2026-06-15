namespace Agent.Memory;

/// <summary>
/// Configuration for RestMemoryGateway. Bind from appsettings.json section "Agent:Memory".
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
}