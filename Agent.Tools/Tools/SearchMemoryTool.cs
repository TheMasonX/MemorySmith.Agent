namespace Agent.Tools;

using Agent.Core;
using System.Text.Json;

/// <summary>
/// Searches the world knowledge base for observations, notes, and other in-world context.
/// </summary>
public sealed class SearchMemoryTool : ITool
{
    private readonly IMemoryGateway _memory;

    public SearchMemoryTool(IMemoryGateway memory) => _memory = memory;

    public string Name => "SearchMemory";

    public string Description => "Searches the world knowledge base for spatial observations, block data, biome notes, and in-world exploration history. Routes to the world KB instance (see WorldKbUrl in appsettings).";

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
                    : throw new ArgumentException("SearchMemory requires a 'query' parameter.");
        var limit = arguments.TryGetProperty("limit", out var l) && l.TryGetInt32(out var parsedLimit)
            ? Math.Max(1, parsedLimit)
            : 10;

        var results = await _memory.SearchAsync(query!, ct).ConfigureAwait(false);
        var limitedResults = results.Take(limit).ToList();
        var bestPageId = limitedResults.FirstOrDefault()?.PageId;

        return new ToolResult(
            true,
            $"Found {limitedResults.Count} result(s).",
            new Dictionary<string, object?>
            {
                ["query"] = query,
                ["results"] = limitedResults,
                ["bestPageId"] = bestPageId,
                ["count"] = limitedResults.Count,
            });
    }
}
