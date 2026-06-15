namespace Agent.Planning;

using Agent.Core;
using Agent.Planning.Goals;

/// <summary>
/// Default IGoalFactory. Maps goal names to constructor delegates.
/// Add new predefined goals here when implementing them.
/// </summary>
public sealed class GoalFactory : IGoalFactory
{
    private static readonly Dictionary<string, Func<IReadOnlyDictionary<string, object?>?, IGoal>>
        Creators = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GatherWood"]  = p => new GatherWoodGoal(GetInt(p, "count", 10)),
        ["SurviveNight"] = _ => new SurviveNightGoal(),
    };

    public IReadOnlyList<string> RegisteredGoals => [.. Creators.Keys];

    public IGoal? Create(
        string goalName,
        IReadOnlyDictionary<string, object?>? parameters = null) =>
        Creators.TryGetValue(goalName, out var create) ? create(parameters) : null;

    private static int GetInt(
        IReadOnlyDictionary<string, object?>? p, string key, int defaultValue) =>
        p?.TryGetValue(key, out var v) == true
            ? v switch
            {
                int i     => i,
                long l    => (int)l,
                string s when int.TryParse(s, out var parsed) => parsed,
                _         => defaultValue,
            }
            : defaultValue;
}
