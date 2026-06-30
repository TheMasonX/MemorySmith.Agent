namespace Agent.Core;

/// <summary>
/// Models what the agent is currently capable of doing — game mode,
/// available tools, and any restrictions that affect planning decisions.
///
/// The planner reads this to decide whether a goal is feasible and which
/// actions are available. In creative mode, for example, gathering goals
/// for spawnable items should be rejected (the item can be spawned directly).
///
/// Sprint 57: Introduced as part of the ExecutionContext architecture.
/// Sprint 59 target: full integration with ActionRegistry.
/// </summary>
/// <param name="GameMode">The current Minecraft game mode (survival, creative, etc.).</param>
/// <param name="CanSpawnItems">True when the agent can create items directly (creative mode).</param>
/// <param name="CanFly">True when flight is available (creative/spectator).</param>
/// <param name="IsInvulnerable">True when the agent cannot take damage.</param>
public sealed record ExecutionCapabilities(
    string? GameMode,
    bool CanSpawnItems,
    bool CanFly,
    bool IsInvulnerable)
{
    /// <summary>Default capabilities for an unknown / disconnected state.</summary>
    public static readonly ExecutionCapabilities Unknown = new(null, false, false, false);

    /// <summary>Typical survival-mode capabilities.</summary>
    public static readonly ExecutionCapabilities Survival = new("survival", false, false, false);

    /// <summary>Typical creative-mode capabilities.</summary>
    public static readonly ExecutionCapabilities Creative = new("creative", true, true, true);

    /// <summary>Derives capabilities from a WorldState snapshot.</summary>
    public static ExecutionCapabilities FromWorldState(WorldState state)
    {
        var isCreative = state.IsCreativeMode;
        return new ExecutionCapabilities(
            GameMode: state.GameMode,
            CanSpawnItems: isCreative,
            CanFly: isCreative,
            IsInvulnerable: isCreative);
    }
}
