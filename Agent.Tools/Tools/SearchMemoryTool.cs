namespace Agent.Tools;

using Agent.Core;
using Agent.Memory;

/// <summary>
/// Searches the world knowledge base for spatial observations, block data,
/// biome notes, and in-world exploration history.
/// <para>
/// Sprint 23 P0-B: routed to the world-keyed <see cref="IMemoryGateway"/> so
/// world observations (block locations, exploration notes) are kept separate
/// from agent codebase knowledge. The gateway instance is selected at DI
/// composition time by resolving the keyed singleton registered under
/// <c>"world"</c>; when that key is not configured the wiring falls back to
/// the agent KB and logs a startup warning.
/// </para>
/// </summary>
public sealed class SearchMemoryTool : ITool
{
    private readonly IMemoryGateway _memory;

    public SearchMemoryTool(IMemoryGateway memory) => _memory = memory;

    public string Name => "SearchMemory";

    public string Description => "Searches the world knowledge base for spatial observations, block data, biome notes, and in-world exploration history. Routes to the world KB instance (see WorldKbUrl in appsettings). Use GetPage to retrieve agent knowledge base entries such as sprint docs or code documentation.";

    public async Task<ToolResult> ExecuteAsync(ActionData action, CancellationToken ct)
    {
        var query = action.GetString("query")
                    ?? throw new ArgumentException("SearchMemory requires 'query' parameter.");
        var limit = action.GetInt("limit") ?? 10;

        var results = await _memory.SearchAsync(query, limit, ct).ConfigureAwait(false);
        return ToolResult.Ok(new { results });
    }
}
