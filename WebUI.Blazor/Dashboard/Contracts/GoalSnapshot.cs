namespace WebUI.Blazor.Dashboard.Contracts;

public sealed record GoalSnapshot(
    string GoalName,
    string Description,
    DateTimeOffset StartedUtc);
