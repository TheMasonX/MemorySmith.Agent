namespace WebUI.Blazor.Dashboard.Contracts;

public sealed record AgentStatusSnapshot(
    string State,
    double Health,
    double Food,
    PositionSnapshot Position,
    int ConsecutiveFailures);
