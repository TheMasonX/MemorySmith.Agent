namespace WebUI.Blazor.Dashboard.Contracts;

public sealed record QueueActionSnapshot(
    string Tool,
    IReadOnlyDictionary<string, string> Arguments);
