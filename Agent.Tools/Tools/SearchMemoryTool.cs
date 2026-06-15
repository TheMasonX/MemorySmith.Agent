namespace Agent.Tools;

using Agent.Core;
using System.Text.Json;

/// <summary>
/// Searches MemorySmith (memories + pages) for content matching the query.
/// Backed by IMemoryGateway — works with MockMemoryGateway in tests and
/// RestMemoryGateway in production.
/// </summary>
public sealed class SearchMemoryTool(IMemoryGateway memory) : ITool
{
    public string Name => "SearchMemory";
    public string Description => "Full-text and semantic search across MemorySmith wiki pages and memories.";
    public JsonElement InputSchema => JsonDocument.Parse(
        "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\",\"description\":\"Search query\"}},\"required\":[\"query\"]}"
    ).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetProperty("query", out var qEl))
            return new ToolResult(false, "SearchMemory requires a 'query' argument.");

        var query = qEl.GetString() ?? "";
        var results = await memory.SearchAsync(query, cancellationToken);

        var summary = results.Count == 0
            ? "No results found."
            : string.Join(", ", results.Take(5).Select(r => $"{r.PageId}({r.Score:F2})"));

        // Phase 4: write best page result into Data so AgentBackgroundService
        // can carry it forward as plan context for subsequent actions.
        var data = new Dictionary<string, object?>
        {
            ["results"] = results,
        };
        if (results.Count > 0)
        {
            // The best page result (kind=page) is the primary result for GetPageAsync calls.
            var bestPage = results.FirstOrDefault(r =>
                r.Kind.Equals("page", StringComparison.OrdinalIgnoreCase));
            if (bestPage is not null)
            {
                data["bestPageId"]  = bestPage.PageId;
                data["bestScore"]   = bestPage.Score;
                data["bestSnippet"] = bestPage.Snippet;
            }
        }

        return new ToolResult(true, $"Found {results.Count} result(s): {summary}", data);
    }
}
