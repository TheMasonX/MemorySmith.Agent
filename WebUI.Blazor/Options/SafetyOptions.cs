namespace WebUI.Blazor.Options;

/// <summary>
/// Safety configuration for the agent runtime.
/// Bind from the "Agent:Safety" configuration section.
/// </summary>
public sealed record SafetyOptions
{
    /// <summary>
    /// When <c>true</c>, allows the LLM to dispatch commands in the
    /// <see cref="DeniedCommands"/> list (e.g. /op, /kill, /gamemode).
    /// This is an explicit opt-in for server operators who want the bot to
    /// have full command access. Default: <c>false</c>.
    ///
    /// Even when <c>false</c>, non-denied commands like /time and /give
    /// (for creative provisioning) still pass through when
    /// <c>CommandExecutionEnabled</c> is <c>true</c>.
    /// </summary>
    public bool AllowDestructiveCommands { get; init; } = false;

    /// <summary>
    /// Minecraft server commands the LLM must NEVER be allowed to dispatch,
    /// regardless of <c>CommandExecutionEnabled</c> or creative mode.
    /// This is a hard safety layer — LLM hallucination must never escalate
    /// privileges or destroy server state through the command intent path.
    ///
    /// Commands are compared case-insensitively (leading slash optional in config).
    /// When this set is empty in config, the built-in <c>DefaultDeniedCommands</c>
    /// in AgentBackgroundService is used as the fallback.
    ///
    /// Example appsettings.json:
    /// <code>
    /// "Agent": {
    ///   "Safety": {
    ///     "DeniedCommands": [
    ///       "/op", "/deop", "/kill", "/ban", "/stop"
    ///     ]
    ///   }
    /// }
    /// </code>
    /// </summary>
    public HashSet<string> DeniedCommands { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
