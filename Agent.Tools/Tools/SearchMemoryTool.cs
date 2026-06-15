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

        return new ToolResult(true, $"Found {results.Count} result(s): {summary}",
            new Dictionary<string, object?> { ["results"] = results });
    }
}