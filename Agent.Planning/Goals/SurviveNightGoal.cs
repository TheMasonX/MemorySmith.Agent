namespace Agent.Planning.Goals;

using Agent.Core;

/// <summary>
/// Survive the night by finding or building shelter and waiting for sunrise.
///
/// Phases: FindShelter → LightArea → WaitForSunrise
///
/// IsComplete: world time is daytime (time-of-day fact from world state)
///   OR the agent is inside a shelter.
/// HasFailed: health dropped critically during the night.
/// </summary>
public sealed class SurviveNightGoal : IGoal
{
    private const int CriticalHealthThreshold = 4;

    public string Name => "SurviveNight";
    public string Description => "Find shelter and survive until sunrise.";
    public string[] Phases => ["FindShelter", "LightArea", "WaitForSunrise"];

    public bool IsComplete(WorldState state) =>
        IsDaytime(state) || IsInShelter(state);

    public bool HasFailed(WorldState state) =>
        state.Health <= CriticalHealthThreshold;

    private static bool IsDaytime(WorldState state) =>
        state.Facts.TryGetValue("timeOfDay", out var t) &&
        t?.ToString() is string ts &&
        (ts.Equals("day", StringComparison.OrdinalIgnoreCase) ||
         ts.Equals("morning", StringComparison.OrdinalIgnoreCase));

    private static bool IsInShelter(WorldState state) =>
        state.Facts.TryGetValue("inShelter", out var s) && s is true;
}
