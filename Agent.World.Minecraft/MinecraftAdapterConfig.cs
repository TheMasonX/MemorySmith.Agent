namespace Agent.World.Minecraft;

/// <summary>
/// Configuration for MinecraftAdapter.
/// Bind from appsettings.json section "Agent:Minecraft".
/// </summary>
public sealed record MinecraftAdapterConfig
{
    /// <summary>WebSocket URL the adapter connects to (matches the Node.js WS server port).</summary>
    public string WebSocketUrl { get; init; } = "ws://localhost:3000";

    /// <summary>Port the Node.js WebSocket server listens on.</summary>
    public int WebSocketPort { get; init; } = 3000;

    /// <summary>Path to the MineflayerAdapter/index.js script. Auto-start is skipped if empty.</summary>
    public string NodeScriptPath { get; init; } = string.Empty;

    /// <summary>If true, MinecraftAdapter spawns the Node process automatically on ConnectAsync.</summary>
    public bool AutoStartNode { get; init; } = false;

    /// <summary>Minecraft server host for the Node.js bot.</summary>
    public string ServerHost { get; init; } = "localhost";

    /// <summary>Minecraft server port for the Node.js bot.</summary>
    public int ServerPort { get; init; } = 25565;

    /// <summary>Bot username.</summary>
    public string BotUsername { get; init; } = "AgentBot";

    /// <summary>Milliseconds to wait for the Node WebSocket server to start before giving up.</summary>
    public int NodeStartTimeoutMs { get; init; } = 10_000;

    /// <summary>
    /// Sprint 32 SEC-02: shared secret sent by the C# agent in the WebSocket handshake.
    /// Configure via env var <c>Agent__Minecraft__AdapterSecret</c> or
    /// appsettings section <c>Agent:Minecraft:AdapterSecret</c>.
    ///
    /// When null or empty, no secret is sent and the Node.js server must also have
    /// <c>WS_TOKEN</c> unset for the connection to be accepted (dev/localhost mode).
    /// Never commit a real secret value — always use environment variables.
    /// </summary>
    public string? AdapterSecret { get; init; } = null;
}
