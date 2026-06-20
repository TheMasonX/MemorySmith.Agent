namespace Agent.Tools;

using Agent.Core;
using Agent.Memory;
using System.Text.Json;

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
/// Sprint 30 P0-B: rewritten ExecuteAsync to JsonElement signature for ITool compliance.
/// ActionData-based signature was restored when file was decoded in Sprint 28
/// but the interface had already changed in Sprint 5.
/// </summary>
public sealed class SearchMemoryTool : ITool
{
    private readonly IMemoryGateway _memory;

    public SearchMemoryTool(IMemoryGateway memory) => _memory = memory;

    public string Name => "SearchMemory";

    public string Description => "Searches the world knowledge base for spatial observations, block data, biome notes, and in-world exploration history. Routes to the world KB instance (see WorldKbUrl in appsettings). Use GetPage to retrieve agent knowledge base entries such as sprint docs or code documentation.";

    public JsonElement InputSchema => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Search query for the world knowledge base" },
            "limit": { "type": "integer", "description": "Maximum number of results to return (default 10)" }
          },
          "required": ["query"]
        }
        """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        var query = arguments.TryGetProperty("query", out var q) ? q.GetString()
                    : throw new ArgumentException("SearchMemory requires 'query' parameter.");
        var limit = arguments.TryGetProperty("limit", out var l) && l.TryGetInt32(out var li) ? li : 10;

        var results = await _memory.SearchAsync(query!, limit, ct).ConfigureAwait(false);
        return ToolResult.Ok(new { results });
    }
}
