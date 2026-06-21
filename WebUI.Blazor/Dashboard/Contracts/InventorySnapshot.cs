namespace WebUI.Blazor.Dashboard.Contracts;

public sealed record InventorySnapshot(
    IReadOnlyDictionary<string, int> Items);
